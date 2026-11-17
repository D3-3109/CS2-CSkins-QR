using System.Collections.Concurrent;
using System.Net;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace CS2_CSkins_QR;

public class CS2_CSkins_QR : BasePlugin, IPluginConfig<CS2_CSkins_QRConfig>
{
    private const string ChatPrefix = "[CSkins]";
    private const string ClearCenterHtmlPayload = " ";
    private const int MinDisplayDurationSeconds = 10;
    private const int MaxDisplayDurationSeconds = 300;
    private const int MinCooldownSeconds = 1;
    private const int MaxCooldownSeconds = 30;

    public override string ModuleName => "CS2_CSkins_QR";
    public override string ModuleDescription => "Change player skin by scanning a one-time QR code";
    public override string ModuleAuthor => "Jasonlee";
    public override string ModuleVersion => "1.1.0";

    public CS2_CSkins_QRConfig Config { get; set; } = new();

    private sealed record ActiveQrDisplay(string CenterHtmlPayload, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<ulong, ActiveQrDisplay> _activeDisplays = new();
    private readonly ConcurrentDictionary<ulong, byte> _inflightRequests = new();
    private readonly ConcurrentDictionary<ulong, DateTimeOffset> _cooldownUntil = new();

    private QrSessionClient? _qrClient;
    private string? _configError;
    private readonly CancellationTokenSource _lifecycleCts = new();

    public void OnConfigParsed(CS2_CSkins_QRConfig config)
    {
        Config = config;

        bool created = QrSessionClient.TryCreate(config, out QrSessionClient? nextClient, out string? validationError);
        _configError = validationError;

        QrSessionClient? previousClient = Interlocked.Exchange(ref _qrClient, nextClient);
        previousClient?.Dispose();

        if (!created)
        {
            Logger.LogError("配置校验失败: {Error}", validationError);
            return;
        }

        Logger.LogInformation("换肤服务地址已配置: {WebUrl}", config.WebUrl);
    }

    public override void Load(bool hotReload)
    {
        if (hotReload)
        {
            ResetRuntimeState();
        }

        RegisterListener<Listeners.OnTick>(OnTick);
        Logger.LogInformation("{ModuleName} v{ModuleVersion} loaded (hotReload={HotReload})", ModuleName, ModuleVersion, hotReload);
    }

    public override void Unload(bool hotReload)
    {
        _lifecycleCts.Cancel();
        _lifecycleCts.Dispose();

        ResetRuntimeState();

        QrSessionClient? client = Interlocked.Exchange(ref _qrClient, null);
        client?.Dispose();
    }

    [GameEventHandler]
    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        CCSPlayerController? player = @event.Userid;
        if (player is null || !player.IsValid || player.SteamID == 0)
        {
            return HookResult.Continue;
        }

        ClearPlayerState(player.SteamID);
        return HookResult.Continue;
    }

    [ConsoleCommand("css_cskin", "Pop the skin change QR image")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnCSkinsCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsConnectedHumanPlayer(player))
        {
            return;
        }

        CCSPlayerController targetPlayer = player!;
        ulong steamId = targetPlayer.SteamID;
        string arg = command.ArgString.Trim();

        bool wantsClose = string.Equals(arg, "close", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "off", StringComparison.OrdinalIgnoreCase);

        if (wantsClose)
        {
            if (_activeDisplays.TryRemove(steamId, out _))
            {
                targetPlayer.PrintToCenterHtml(ClearCenterHtmlPayload);
                targetPlayer.PrintToChat($"{ChatPrefix} 已关闭换肤二维码。");
            }
            return;
        }

        QrSessionClient? client = _qrClient;
        if (client is null)
        {
            Logger.LogError("无法处理 css_cskin: {Error}", _configError ?? "客户端未初始化");
            targetPlayer.PrintToChat($"{ChatPrefix} 二维码登录尚未配置，请联系服务器管理员。");
            return;
        }

