# Motion evidence

`WorkIsland.exe --motion-review --data-dir <独立目录> --smoke-output <采集目录>` 采集 16 秒真实系统窗口像素和 PNG 帧时间戳。测试窗口使用自身背景与合成完成日数据，UI 动作是脚本触发。

`MotionEvidence <采集目录> <输出.mp4>` 使用 Windows Media Foundation 编码 H.264；保留每帧实际时间，右侧或底部仅补齐到偶数尺寸，不缩放、慢放或添加音频。依赖 Windows 原生接口，无 NuGet 编码库。

`VisualEvidence decode <输出.mp4> <解码目录>` 用 Windows MediaPlayer 原速播放到结束，导出 16 张原尺寸解码帧与播放结果。

接口槽位与属性 GUID 核对于 [Microsoft win32metadata SDK 头文件](https://github.com/microsoft/win32metadata/tree/main/generation/WinSDK/RecompiledIdlHeaders/um)。编码流程依据 [Microsoft Sink Writer 视频教程](https://learn.microsoft.com/en-us/windows/win32/medfound/tutorial--using-the-sink-writer-to-encode-video)。下载的头文件只用于本地核对，不加入交付包。
