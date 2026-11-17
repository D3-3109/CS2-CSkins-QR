# CS2-CSkins-QR

一个基于 Counter-Strike 2 (CS2) 的插件，允许玩家通过扫描一次性二维码登录配套换肤网站并更换游戏内皮肤。

## 部署插件

1. 在 [GitHub Releases](https://github.com/D3-3109/CS2-CSkins-QR/releases) 页面下载最新版本的 `CS2-CSkins-QR-<version>.zip`
2. 将压缩包解压到 CS2 服务器根目录，得到 `addons/counterstrikesharp/plugins/CS2-CSkins-QR/` 目录（内含 `CS2-CSkins-QR.dll`、`CS2-CSkins-QR.deps.json`、`CS2-CSkins-QR.pdb` 三个文件）
3. 重启服务器或执行 CounterStrikeSharp 插件热重载

**要求：** 服务器需已安装 CounterStrikeSharp 运行时与 .NET 8.0 Runtime，插件不携带任何依赖文件。

## 配置插件

在 `addons/counterstrikesharp/configs/plugins/CS2-CSkins-QR/CS2-CSkins-QR.json` 配置如下内容：

```json
{
  "WebUrl": "https://your-skin-service.com",
  "ApiKey": "your-game-server-api-key",
  "RequestTimeoutSeconds": 8,
  "DisplayDurationSeconds": 60,
  "CooldownSeconds": 3,
  "Version": 3
}
```

**配置说明：**
- `WebUrl`: 皮肤更换服务的 URL 地址（自动去除末尾 `/`）
- `ApiKey`: 后端分配给当前游戏服务器的 API Key
- `RequestTimeoutSeconds`: 请求后端生成二维码的超时时间（秒，范围 2–30，默认 8）
- `DisplayDurationSeconds`: 二维码在屏幕中央自动关闭前的最长显示时间（秒，范围 10–300，默认 60）
- `CooldownSeconds`: 同一玩家重复请求二维码的冷却时间（秒，范围 1–30，默认 3）

## 后端接口

玩家执行命令时，插件会请求：

```http
GET /?qr={SteamID64}
Authorization: Bearer {ApiKey}
```

后端应创建一个短期、一次性的二维码登录会话，并返回：

```json
{
  "success": true,
  "qr_url": "https://your-skin-service.com/images_qr/sess_0123456789abcdef.png"
}
```

`qr_url` 必须是玩家客户端可以访问的绝对 `http`/`https` URL。二维码内容应只包含一次性 token，后端通过 token 查询绑定的 SteamID64，原子消费 token 后再创建网页登录会话。

## 使用方法

在游戏聊天框中输入：

```text
!cskin
```

或主动关闭二维码显示：

```text
!cskin close
```

操作流程：

1. 玩家输入 `!cskin` 命令（存活、阵亡或观战状态均可使用）
2. 屏幕中央显示专属一次性二维码及剩余秒数提示
3. 玩家使用手机扫描二维码完成网页登录并配置皮肤
4. 按鼠标右键、输入 `!cskin close` 或等待倒计时结束即可关闭二维码显示
