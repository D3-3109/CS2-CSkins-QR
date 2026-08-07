# CS2-CSkins-QR

一个基于 Counter-Strike 2 (CS2) 的插件，允许玩家通过扫描二维码来更换游戏内皮肤。

## 部署插件

1. 在 [GitHub Releases](https://github.com/D3-3109/CS2-CSkins-QR/releases) 页面下载最新版本的 `CS2-CSkins-QR-<version>.zip`
2. 将压缩包解压到 CS2 服务器根目录，得到 `addons/counterstrikesharp/plugins/CS2-CSkins-QR/` 目录（内含 `CS2-CSkins-QR.dll`、`CS2-CSkins-QR.deps.json`、`CS2-CSkins-QR.pdb` 三个文件）
3. 重启服务器或执行 CounterStrikeSharp 插件热重载

**要求：** 服务器需已安装 CounterStrikeSharp 运行时与 .NET 8.0 Runtime，插件不携带任何依赖文件。

## 配置插件

在 `addons/counterstrikesharp/configs/plugins/CS2-CSkins-QR` 目录下创建配置文件：

```json
{
  "WebUrl": "https://your-skin-service.com",
  "ApiKey": "your-game-server-api-key",
  "Version": 2
}
```

**配置说明：**
- `WebUrl`: 皮肤更换服务的URL地址
- `ApiKey`: 后端分配给当前游戏服务器的 API Key

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
  "qr_url": "https://your-skin-service.com/images_qr/session-id.png",
  "redirect_url": null,
  "is_new": true
}
```

`qr_url` 必须是玩家客户端可以访问的绝对 URL。二维码内容应只包含一次性 token，后端通过 token 查询绑定的 SteamID64，消费 token 后再创建网页登录会话。

## 使用方法

在游戏聊天框中输入：

```
!cskin
```

操作流程：

1. 玩家输入 `!cskin` 命令
2. 游戏中心显示专属二维码
3. 玩家使用手机扫描二维码
4. 在网页中选择想要更换的皮肤
5. 皮肤立即在游戏中生效
6. 按鼠标右键退出二维码显示
