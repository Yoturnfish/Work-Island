# 参与贡献

请从 `main` 创建分支，说明问题、改动和验证结果。界面改动附截图。

## macOS

需要 macOS 15+、Swift 6.1+ 和对应的 Xcode。

```sh
swift test
swift build -c release
```

打包使用 `sh scripts/package_app.sh`。`scripts/update_app.sh` 会替换本机 `/Applications/Work Island.app`，仅用于本机开发更新。

## Windows

需要 Windows、PowerShell 7 和 `windows/global.json` 对应的 .NET SDK。

```powershell
cd windows
dotnet build WorkIsland.App.Tests/WorkIsland.App.Tests.csproj -c Release
dotnet run --project WorkIsland.Tests/WorkIsland.Tests.csproj -c Release
```

桌面交互测试见 `windows/README.md`。两端共用 `Specs/activity-decision-cases.json`。

请保留已有数据和迁移逻辑，测试使用独立数据目录。不要提交用户数据、日志、截图中的私人内容、签名证书或构建缓存。
