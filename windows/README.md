# Work Island Windows 0.1.24

Windows 11 x64 实测的 WPF/.NET 应用：屏幕工作计时、应用/可选网站规则、逻辑日与历史、主题组合、悬浮窗和任务栏通知区域模式。当前前景使用实时绘制，避免整窗缓存交接；悬停不改变手动透明度。

## 当前下载包

Release 的 Windows 附件已换为 `20261113-net48-r2` 轻量兼容版，使用系统提供的 .NET Framework 4.8，不再捆绑 .NET Desktop Runtime。安装器约 3.34 MB，免安装 ZIP 约 0.77 MB。产品版本仍为 0.1.24 / 文件版本 0.1.24.25。

免安装版解压后运行 `Start-Portable.cmd`，记录保存在旁边的 `Data`；直接运行 `WorkIsland.exe` 使用 `%LOCALAPPDATA%/WorkIsland`。升级前正常退出旧版并备份数据。

当前仓库保留原 .NET 10 源码；兼容版源码及其 `build-compat.ps1` 待收到后同步。下列构建命令针对现有 .NET 10 源码。

## 构建

需要 Windows、PowerShell 7 和 .NET SDK 10.0.401（或 global.json 允许的补丁）。项目不含远程 NuGet 包依赖。
在本目录执行：

```powershell
./build.ps1 -BuildId local-review
```

也可用 `-SdkPath` 指定包含 dotnet.exe 的 SDK 安装根目录。脚本构建并执行核心及桌面回归，再生成带本地运行时的独立目录；`-SkipWindowTests` 只适用于无交互桌面环境，不能视为完整桌面验收。

快速编译与核心测试：

```powershell
dotnet build WorkIsland.App.Tests/WorkIsland.App.Tests.csproj -c Release
dotnet run --project WorkIsland.Tests/WorkIsland.Tests.csproj -c Release -- --output test-results/core.json
```

WPF、托盘和 Win32 行为需在 Windows 交互会话验证；Mac 上可审阅全部源码，安装相应 .NET SDK 后可运行纯 Core 项目测试。

## 安装包

使用官方 Inno Setup 编译器；在 `build.ps1` 输出的干净目录上运行：

```powershell
./Installer/build-installer.ps1 -Compiler 'C:/path/to/ISCC.exe' -PayloadDir 'artifacts/WorkIsland-0.1.24-win-x64-build-local-review' -PackageName 'WorkIsland-0.1.24-win-x64-local-review-setup'
```

安装脚本当前用户安装、升级保留设置和历史，卸载保留数据。安装器拒绝把 Data、PreviewData 或本机测试数据一起打包。安装包目前没有代码签名证书。

## 数据与测试边界

普通数据目录是 `%LOCALAPPDATA%/WorkIsland`；测试、预览必须明确传入独立 `--data-dir`，不要用日常数据目录。查询与正常退出接口是 `--query-instance` 和 `--request-exit`，退出前核对 PID、启动时间与数据目录；不要强杀未知进程。

共享活动判定样例位于仓库根目录的 `Specs/`。`WorkIsland.App` / `WorkIsland.Core` 保留 Windows 交接版本的产品代码。
专用历史诊断工具位于 Tools，其显式实验入口不代表正式启动策略；可通过 ProductDirectory 参数选择已构建程序集。
