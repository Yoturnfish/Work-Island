# Work Island

![Work Island 界面与功能概览](docs/assets/work-island-overview.png)

一款本地工作计时工具，提供 macOS 和 Windows 版本。
根据启用的应用、网页规则和用户活跃状态累计工作时间，在悬浮窗或菜单栏／系统托盘查看每日进度。

## 宣传片

English · [观看中文版](docs/VIDEO.zh-CN.md)

https://github.com/user-attachments/assets/28697375-347d-45ac-888c-212086264d60

## 软件介绍

在电脑前坐了十几个小时，真正用在写代码、剪视频等有长期价值的事情上的时间有多少？这是我经常面对的焦虑，也是开发 Work Island 的起点。

我把每天四小时有效工作设为自己的目标，用逐格点亮的点阵进度记录投入。每一格都是具体的积累，让我看得见一天的成果，也减少了对时间被浪费的焦虑。

通过鼠标的实际所在位置来判断你的专注内容，移开或者长时不动时进入游离状态。可以根据自己的实际工作内容，微调判断阈值。

希望它能帮助独立开发者、自由职业者和屏幕办公者，看清自己的工作投入，积累每天的完成感。

## 下载

[安装包与版本记录](https://github.com/Yoturnfish/Work-Island/releases)

| 平台 | 版本 | 系统 | 安装包 |
| --- | --- | --- | --- |
| macOS | 0.8.28（87） | macOS 15+，Apple Silicon | DMG |
| Windows | 0.1.24 | Windows 11 x64 已验证 | 安装器、免安装 ZIP |

Mac 下载后打开 DMG，将应用拖到 Applications。Windows 运行安装器，或解压免安装包后启动应用。
Mac 当前安装包使用本机签名，尚未进行 Apple 公证；Windows 安装包尚未进行代码签名。

## 功能

- 悬浮窗及菜单栏／系统托盘两种常驻模式。
- 应用和可选网页规则、工作／游离／手动暂停状态。
- 每日目标、统计日刷新时间、历史记录和时长修正。
- 颜色组合、进度条样式及中英文界面。
- Mac 提供黑色、透明、液态玻璃背景；Windows 使用原生平台材质。

## 隐私与权限

数据保存在本机，没有遥测或远程账号服务。两端的数据格式独立，目前没有跨系统同步功能。
应用读取前台或最近交互的应用标识、系统输入空闲时间，不记录按键内容。
启用网页规则后，读取支持浏览器的地址用于匹配；完整网址不写入计时历史。

Mac 的网页识别需要浏览器自动化权限。液态玻璃需要屏幕录制权限，只采集窗口背后的局部区域用于材质渲染，不保存或上传画面；黑色、透明主题不采集。

## 从源码构建

macOS 需要 Swift 6.1+ 和对应的 Xcode：

```sh
swift test
swift run FocusIsland
sh scripts/package_app.sh
```

Windows 需要 PowerShell 7 和 .NET SDK 10.0.401（允许同系列更新补丁）：

```powershell
cd windows
./build.ps1 -BuildId local
```

Windows 详细构建与安装说明见 [windows/README.md](windows/README.md)，工程结构见 [架构说明](docs/ARCHITECTURE.md)。
Mac 离线教程见 [使用教程](使用教程.html)。

## 反馈与贡献

问题和建议可提交到 [Issues](https://github.com/Yoturnfish/Work-Island/issues)。
提交代码前请阅读 [贡献说明](CONTRIBUTING.md)，安全问题见 [安全策略](SECURITY.md)。

MIT License · Yoturn。第三方组件与资源说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
