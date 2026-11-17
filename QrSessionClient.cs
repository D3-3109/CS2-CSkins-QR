using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CS2_CSkins_QR;

internal readonly record struct QrSessionResult(bool IsSuccess, string? QrUrl, string? ErrorMessage)
{
    public static QrSessionResult Success(string qrUrl) => new(true, qrUrl, null);

    public static QrSessionResult Failure(string errorMessage) => new(false, null, errorMessage);
}

internal sealed class QrSessionClient : IDisposable
{
    private const string PlaceholderWebUrl = "YourSkinChangeUrl";
    private const string BearerScheme = "Bearer";
    private const int MinTimeoutSeconds = 2;
    private const int MaxTimeoutSeconds = 30;
    private static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly string _baseEndpointUrl;

    private sealed class QrApiResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("qr_url")]
        public string? QrUrl { get; init; }
    }

    private QrSessionClient(string baseEndpointUrl, string apiKey, int timeoutSeconds)
    {
        _baseEndpointUrl = baseEndpointUrl;

        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = PooledConnectionLifetime,
        };

        int clampedTimeout = Math.Clamp(timeoutSeconds, MinTimeoutSeconds, MaxTimeoutSeconds);
        _httpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(clampedTimeout),
        };
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(BearerScheme, apiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public static bool TryCreate(
        CS2_CSkins_QRConfig config,
        out QrSessionClient? client,
        out string? validationError)
    {
        client = null;

        string rawUrl = config.WebUrl?.Trim() ?? string.Empty;
        bool isMissingUrl = string.IsNullOrWhiteSpace(rawUrl)
            || string.Equals(rawUrl, PlaceholderWebUrl, StringComparison.OrdinalIgnoreCase);

        if (isMissingUrl || !TryNormalizeHttpUrl(rawUrl, out string normalizedBaseUrl))
        {
            validationError = "WebUrl 未配置或不是有效的 http/https 地址";
            return false;
        }

        string apiKey = config.ApiKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            validationError = "ApiKey 未配置，无法创建二维码登录会话";
            return false;
        }

        validationError = null;
        client = new QrSessionClient(normalizedBaseUrl, apiKey, config.RequestTimeoutSeconds);
        return true;
    }

    public async Task<QrSessionResult> RequestQrAsync(ulong steamId, CancellationToken cancellationToken)
    {
        string requestUrl = $"{_baseEndpointUrl}/?qr={steamId}";

        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(
                requestUrl,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return QrSessionResult.Failure(
                    $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase ?? "Error"})");
            }

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            QrApiResponse? payload = JsonSerializer.Deserialize<QrApiResponse>(responseBody);

            if (payload is not { Success: true })
            {
                return QrSessionResult.Failure("后端返回 success=false");
            }

            if (!TryNormalizeHttpUrl(payload.QrUrl, out string validatedQrUrl))
            {
                return QrSessionResult.Failure("后端返回的 qr_url 无效");
            }

            return QrSessionResult.Success(validatedQrUrl);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return QrSessionResult.Failure("插件正在卸载，请求已取消");
        }
        catch (TaskCanceledException)
        {
            return QrSessionResult.Failure("请求换肤后端超时");
        }
        catch (HttpRequestException ex)
        {
            return QrSessionResult.Failure($"网络请求失败: {ex.Message}");
        }
        catch (JsonException ex)
        {
            return QrSessionResult.Failure($"后端响应 JSON 解析失败: {ex.Message}");
        }
        catch (ObjectDisposedException)
        {
            return QrSessionResult.Failure("HTTP 客户端已释放");
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static bool TryNormalizeHttpUrl(string? rawUrl, out string normalizedUrl)
    {
        normalizedUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        bool isHttpScheme = uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        if (!isHttpScheme)
        {
            return false;
        }

        normalizedUrl = uri.AbsoluteUri.TrimEnd('/');
        return true;
    }
}
