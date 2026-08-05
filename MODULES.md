# 司南工具箱（WINHELP）模块地图 / 代码导览

> 用途：本文件是项目的**总索引**。当你在 VS 里"只看到部分代码、找不到某个模块"时，先查这里。
> 维护约定：新增/合并模块后，请同步更新本文件与 `ModuleRegistry.cs`（模块元数据单一来源），`MainWindow.InitPages()` 会自动遍历注册。
> 版本：随主程序 5.3.0。命名空间统一为 `WINHELP`；程序集名/EXE 名为「司南工具箱」（AppData 数据目录仍为 `WINHELP`）。

## v5.3.0 UI 全面重写（重大变更）

- **去掉 QQ 风格蓝渐变标题栏**：改为纯白色玻璃主题栏，与 Win11 设计风格一致；窗口按钮按 Win11 浅色规范实现。
- **侧栏导航数据驱动**：移除 v5.2.0 的 7 个硬编码按钮与 7 个 Click 处理器，`MainWindow.NavSpec` 静态配置 → `BuildNav()` 自动生成按钮 + 分组标题；新增 / 删除模块只需改 `ModuleRegistry.All`。
- **去 emoji 化**：所有模块图标不再使用 emoji，改为「标题首字徽标」—— 圆角强调色浅底 + 中文首字（如 `清/启/网/截/卸`）。命令面板与首页卡片统一改用首字显示。
- **首页重设计**：仅保留 19 张功能卡片 + 顶部轻量欢迎条（问候 + 上次优化 + 可清理统计 + 一键优化）；移除原「每日贴士卡」「英雄横幅」「系统状况状态条」等冗余装饰；卡片改为统一尺寸、实色白底、轻边框。
- **新增模块**：`duplicate` 重复文件查找（按文件名 + 大小分组 → SHA-256 校验 → 全部移入回收站 + 双重确认，常规 `FileSystem.DeleteFile` 不可逆风险彻底规避）。
- **删除模块**：`wizard` 故障向导（与 `issue` 问题解决功能重叠，删除其页与类）、`novice` 新手导览（内容并入 `help` 电脑帮助页）。`tutorial` AI 密钥教程保留为 Agent 内部跳转入口。
- **侧栏 `系统工具` 组**（v5.3.0）：clean / startup / system / net / issue / rescue（6 项，最常用入口）；其余模块（重复文件、截图、便签、装机等）经首页卡片 / Ctrl+K 命令面板直达。

## UI 规范（v5.2.0 → v5.3.0 更新）

- **高危操作 5 连确认（v5.2.0 沿用）**：磁盘修复、防火墙重置、清空打印队列、文件粉碎、UAC 级别修改等
  「高危代码执行项」必须走 `RiskGuard.ConfirmHighRisk()`（默认 5 连确认，任一轮点「否」立即中止）；
  普通删除/覆盖类操作至少 1~2 次确认（`ConfirmTwice`）。禁止新增跳过确认的高危执行项。
- **术语解释**：统一走 `Glossary.cs` 词典（term → 一句话中英解释），`Glossary.Hint(raw)` 始终生效（面向新手，无专业模式）。
- **emoji 使用规则（v5.3.0 收紧）**：emoji 仅允许作为**模块/功能/分类图标**（如导航、命令面板中极少数语义符号）；
  **禁止**作为首页卡片图标 / 按钮 / 标题 / 提示行首的装饰前缀。新模块默认走「中文首字徽标」chip。
- **面向新手**：面向电脑新手用户，界面保持朴素简洁；操作前必须给通俗说明与安全确认。
- **界面风格（v5.1.0）**：整体简洁浅色设计，统一卡片圆角（10px）/间距（8px 网格）；命令输出区不使用纯黑控制台，
  采用浅色等宽面板 + 彩色状态徽章；具体视觉规范见 `GlassTheme.xaml`。

---

