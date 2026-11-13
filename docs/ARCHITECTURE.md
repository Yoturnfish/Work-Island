# 工程结构

| 路径 | 内容 |
| --- | --- |
| `Sources/FocusIsland` | Mac 应用、SwiftUI / AppKit 界面、平台采样和 SwiftData 存储 |
| `Sources/WorkIslandCore` | Swift 规则匹配、活动判定和数据结构 |
| `Tests` | Mac 与 Swift 核心测试 |
| `windows/WorkIsland.App` | Windows WPF 界面、Win32 采样和系统集成 |
| `windows/WorkIsland.Core` | C# 活动判定、规则和本机 JSON 存储 |
| `windows/WorkIsland.Tests` | Windows 核心回归 |
| `windows/WorkIsland.App.Tests` | Windows 桌面回归工具 |
| `windows/Tools` | 图标生成与开发诊断工具 |
| `Specs` | 两端共用的 JSON 行为样例 |
| `scripts` | Mac 测试、打包和本机更新 |

两端共享行为规格，分别实现原生界面和平台适配；没有共用的运行时或数据库。

Mac：`SystemActivityDetector → TrackingEngine → SwiftDataSessionStore`，由 `IslandPanelController` 管理窗口和菜单栏。
Windows：`WorkIsland.App` 连接 `WorkIsland.Core`，正式前景使用实时 WPF 绘制；整窗原生缓存控制器为显式诊断入口。

Mac 设置使用 UserDefaults，历史使用 SwiftData。Windows 数据位于 `%LOCALAPPDATA%/WorkIsland`，使用原子 JSON 和备份。
应用规则使用各平台自身的标识：Mac bundle ID 与 Windows 可执行文件标识分开维护。

版本和发行标签按平台维护，例如 `macos-v0.8.28`、`windows-v0.1.24`。

Windows 同一套产品源码支持 .NET Framework 4.8 和 .NET 10 双目标；默认发布轻量 Framework 版，由 RuntimeCompat 适配 API 差异。build-compat.ps1 构建轻量版，build.ps1 保留现代 .NET 构建路径。
