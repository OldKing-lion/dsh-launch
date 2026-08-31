# dsh-launch

Windows 桌面壳：用 WebView2 打开本机 deepseek-harness 仓库的 `pnpm dsh web` 页面。

- 关窗口收起到托盘；托盘右键「退出」会结束本窗口和它拉起的 `dsh web` 进程树（释放端口）
- 不依赖 npm 全局 `@deepseek-ai/dsh`
- 图标为黑鲸（来自社区 splash-launcher 资源）

## 要求

- Windows 10/11，已安装 Edge WebView2 Runtime（一般随 Edge 自带）
- [.NET 10 SDK](https://dotnet.microsoft.com/download)（编译）
- 本机已有 deepseek-harness git checkout，且能跑 `pnpm dsh web`

## 配置

编辑 `config.json`（发布目录或 `%LOCALAPPDATA%\dsh-repo-shell\config.json`）：

```json
{
  "repoRoot": "C:\\Repo\\K2Mobilization\\deepseek-harness",
  "port": 3080,
  "profile": "web",
  "nodeExe": ""
}
```

`nodeExe` 留空则使用 `DSH_NODE` 环境变量，再回退到 `C:\Program Files\nodejs\node.exe`。

## 编译

```bat
dotnet publish DshRepoShell.csproj -c Release -r win-x64 --self-contained false -o publish
```

双击 `publish\DshRepoShell.exe`。右键 exe → 发送到 → 桌面快捷方式。

dsh web 要求带 process token 的地址。本程序会自己拉起 `pnpm dsh web --no-open` 并打开打印出来的 `?token=` URL。

托盘「退出」或结束本进程时，会把这次拉起的 node/`dsh web` 整棵进程树一起杀掉，避免 3080 被残留占用。关窗口（右上角 X）仍然只是收起到托盘，不杀 dsh。

若 3080 已被终端里的 `pnpm dsh web` 占用，会提示你先关掉那个进程再重试——直接打开 `/` 会出 `authentication required`。