## 一、先建立心智模型（为什么代码"看起来没有固定元素"）

本项目是 WPF + .NET 10。**XAML 只是结构骨架，真正的视觉元素在运行时被注入**。理解这 5 层间接性，读代码就不会"迷路"：

1. **页面靠字典调度**：所有模块入口集中在 `MainWindow.xaml.cs` 的 `InitPages()`，用 `_titles[key]`（标题）和 `_factories[key]`（页面工厂）两个字典把字符串 key 映射到页面类。**页面是懒加载的**——只在点击导航时才 `new`，设计器里看不到。模块元数据（key/标题/图标/分组）集中在 `ModuleRegistry.cs` 的 `ModuleDefinition` 列表，`InitPages()` 遍历它自动填充字典。
2. **画刷不是写死的**：XAML 里全是 `{DynamicResource GlassCardBrush}` 这类引用（`GlassTheme.xaml` 里的只是兜底默认值）。真正的 `Brush` 实例由 `ThemeManager.ApplyGlass()` 在运行时 `new` 出来、再塞进 `Application.Current.Resources`（`ThemeManager.cs`）。
3. **按钮模板在 C# 里重建**：`ThemeManager.ApplyButtonTheme()` 用 `FrameworkElementFactory` 在代码里拼 `Button` 的 `ControlTemplate`，XAML 里看不到按钮内部结构。
4. **文字不是固定字符串**：`{loc:Loc 中文|英文}`（`LocExtension`）运行时按 `UiLanguage` 解析，XAML 里看到的是占位，不是最终显示的字。
5. **首页卡片是代码生成的**：`HomePage.BuildCards()` 遍历 `ModuleRegistry.All`，按 `HomeGroup` 把模块渲染成卡片（`BuildCard(m)`），`WrapCardContent()` 动态加星标/NEW 徽标。XAML 里只有空的 `WrapPanel` 壳。

**结论**：你"看着像没有固定元素"，是因为颜色、模板、文字、卡片都是运行时注入的。结构（XAML）+ 外观（ThemeManager/GlassTheme）+ 文字（LocExtension）+ 动态卡片（HomePage）被刻意解耦。这是「液态玻璃 + 多主题 + 中英双语」架构的必然结果，**不是代码丢失**。

---

## 二、模块总表（导航 key → 文件）

> 类型：`Page`=内嵌到主窗口右侧内容区；`Window`=独立窗体（注意：文件名叫 `WindowXxx` 的不一定是独立窗体，很多其实是内嵌 Page）。
> 依赖通用项：绝大多数模块依赖 `ThemeManager` 共享玻璃画刷（`GlassCardBrush` 等）+ `LocExtension` 多语言；按钮点击动画由 `GlassTheme.xaml` 的隐式 `ClickAnim` 全局提供。下表"依赖"只列**模块特有**的依赖。

