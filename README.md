# 司南工具箱 (WINHELP)
先前公示：本软件使用AI技术进行开发，人工进行维护
![License](https://img.shields.io/badge/license-GPL%20v2-blue.svg)
![Platform](https://img.shields.io/badge/platform-Windows%20x64-lightgrey.svg)
![Version](https://img.shields.io/badge/version-5.6.0-brightgreen.svg)

> 一款完全免费、非盈利的 Windows 电脑辅助工具，专注于系统检测、清理优化、网络诊断与故障排查。
> 本地运行、操作安全，绝不自动删除你的个人文件。

---

## 设计风格（自 v5.3.0 起为极简风格，v5.6.0 导引栏移至右侧）

**从 v5.3.0 开始，软件全面转向极简设计；v5.6.0 进一步重构**：

- 移除 QQ 风格蓝色渐变标题栏，改为浅色玻璃标题栏（对齐 Windows 11 设计语言）
- **导引栏位于右侧**，导航项为「首字徽标 + 标题」，激活项徽标高亮为实底主题色，指示条朝向内容区
- 界面不再使用 emoji 作为装饰与卡片图标，模块统一采用「中文首字徽标」（如 `清`、`启`、`网`）
- 侧栏导航由数据驱动自动生成，仅保留最常用入口；其余功能经首页卡片或 Ctrl+K 命令面板直达
- 首页为渐变欢迎条（问候 + 日期 + 可清理统计 + 一键优化）+ 分组功能卡片，排版宽松、留白充足
- **关怀模式**：可在「个性装扮」中整体缩放界面（85%–140%），方便老人 / 低视力 / 远距离使用
- 卡片统一尺寸、实色白底、轻边框；整体克制、安静、易读

## 功能特性

| 分类 | 功能 |
| --- | --- |
| 设备与健康 | 设备检测与硬件信息（CPU / 显卡 / 内存 / 主板 / 系统）、一键优化、健康度检测 |
| 系统维护 | 系统清理（仅清理临时文件）、启动项管理（禁用即可逆，不删除）、**驱动管理**（健康检测 / 版本查看 / 备份入口 / 官网直达） |
| 网络与排障 | 网络诊断（连通性 / 网卡 / DNS）、问题解决知识库（常见故障一键修复） |
| 效率工具 | 重复文件查找（SHA-256 校验 + 回收站可恢复）、文件粉碎、截图标注、卸载残留清理、便签、录音录像、个性化调校、一键体检 |
| 智能与帮助 | AI 智能助手（调用系统工具前需确认）、电脑帮助、网站与官网、月度报告、装机助手、BUG 反馈、陪伴运行 |
| 个性化 | 多套主题（默认 / 绿 / 橙 / 紫 / 粉 / 青 / 星空 / 极光）、背景壁纸、点击动画、**关怀模式界面缩放** |

设计取向：极简、分区清晰、层级明确、操作安全、无花哨干扰元素。

## 技术栈

- **框架**：WPF + .NET 10（`net10.0-windows`），C# / XAML
- **发布形态**：自包含单文件（`PublishSingleFile` + `SelfContained`），复制到任意 Windows x64 电脑即可运行，无需安装 .NET 运行时
- **构建优化**：`PublishReadyToRun` 预编译以加快冷启动（未启用单文件压缩，避免解压拖慢启动）
- **开发者**：YYRMM

> 注：项目曾用名「雅易帮 / WINHELP」，命名空间 `WINHELP` 与 `AppData` 数据目录均予以保留以兼容老用户设置。

## 快速开始

### 方式一：直接下载运行
从 [Releases](../../releases) 下载 `司南工具箱_Setup_v5.6.0.exe`（安装包）或 `司南工具箱.exe`（绿色单文件），在 Windows 10/11 x64 上双击即可运行。

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
产物：`dist/BAND/司南工具箱_Setup_v5.3.0.exe`

## 隐私与安全

- 软件在**本地运行**，不会在未经你明确允许的情况下上传任何个人文件或隐私数据。
- 内置 AI 助手在调用系统工具或发送截图前，会弹出确认框，由你决定是否允许。
- 清理类功能**仅删除临时文件**，绝不在未确认的情况下删除你的个人文件；启动项以「禁用」而非「删除」处理，操作可逆。
- 高危操作（磁盘修复、防火墙重置、文件粉碎等）需连续多次确认；重复文件删除一律移入回收站。
- 所检测的硬件信息、健康评分与优化建议仅供参考，不构成专业维修或诊断意见。

## 项目结构（精简）

```
WINHELP/
├─ WINHELP.csproj          # 主工程（net10.0-windows, 自包含单文件）
├─ MainWindow.xaml(.cs)    # 主窗口：数据驱动侧栏导航 + 内容区
├─ ModuleRegistry.cs       # 模块元数据单一来源（key/标题/分组/工厂）
├─ HomePage / CompanionPage / Window* …  # 各功能页
├─ Cleaner.cs / HardwareInfo.cs …        # 核心逻辑
├─ GlassTheme.xaml / ClickAnim.cs        # 主题样式与点击动画
├─ 1.9.0pre01.iss          # Inno Setup 安装脚本（含本机绝对路径）
├─ license.txt             # 许可协议（与根 LICENSE 内容一致）
├─ .github/                # Issue/PR 模板、贡献指南、CI 工作流
└─ dist/                   # 交付产物（已被 .gitignore 忽略）
```

## 贡献

欢迎 Issue 与 Pull Request！请参阅 [CONTRIBUTING.md](.github/CONTRIBUTING.md) 了解开发环境、构建步骤与代码约定。
提交缺陷报告或功能建议时，请使用仓库提供的 Issue 模板。

## 许可证

本软件以 **GNU 通用公共许可证第 2 版（GPL v2）** 发布。
你可以自由地使用、复制、修改和分发本软件，包括用于商业用途；若分发修改版本，
必须以同样的 GPL v2 协议开源并保留本协议。本软件按“现状”提供，不含任何担保。
详见 [LICENSE](LICENSE) 与 [license.txt](license.txt)。

---

感谢你使用 司南工具箱，希望它能为你的电脑使用带来便利。
