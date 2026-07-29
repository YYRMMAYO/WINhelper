# 司南工具箱 (WINHELP)
先前公示：本软件使用AI技术进行开发，人工进行维护
![Build](https://github.com/OWNER/REPO/actions/workflows/build.yml/badge.svg)
![License](https://img.shields.io/badge/license-免费非商业-blue.svg)
![Platform](https://img.shields.io/badge/platform-Windows%20x64-lightgrey.svg)

> 一款完全免费、非盈利的 Windows 电脑辅助工具，专注于系统检测、清理优化、网络诊断与故障排查。
> 本地运行、操作安全，绝不自动删除你的个人文件。

---

## ✨ 功能特性

| 分类 | 功能 |
| --- | --- |
| 🖥️ 设备与健康 | 设备检测与硬件信息（CPU / 显卡 / 内存 / 主板 / 系统）、一键优化、健康度检测 |
| 🧹 系统维护 | 系统清理（仅清理临时文件）、启动项管理（禁用即可逆，不删除） |
| 📡 网络与排障 | 网络诊断（连通性 / 网卡 / DNS）、故障排查向导（上网 / 卡顿 / 蓝屏 / 无声 / 闪退 / 发热） |
| 📘 帮助与导览 | 新手导览（常用快捷键与小技巧） |
| 🤖 智能与效率 | AI 智能助手（调用系统工具前需确认）、共享便签、陪伴运行、录音录像 |
| 🎨 个性化 | 多套主题（默认 / 绿 / 橙 / 紫 / 粉 / 青 / 星空 / 极光）与点击动画 |

设计取向：分区清晰、层级明确、操作安全、无花哨干扰元素。

## 🛠️ 技术栈

- **框架**：WPF + .NET 10（`net10.0-windows`），C# / XAML
- **发布形态**：自包含单文件（`PublishSingleFile` + `SelfContained`），复制到任意 Windows x64 电脑即可运行，无需安装 .NET 运行时
- **构建优化**：`PublishReadyToRun` 预编译以加快冷启动（未启用单文件压缩，避免解压拖慢启动）
- **开发者**：YYRMM

> 注：项目曾用名「雅易帮 / WINHELP」，命名空间 `WINHELP` 与 `AppData` 数据目录均予以保留以兼容老用户设置。

## 🚀 快速开始

### 方式一：直接下载运行
从 [Releases](../../releases) 下载 `司南工具箱_Setup_vX.Y.Z.exe`（安装包）或 `司南工具箱.exe`（绿色单文件），在 Windows 10/11 x64 上双击即可运行。

### 方式二：从源码构建
```bash
# 需要 .NET 10 SDK 与 Windows x64
git clone <你的仓库地址>
cd WINHELP
dotnet publish WINHELP.csproj -c Release -r win-x64
```
产物位于：
```
bin/Release/net10.0-windows/win-x64/publish/司南工具箱.exe
```
将其复制为 `dist/司南工具箱.exe` 即为交付用的绿色版。

### 打包安装程序（作者本地出包）
安装包脚本 `1.9.0pre01.iss` 使用 Inno Setup 6，**其中写死了本机绝对路径**，
在其他机器上需先修改为你的路径，再用 `ISCC.exe` 编译：
```bash
"/c/Program Files (x86)/Inno Setup 6/ISCC.exe" "F:/new/WINHELP/1.9.0pre01.iss"
```
产物：`dist/BAND/司南工具箱_Setup_v4.2.0.exe`

## 🔒 隐私与安全

- 软件在**本地运行**，不会在未经你明确允许的情况下上传任何个人文件或隐私数据。
- 内置 AI 助手在调用系统工具或发送截图前，会弹出确认框，由你决定是否允许。
- 清理类功能**仅删除临时文件**，绝不在未确认的情况下删除你的个人文件；启动项以「禁用」而非「删除」处理，操作可逆。
- 所检测的硬件信息、健康评分与优化建议仅供参考，不构成专业维修或诊断意见。

## 📁 项目结构（精简）

```
WINHELP/
├─ WINHELP.csproj          # 主工程（net10.0-windows, 自包含单文件）
├─ MainWindow.xaml(.cs)    # 主窗口：左侧导航 + 右侧分区卡片
├─ HomePage / CompanionPage / Window* …  # 各功能页
├─ Cleaner.cs / HardwareInfo.cs …        # 核心逻辑
├─ GlassTheme.xaml / ClickAnim.cs        # 主题与点击动画
├─ 1.9.0pre01.iss          # Inno Setup 安装脚本（含本机绝对路径）
├─ license.txt             # 许可协议（与根 LICENSE 内容一致）
├─ .github/                # Issue/PR 模板、贡献指南、CI 工作流
└─ dist/                   # 交付产物（已被 .gitignore 忽略）
```

## 🤝 贡献

欢迎 Issue 与 Pull Request！请参阅 [CONTRIBUTING.md](.github/CONTRIBUTING.md) 了解开发环境、构建步骤与代码约定。
提交缺陷报告或功能建议时，请使用仓库提供的 Issue 模板。

## 📄 许可证

本软件为 **完全免费、非盈利** 的工具，仅供个人学习与交流使用，请勿用于商业用途。
详见 [LICENSE](LICENSE)。在不违反许可协议的前提下，你可自由复制、传播本软件，
但不得用于商业牟利。

---

感谢你使用 司南工具箱，希望它能为你的电脑使用带来便利。
