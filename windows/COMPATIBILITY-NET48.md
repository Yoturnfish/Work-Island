# Work Island Windows 轻量兼容版

产品版本仍为 0.1.24（文件版本 0.1.24.25）。界面、数据格式和功能使用与原版相同的源码。

此版本使用 Windows 自带的 .NET Framework 4.8，不附带现代 .NET Desktop Runtime，也不要求用户安装 .NET 10 或 SDK。标准 Windows 10 1903 及更新版本、Windows 11 已包含所需框架。旧版、被精简或移除此组件的系统不保证直接运行；安装器会检查框架，便携版由系统检查运行需求。本包面向 x64 Windows。

便携使用：解压到有写入权限的目录，双击 Start-Portable.cmd；工作记录保存在旁边的 Data 目录。直接打开 WorkIsland.exe 使用当前用户的 %LOCALAPPDATA%\WorkIsland 数据目录。请保持程序与旁边的 DLL、配置文件同处一个文件夹。

数据迁移：先在旧版正常退出并备份数据。新旧版本的数据格式相同，不要让它们同时写同一份数据。卸载程序仍保留工作记录。

开发构建：安装 .NET 10 SDK，在 Windows 上运行 build-compat.ps1；可以用 -SdkPath 指定 SDK。开发过程需要参考程序集和少量微软 NuGet 库，参考程序集不会进入发布包。默认构建并运行核心及窗口检查，再发布便携 ZIP。原 .NET 10 版本仍可通过 build.ps1 构建。

适配层保留 SHA-256 命名、命名管道的当前用户限制、独占实例、原子保存和正常退出。Framework 诊断工具的分配计数使用当前 AppDomain 指标，与现代 .NET 的全进程分配计数口径不同，不能直接比较数值。

本项目 MIT 许可见 LICENSE。附带的 Microsoft.Bcl.AsyncInterfaces、System.Buffers、System.IO.Pipelines、System.Memory、System.Numerics.Vectors、System.Runtime.CompilerServices.Unsafe、System.Text.Encodings.Web、System.Text.Json 和 System.Threading.Tasks.Extensions 使用微软 MIT 许可，见 MICROSOFT-LIBRARIES-LICENSE.txt。
