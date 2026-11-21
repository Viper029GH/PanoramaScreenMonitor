# Panorama Screen Monitor

[Русский](../../README.md) · [English](README.en.md) · [Deutsch](README.de.md) · [Français](README.fr.md) · [Español](README.es.md) · [Português (Brasil)](README.pt-BR.md) · **简体中文** · [日本語](README.ja.md)

**在 Windows 上自动控制 TRYX Panorama 水冷屏幕的背光。**

离开电脑后，主显示器可能已经熄灭，但 Panorama 屏幕还亮着。Panorama Screen Monitor 在系统托盘中运行，空闲一段时间后关闭水冷屏幕的背光，并在你回来操作电脑时重新点亮。

**已在 TRYX Panorama `391A:1011` 和 KANALI 2.7.0 上验证。**

大约半年前，我请 KANALI 开发者添加空闲时自动关闭背光的功能。准备此版本时，该功能仍未加入，所以我写了这个工具。

[下载 Windows x64 版](https://github.com/Viper029GH/PanoramaScreenMonitor/releases/latest) · [Changelog](../../CHANGELOG.md) · [CC0 1.0](../../LICENSE)

<p align="center">
  <img src="../images/panorama-demo.png" alt="演示模式下的 Panorama Screen Monitor" width="420">
</p>

## 快速开始

**要求：** Windows x64、带有 RK PANO USB 设备（`391A:1011`）的 TRYX Panorama，以及正常运行的 KANALI。

1. 从[最新版本](https://github.com/Viper029GH/PanoramaScreenMonitor/releases/latest)下载 Windows x64 的 EXE 或 ZIP。如果下载的是 ZIP，请先解压。
2. 运行 EXE。程序无需安装，且已包含 .NET。如果 KANALI 稍后启动，程序会自动等待。
3. 默认空闲时间为 **10 分钟**。若要使用主显示器的超时设置，请展开**设置**，勾选**使用 Windows 屏幕超时设置**，然后点击**保存**。

关闭窗口后，程序会留在系统托盘中。要彻底关闭，请在托盘图标菜单中选择**退出**。启动前请先停止其他正在控制 Panorama 背光的脚本。

EXE 没有数字签名。发布文件的校验和见 `SHA256SUMS.txt`。

## 功能

| 功能 | 说明 |
| --- | --- |
| 自动控制 | 可设置 1 至 1440 分钟的空闲时间，也可使用 Windows 屏幕超时设置。程序会根据当前使用电源还是电池选择设置。设为**从不**时不会自动关闭。 |
| 手动控制与暂停 | 可通过窗口或托盘菜单开关屏幕。暂停时保持当前状态，可随时恢复自动控制。 |
| 亮度 | 屏幕开启时，可用滑块调整 0 至 100% 的亮度。程序通过回读设备确认更改。 |
| 设备诊断 | 显示型号、固件、屏幕软件和系统、USB 与 UDB 状态、亮度及最后一次成功读取的时间。 |
| Windows 集成 | 支持随 Windows 启动或启动时隐藏到托盘，主题可选浅色、深色或跟随系统。 |
| 界面语言 | 俄语、英语、德语、法语、西班牙语、巴西葡萄牙语、简体中文和日语。 |

## 工作原理

程序根据 Windows 中的最后一次输入计算空闲时间，并通过设备的标准 USB 接口以共享访问方式发送背光命令。KANALI 继续运行，Panorama Screen Monitor 从 UDB 日志中查找对应响应并验证屏幕状态。每次调整前重新读取配置，保留其他字段。若无法识别完整配置，则取消写入。

同步功能读取当前 Windows 电源计划中的显示器关闭时间。视频播放、手动关闭显示器等操作可能单独影响主显示器。

设置和诊断日志保存在 `%LOCALAPPDATA%\PanoramaScreenMonitor`。报告问题时，请附上 KANALI 版本、诊断中显示的固件版本和复现步骤。

## 从源代码构建

构建需要 **.NET 10 SDK**。项目使用 C# 和 WPF。

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
.\artifacts\publish\win-x64\PanoramaScreenMonitor.exe --demo
```

演示模式无需水冷设备即可体验界面，并模拟空闲、输入、错误和超时。运行 `PanoramaScreenMonitor.exe --self-test "<报告路径>"` 可在模拟环境中测试协议、UDB 日志处理和控制逻辑。

在项目根目录用 PowerShell 运行 `./scripts/build-release.ps1`。[脚本](../../scripts/build-release.ps1)会验证构建，并把 EXE、ZIP、.NET 许可声明和 SHA-256 校验和放入 `artifacts/releases/`。版本号在[项目文件](../../PanoramaScreenMonitor.csproj)中设置。

本工具参考已公开的设备命令和字段文档。[TRYX 传输协议](https://github.com/DXVSI/Tryx-Linux-GUI/blob/production/protocol/wire-v1/transport.proto) · [配置协议](https://github.com/DXVSI/Tryx-Linux-GUI/blob/production/protocol/wire-v1/configuration.proto)

## 许可

项目源代码及文档使用 [CC0 1.0 通用](../../LICENSE)。随附 .NET 运行时适用的条款见发布文件中的 `THIRD-PARTY-NOTICES.txt`。