| nav key | 页面类 | XAML 文件 | .cs 文件 | 类型 | 分组 | 职责 | 模块特有依赖 |
|---|---|---|---|---|---|---|---|
| `home` | HomePage | `HomePage.xaml` | `HomePage.xaml.cs` | Page | 系统工具 | 首页：分组功能入口 + 搜索筛选 + 收藏/NEW 徽标 | `ModuleRegistry.All` 驱动 `BuildCards()`；`WrapCardContent` 动态注入星标/NEW |
| `clean` | SystemCleanerPage | `SystemCleanerPage.xaml` | `SystemCleanerPage.xaml.cs` | Page | 系统工具 | 垃圾清理 + 磁盘占用分析（treemap） | `Cleaner` 库 |
| `startup` | StartupPage | `StartupPage.xaml` | `StartupPage.xaml.cs` | Page | 系统工具 | 开机自启项管理 | 注册表 |
| `net` | NetworkDiagnosticsPage | `NetworkDiagnosticsPage.xaml` | `NetworkDiagnosticsPage.xaml.cs` | Page | 系统工具 | 网络连通性检测 + 网速测试 | `System.Net.*` |
| `issue` | IssueSolverPage | `IssueSolverPage.xaml` | `IssueSolverPage.xaml.cs` | Page | 系统工具 | 问题解决：常见故障知识库（6 大类）+ 白名单命令一键修复（实时回显） | `IssueCatalog`（问题条目）、`CommandRunner`（白名单执行） |
| `system` | SystemStatusPage | `SystemStatusPage.xaml` | `SystemStatusPage.xaml.cs` | Page | 系统工具 | 设备检测 + 完整性检测 + 优化建议 | `HardwareInfo`、`HealthScoreService` |
| `shred` | WindowShredder | `WindowShredder.xaml` | `WindowShredder.xaml.cs` | Page | 效率工具 | 文件安全擦除（不可恢复） | `System.Security.Cryptography` |
| `snapshot` | WindowSnapshot | `WindowSnapshot.xaml` | `WindowSnapshot.xaml.cs` | Page | 效率工具 | 区域截图 + 标注编辑 | GDI+ `CopyFromScreen` |
| `uninstall` | WindowUninstaller | `WindowUninstaller.xaml` | `WindowUninstaller.xaml.cs` | Page | 效率工具 | 卸载残留清理 | 注册表 |
| `duplicate` | DuplicateFilePage | `DuplicateFilePage.xaml` | `DuplicateFilePage.xaml.cs` | Page | 效率工具 | 重复文件查找：按文件名+大小分组 → SHA-256 校验 → 全部移入回收站（双重确认） | `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(SendToRecycleBin)`、`RiskGuard.ConfirmTwice` |
| `notes` | NotesPage | `NotesPage.xaml` | `NotesPage.xaml.cs` | Page | 效率工具 | 桌面便签 | `NotesStore`（与陪伴窗实时同步） |
| `recorder` | WindowRecorder | `WindowRecorder.xaml` | `WindowRecorder.xaml.cs` | Page | 效率工具 | 录音录像（扫描本机已装录屏软件并一键启动） | — |
| `site` | SiteFinderPage | `SiteFinderPage.xaml` | `SiteFinderPage.xaml.cs` | Page | 助手与信息 | 网站与官网（常用网站 + 软件官网；v4.3.1 由「网站检索助手」合并「官网导航」而来） | — |
| `tool` | WinHelperPage | `WinHelperPage.xaml` | `WinHelperPage.xaml.cs` | Page | 助手与信息 | WIN 助手：实用软件官方下载直通车 | — |
| `help` | PcHelpPage | `PcHelpPage.xaml` | `PcHelpPage.xaml.cs` | Page | 助手与信息 | 电脑帮助：系统工具与使用技巧 | — |
| `agent` | AgentAssistantPage | `AgentAssistantPage.xaml` | `AgentAssistantPage.xaml.cs` | Page | 助手与信息 | Agent 助手：接入 OpenAI 兼容 API 的 AI 对话（流式 SSE） | `AiClient`、`ToolRegistry`、`AgentSettingsManager` |
| `report` | WindowReport | `WindowReport.xaml` | `WindowReport.xaml.cs` | Page | 助手与信息 | 月度报告：使用统计与成就 | `Cleaner` 统计、`SettingsManager` |
| `tutorial` | WindowTutorial | `WindowTutorial.xaml` | `WindowTutorial.xaml.cs` | Page | （内部跳转入口） | AI 密钥获取教程：引导申请各平台 API Key 并填入 | `AgentSettingsManager` |
| `bug` | BugReportPage | `BugReportPage.xaml` | `BugReportPage.xaml.cs` | Page | 助手与信息 | BUG 反馈（腾讯文档表单 / GitHub Issues / 崩溃日志） | `crash.log` |
| `setup` | SetupPage | `SetupPage.xaml` | `SetupPage.xaml.cs` | Page | 助手与信息 | 装机助手：新电脑常用软件官网导航 | `SiteCatalog.Groups` 驱动 `BuildCatalog()`（代码生成卡片） |
| `settings` | SettingsPage | `SettingsPage.xaml` | `SettingsPage.xaml.cs` | Page | 设置类 | 软件设置（开机启动、语言等） | `SettingsManager` |
| `theme` | AppearancePage | `AppearancePage.xaml` | `AppearancePage.xaml.cs` | Page | 设置类 | 个性装扮：主题色 / 背景图 / 玻璃强度 / 字体 | `ThemeManager`（直接写 theme.json） |
| `companion` | CompanionPage | `CompanionPage.xaml` | `CompanionPage.xaml.cs` | Page | 设置类 | 陪伴运行说明页 | `CompanionManager` |

