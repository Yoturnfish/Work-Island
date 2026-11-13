# 第三方组件与资源

## Windows

- 产品工程没有外部 NuGet PackageReference。Windows 发行包附带 Microsoft .NET 运行时，保留其中的 LICENSE.txt 和 ThirdPartyNotices.txt。
- Inno Setup 编译器单独安装，不进入源码仓库。
- Windows 使用系统字体名称，不随包分发 Microsoft YaHei UI、Segoe UI 或 Consolas 字体。
- Windows 的 MacSymbol.cs 记录了参考 Mac 外形绘制的图标轮廓，来源说明随代码保留。

## macOS

- SF Symbols 通过系统接口调用，不随源码分发字体或符号文件。

# Glass HQ liquid-glass

Edge-displacement profile and directional rim-lighting equations adapted from:
https://github.com/Glass-HQ/liquid-glass/tree/9f5bfc7daa7be5d20a41b94d841b8f065de655fc

Used by the native Metal glass renderer and the isolated material lab.
No React, DOM, vgpu, Electron or other upstream dependencies are bundled.

MIT License

Copyright (c) 2026 Glass HQ

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
