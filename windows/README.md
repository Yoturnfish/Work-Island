# Work Island Windows 0.1.24

Work Island 的 Windows 实现。当前版本保持 0.1.24 / 0.1.24.25，使用 WPF；计时、识别规则、逻辑日、历史记录、主题组合、透明模式、悬浮窗及任务栏通知区域状态图标共用现有实现。

## 轻量兼容版

当前源码支持 .NET Framework 4.8 和 .NET 10 双目标。推荐用户下载轻量版：使用标准 Windows 10 1903+、Windows 11 x64 自带的 .NET Framework，不要求用户另装 .NET 10。开发者构建需要 Windows、PowerShell 7、.NET SDK 10.0.401（或 global.json 允许的补丁），首次还原需要访问 NuGet。

```powershell
./build-compat.ps1 -BuildId local-net48
```

默认执行核心与窗口检查。没有交互桌面时可用 `-SkipWindowTests`；这不能替代完整桌面验收。可用 `-SdkPath` 指定 SDK 根目录，`-PackageSource` 指定包源。输出写入本仓库的 artifacts 和 test-results，保留既有同名产物。`build.cmd` 调用轻量构建脚本。原 .NET 10 完整版使用 build.ps1。

快速编译和核心检查：

```powershell
dotnet restore WorkIsland.Tests/WorkIsland.Tests.csproj
dotnet build WorkIsland.Tests/WorkIsland.Tests.csproj -c Release -f net48 --no-restore
./WorkIsland.Tests/bin/Release/net48/WorkIsland.Tests.exe --output test-results/core.json
```

## 安装器

需另外安装 Inno Setup 7.1.0。构建后选择无用户数据的冻结发布目录，运行：

```powershell
./Installer/build-installer.ps1 -BuiltinFramework -Compiler 'C:/path/to/ISCC.exe' -PayloadDir 'artifacts/WorkIsland-0.1.24-win-x64-build-local-net48' -PackageName WorkIsland-local-setup
```

安装器按当前用户安装，检查 Framework 4.8，保留工作记录，拒绝覆盖仍运行的程序并支持失败回滚。便携入口 Start-Portable.cmd 将数据写入旁边的 Data；直接运行 exe 使用当前用户的 LocalAppData/WorkIsland。

## 与 Mac 工程合并

这是 Windows 源码，不包含 Mac 现行工程。可将本目录作为 windows 子目录合并，保持内部目录结构；请保留 Mac 端当前源码及版本，不用历史快照覆盖。共享判定夹具位于仓库根目录的 Specs/activity-decision-cases.json。Windows 编译和桌面验收仍应在 Windows 进行。

## 验证与许可

现有发布二进制通过 149 核心、414 窗口、57 菜单、22 导航画面、4 短时资源及 12 隔离安装检查；本机 Windows 11 实测，干净 Windows 10/11 虚拟机矩阵、长时间及多屏等未在这轮补测。本次源码导出已重新编译 Framework 与 .NET 10 两种目标，并执行 Framework 核心检查。

MIT License · Yoturn，见 LICENSE。微软 NuGet 库及第三方许可说明见 MICROSOFT-LIBRARIES-LICENSE.txt、NOTICE.md。系统字体不随包分发。