---

## 三、独立窗体（不走导航字典，按需弹出）

| 窗体类 | XAML 文件 | .cs 文件 | 由谁启动 | 职责 |
|---|---|---|---|---|
| `CompanionWindow` | `CompanionWindow.xaml` | `CompanionWindow.xaml.cs` | 托盘菜单 / `CompanionPage` | 陪伴运行小窗：图形框 + 北京时间 + 返回 + 设置 |
| `SearchWindow` | `SearchWindow.xaml` | `SearchWindow.xaml.cs` | `MainWindow`（Ctrl+K） | 全局命令面板：跨模块 + 动作搜索直达（CommandItem 列表由 MainWindow 构建） |
| `SplashWindow` | `SplashWindow.xaml` | `SplashWindow.xaml.cs` | `App` 启动流程 | 启动动画（淡入 + 罗盘旋转 + 进度条），主窗口就绪后 `FadeOutAndClose` |
| `CompanionSettingsWindow` | `CompanionSettingsWindow.xaml` | `CompanionSettingsWindow.xaml.cs` | `CompanionWindow` | 小窗设置：自定义图片 + 北京时间开关 |

---

## 四、核心库类（Helper / Manager，无 UI）

这些类被各模块调用，理解它们能补全"完整功能"：

| 类 / 文件 | 职责 |
|---|---|
| `ThemeManager.cs` | 全局主题单例：共享玻璃画刷、主题色、字体、星空/极光背景；`ApplyGlass()`/`ApplyButtonTheme()` 运行时注入（v4.7.0 修：切星空不再清空自定义壁纸路径，切回自动恢复） |
| `GlassTheme.xaml` | 液态玻璃样式表：隐式 `Button`/`ScrollBar`/`ScrollViewer` 样式、`ClickAnim`、兜底画刷（被 `App.xaml` 合并） |
| `Cleaner.cs` | 系统清理核心逻辑（临时文件 / 回收站 / 浏览器缓存 / 更新缓存 / 大文件） |
| `HardwareInfo.cs` | WMI 硬件信息枚举（CPU / 显卡 / 主板等） |
| `HealthScoreService.cs` | 设备健康度评分 |
| `ModuleRegistry.cs` | 模块元数据单一来源：`ModuleDefinition` 列表 + `INavigationHost` 接口 + `CreatePage(key,host)` 工厂；`MainWindow.InitPages()`、`HomePage.BuildCards()`、`BuildCommandItems()` 共用 |
| `SiteCatalog.cs` | 装机助手官网数据：`SiteEntry`/`SiteGroup` + `Groups` 只读列表（含主板官网组）；`SetupPage.BuildCatalog()` 遍历生成卡片 |
| `IssueCatalog.cs` | 问题解决模块数据：`IssueEntry`/`IssueCategory` + `Categories` 只读列表（6 大类共 40 条）；`EnsureRegistered()` 幂等注入 `CommandRunner` 白名单 |
| `CommandRunner.cs` | 命令安全执行：`RegisterAllowed`/`IsAllowed` 精确白名单 + 普通(管道流式)/提权(UAC+日志 tail 准流式)双路径 + `IProgress<string>` 实时回显 + 超时/取消；`RiskLevel` 枚举 |
| `ToolRegistry.cs` | AI 代操作系统沙盒：白名单工具 + 人工确认 + 只读优先 |
| `AiClient.cs` | OpenAI 兼容 API 客户端（流式 SSE） |
| `AgentSettingsManager.cs` / `AgentSettingsWindow`(无) | Agent 密钥/预设配置持久化 |
| `CompanionManager.cs` / `CompanionSettingsManager.cs` | 陪伴运行状态与设置 |
| `NotesStore.cs` | 便签存储（`%APPDATA%/WINHELP/notes/` 每条约一个 txt；`Changed` 事件跨端同步） |
| `SettingsManager.cs` | 全局设置（开机启动、语言等） |
| `LocExtension.cs` / `UiLanguage.cs` | 多语言：`{loc:Loc 中文|英文}` 标记扩展 + 语言切换 |
| `ClickAnim.cs` | 按钮点击动画（缩放），全局隐式样式挂载 |
| `GlobalHotkeyCapture.cs` | 全局快捷键（Ctrl+K 等） |
| `UpdateManager.cs` | 版本检测 / 更新 |
| `SchedulerManager.cs` | 定时任务（月度报告等） |
| `PluginManifest.cs` / `UpgradeTracker.cs` | 插件清单 / 升级追踪 |
| `AviWriter.cs` | 屏幕录像 AVI（VfW）封装 |
| `App.xaml.cs` | 应用入口：启动 Splash → 异步加载主题/设置/托盘 → 显示 `MainWindow` |