        if (!_inflightRequests.TryAdd(steamId, 0))
        {
            targetPlayer.PrintToChat($"{ChatPrefix} 正在生成二维码，请稍候...");
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (_cooldownUntil.TryGetValue(steamId, out DateTimeOffset readyAt) && now < readyAt)
        {
            _inflightRequests.TryRemove(steamId, out _);
            int waitSeconds = Math.Max(1, (int)Math.Ceiling((readyAt - now).TotalSeconds));
            targetPlayer.PrintToChat($"{ChatPrefix} 操作过于频繁，请 {waitSeconds} 秒后再试。");
            return;
        }

        int cooldownSeconds = Math.Clamp(Config.CooldownSeconds, MinCooldownSeconds, MaxCooldownSeconds);
        _cooldownUntil[steamId] = now.AddSeconds(cooldownSeconds);
        targetPlayer.PrintToChat($"{ChatPrefix} 正在获取专属换肤二维码...");

        CancellationToken token = _lifecycleCts.Token;
        _ = RequestQrForPlayerAsync(client, steamId, token);
    }

    private async Task RequestQrForPlayerAsync(
        QrSessionClient client,
        ulong steamId,
        CancellationToken cancellationToken)
    {
        QrSessionResult result;
        try
        {
            result = await client.RequestQrAsync(steamId, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "获取玩家 {SteamId} 二维码时发生未预期异常", steamId);
            result = QrSessionResult.Failure("内部异常");
        }
        finally
        {
            _inflightRequests.TryRemove(steamId, out _);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        Server.NextFrame(() => ApplyQrResultOnMainThread(steamId, result));
    }

    private void ApplyQrResultOnMainThread(ulong steamId, QrSessionResult result)
    {
        CCSPlayerController? player = FindConnectedPlayer(steamId);
        if (player is null)
        {
            return;
        }

        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.QrUrl))
        {
            Logger.LogWarning("玩家 {SteamId} 获取二维码失败: {Reason}", steamId, result.ErrorMessage);
            player.PrintToChat($"{ChatPrefix} 获取二维码失败，请稍后重试。");
            return;
        }

        int durationSeconds = Math.Clamp(
            Config.DisplayDurationSeconds,
            MinDisplayDurationSeconds,
            MaxDisplayDurationSeconds);

        string encodedUrl = WebUtility.HtmlEncode(result.QrUrl);
        var display = new ActiveQrDisplay(
            $"<img src='{encodedUrl}'>",
            DateTimeOffset.UtcNow.AddSeconds(durationSeconds));

        _activeDisplays[steamId] = display;
        player.PrintToChat($"{ChatPrefix} 二维码已显示在屏幕中央，手机扫码即可换肤，按鼠标右键关闭。");
    }

    private void OnTick()
    {
        if (_activeDisplays.IsEmpty)
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (!IsConnectedHumanPlayer(player))
            {
                continue;
            }

            ulong steamId = player.SteamID;
            if (!_activeDisplays.TryGetValue(steamId, out ActiveQrDisplay? display))
            {
                continue;
            }

            bool isExpired = now >= display.ExpiresAt;
            bool isDismissedByPlayer = player.Buttons.HasFlag(PlayerButtons.Attack2);

            if (isExpired || isDismissedByPlayer)
            {
                if (_activeDisplays.TryRemove(steamId, out _))
                {
                    player.PrintToCenterHtml(ClearCenterHtmlPayload);
                    if (isDismissedByPlayer)
                    {
                        player.PrintToChat($"{ChatPrefix} 已关闭换肤二维码。");
                    }
                }
                continue;
            }

            int remainingSeconds = Math.Max(1, (int)Math.Ceiling((display.ExpiresAt - now).TotalSeconds));
            player.PrintToCenterHtml(display.CenterHtmlPayload);
            player.PrintToCenter($"扫码换肤 | 右键退出 ({remainingSeconds}s)");
        }
    }

    private void ResetRuntimeState()
    {
        _activeDisplays.Clear();
        _inflightRequests.Clear();
        _cooldownUntil.Clear();
    }

    private void ClearPlayerState(ulong steamId)
    {
        _activeDisplays.TryRemove(steamId, out _);
        _inflightRequests.TryRemove(steamId, out _);
        _cooldownUntil.TryRemove(steamId, out _);
    }

    private static CCSPlayerController? FindConnectedPlayer(ulong steamId)
    {
        foreach (CCSPlayerController candidate in Utilities.GetPlayers())
        {
            if (IsConnectedHumanPlayer(candidate) && candidate.SteamID == steamId)
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsConnectedHumanPlayer(CCSPlayerController? player)
    {
        return player is not null
            && player.IsValid
            && !player.IsBot
            && !player.IsHLTV
            && player.Connected == PlayerConnectedState.Connected
            && player.SteamID != 0;
    }
}