---

## 五、主题与多语言机制（读 XAML 的正确姿势）

- **看颜色/背景**：别在 XAML 里找具体色值，它们是 `{DynamicResource GlassCardBrush}` 等；真实值来自 `ThemeManager.ApplyGlass()`。
- **看按钮外观**：`Button` 的 `ControlTemplate` 在 `ThemeManager.ApplyButtonTheme()` 用代码生成；XAML 里通常只有 `<Button Content="{loc:Loc ...}"/>`。
- **看文字**：`{loc:Loc 中文|英文}` 运行时按 `UiLanguage` 选语言；想看最终文案，改 `UiLanguage` 或直接在 `LocExtension` 调用处脑补。
- **改主题/字体/背景**：去 `AppearancePage`（nav key `theme`），它直接写 `ThemeManager` 与 `theme.json`。

---

## 六、VS 使用提示（解决"只能看到部分代码"）

1. **展开嵌套文件**：VS 默认把 `*.xaml.cs` 折叠在 `*.xaml` 下面。在解决方案资源管理器里点开每个 `.xaml` 左侧箭头，就能看到对应的 C# 代码。
2. **`obj/`、`bin/` 是生成目录**：里面有 `*.g.i.cs` 等自动生成的"影子代码"，**不要编辑**，它们只是给编译器用的。真正的源码都在项目根目录的 `.cs` / `.xaml` 里。
3. **所有源码都已被自动包含**：项目是 SDK 风格，`*.cs` 自动编译进工程，无需手动"包括在项目中"。如果你发现某文件不在列表里，通常是被折叠或你没展开对应节点。
4. **找模块两步法**：① 在 `MODULES.md` 本表按功能/key 查到 `XxxPage.xaml.cs`；② 在 VS 双击打开即可看到完整的 C# 代码（文件头已注明它对应的 nav key 与依赖）。
5. **加新模块两步走**：① 在 `ModuleRegistry.cs` 的 `All` 列表加一条 `ModuleDefinition`（含 key/标题/图标/`HomeGroup`/`CreatePage` 分支）；② 建对应 `XxxPage.xaml/.cs` 并在 `CreatePage` 的 switch 中 `new`。`InitPages()`/`BuildCards()`/`BuildCommandItems()` 会自动适配，无需手动改字典。
