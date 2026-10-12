# 构建与诊断

[返回 README](../README.md) · [使用与配置](USER_GUIDE.md)

## 开发环境

- Windows 11 x64；运行最低版本为 Windows 11 22H2（build 22621）。
- .NET 10 SDK。脚本优先使用 `.tools/dotnet/dotnet.exe`，不存在时使用 PATH 中的 `dotnet`。
- App 的目标 Windows SDK 为 `10.0.26100.0`，Windows App SDK 为 `2.5.1`；依赖由 NuGet 还原。
- UI 为 WinUI 3 原生控件；数学排版为 CSharpMath.Rendering 和项目自己的 WinUI 矢量适配器，不需要 TeX 运行时。

## 最小验证

在仓库根目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify.ps1
```

脚本先构建 Release App，再运行 `tests/ScreenShot.Tests` 控制台测试程序。测试入口是 `dotnet run`，不是 `dotnet test`。断言涵盖 Core 策略、模拟 Codex 协议、Agent 和剪贴板保护；成功不等于真实服务或所有桌面环境已通过验收。

## 便携发布

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -OutputDirectory .\dist\AIScreenShot-win-x64
```

输出目录包含 `ScreenShot.App.exe`、依赖、文档和第三方许可；.NET 与 Windows App SDK 均自包含，不需要在使用者机器安装 SDK。保留完整目录。当前为未打包的 x64 EXE，没有安装器、MSIX 或自动更新流程。

若指定目录含旧版浏览器桥接文件，脚本会拒绝发布。使用新的输出目录，不要混用旧包。公开仓库为 [cricket152/AI_ScreenShot](https://github.com/cricket152/AI_ScreenShot)，便携预览包在 [GitHub Releases](https://github.com/cricket152/AI_ScreenShot/releases) 手动发布；当前没有自动 Release 工作流。

## 原生专项诊断

以下参数在发布目录中运行。大多数专项使用生成 PNG、独立数据库与注入发送器，不代表真实客户端端到端验收。具体副作用以各项说明为准；真实发送诊断不要作为日常启动参数。

悬浮球视觉专项：`ScreenShot.App.exe --startup-test --floating-ball-test` 检查原生五种状态、Light/Dark 与七种自定义色、纯白图形与高光叠加后的对比度、客户区无隐藏边框、圆周透明与主体 alpha、100/125/150/200% 控件渲染，以及外阴影的透明度、预乘颜色、输入穿透、跟随、缓存和显隐。结果与生成图位于 `diagnostics/floating-ball-test.txt`、`ball-*.png`，包括较大尺寸原生视觉预览；不截图桌面、不读写用户历史/剪贴板或发送模型请求。混合 DPI 多屏与系统高对比度的实际观感仍需桌面验收。

运行诊断（可选）：`ScreenShot.App.exe --smoke-test`。生成的 `diagnostics` 仅用于本机测试；界面图片由 WinUI 控件渲染，不保存窗口后方的桌面。真实发送测试参数 `--test-codex-workspace AI_ScreenShot` 仅与 `--smoke-test` 一起使用，发送的是生成的渐变测试图。使用之前关闭已有实例；默认正常启动不执行发送测试。

Provider 连接专项诊断：`ScreenShot.App.exe --codex-provider-test` 检查真实 CLI 的 stdio 初始化、账号就绪及配置模型的图片能力和 reasoning effort 兼容性，使用独立 Mutex。结果位于 `diagnostics/codex-provider-test.txt`；不创建 thread/turn，不提交截图或提示词，也不记录账号内容。

数学渲染专项：`ScreenShot.App.exe --startup-test --math-render-test` 用生成样例检查明暗主题、公式/专业符号/中文术语、窄窗口显示、跨公式选择、无损全文复制、流式完整/未完成公式以及绘图/未知语法回退；生成图片与报告位于 `diagnostics/math-render-*.png`、`math-render-test.txt`。可与 `--message-selection-test` 合用，完整 `--floating-ui-test` 也会执行。自测不读写用户剪贴板、用户历史或请求模型。

截图跨对话专项：`ScreenShot.App.exe --startup-test --capture-routing-test` 使用生成 PNG、临时数据库和注入发送器，验证截图后切换/新建/重开标签、不同工作目录、原生发送按钮、提示词保留、防重发/失败重试、后台请求快照、解码竞态与重启恢复；报告与样例为 `diagnostics/capture-routing-test.txt`、`capture-routing.png`。完整 `--floating-ui-test` 同样执行，不截取桌面、操作用户剪贴板或请求真实模型。

紧凑输入区专项：`ScreenShot.App.exe --startup-test --composer-test` 检查截图行移除及高度释放，并在测试 HWND 获得前台焦点后注入真实按键，验证 Enter 换行、Ctrl+Enter 发送文字/截图/默认提示词、所选 Codex / Agent 模式、当前标签和 Sent/Unknown/忙碌防重发。另通过原生发送按钮及 Ctrl+Enter 检查第二/第三标签截图后的文字追问、历史图片保留而新消息不重复附图、空白/原提示词保护、失败重试、切换/重启和新截图恢复发送，以及新建无图标签的纯文本发送。报告与生成图为 `diagnostics/composer-test.txt`、`composer-compact.png`；仅使用生成 PNG、专用数据库和注入发送器，不操作用户内容、剪贴板或真实模型。

本地存储专项：`ScreenShot.App.exe --startup-test --storage-test` 使用专用 SQLite、生成 PNG 和注入文件夹打开器，通过原生按钮验证统计/路径、Light/Dark/窄窗、确认取消/执行、历史/草稿/流式图片保留、未知文件名、占用刷新、设置草稿隔离及关闭确认不删除。报告和生成图为 `diagnostics/storage-test.txt`、`storage-light.png`、`storage-dark.png`、`storage-narrow.png`。Core 测试另覆盖并发保存、确认后新增引用/文件变化、取消、文件占用、缺失/损坏/不完整索引、缺表检查不修改数据库、确认后数据表丢失及备份保护。所有显式 EXE 自测的设置窗口默认使用独立生成路径，不扫描或清理用户目录。

全屏/输入折叠专项：`ScreenShot.App.exe --startup-test --reading-mode-test` 使用临时数据库和生成图，通过原生图标 Invoke 检查当前显示器全屏、标签置顶、尺寸及工具栏恢复、全屏禁用拖动/缩放/球跟随、操作隐藏恢复、向下淡出/快速反向/关闭动画、跨标签草稿与截图保留、新截图展开输入和收起重开。结果写入 `diagnostics/startup-test.txt` 与 `reading-*.png`；完整 `--floating-ui-test` 同样执行，不操作用户数据或请求模型。生成图片不包含系统 backdrop，深色预览存在已知的 RenderTargetBitmap 白块问题，不能代替桌面观感验收。

历史恢复冷启动诊断：`ScreenShot.App.exe --startup-test` 使用独立 Mutex 和临时 SQLite 测试数据，在没有悬浮球的情况下先创建透明消息面板，检查标签、选中状态、草稿、历史回复及窗口显隐。结果位于发布目录 `diagnostics/startup-test.txt`，不读取用户历史或发送模型请求。正常启动在创建任何 backdrop 前初始化 Windows.System 消息队列；启动阶段与异常类型/HRESULT/调用栈保存在 `%LOCALAPPDATA%\AIScreenShot\diagnostics\startup.log`，不记录配置或消息内容。

会话外观快速诊断：`ScreenShot.App.exe --startup-test --conversation-appearance-test` 额外检查标签溢出滚动、浅色/深色强调色、白色取消按钮和初始焦点，并调用生成对话的取消/确认按钮检查结果区分；不会删除用户数据。此显式自测会激活测试面板验证键盘焦点，结束时尝试恢复原前台窗口。

历史删除与重命名诊断：`ScreenShot.App.exe --startup-test --conversation-history-test` 使用临时数据库和生成图片，通过原生按钮验证冷启动后未打开的历史对话可取消/确认删除、列表刷新、截图清理及其他标签隔离，并检查浅色/深色重命名主题色、保存/取消和空白标题校验。不读取或删除用户历史，不发送模型请求；可与 `--conversation-appearance-test` 合用。

悬浮界面专项诊断：`ScreenShot.App.exe --floating-ui-test` 检查 52px 悬浮球、空状态、浅色/深色布局、球点击展开/收起、单个显隐菜单入口、打开菜单不改变两栏状态、两项文字及菜单选项执行后的折叠/恢复和内容保留；通过原生 Button 与 MenuFlyoutItem 的 Invoke 验证同一箭头在可见栏之间移动、选择后关闭菜单。另检查顶部留白和工具栏边缘的可视树命中、生产 resize 回调的顶部高度变化/底边锚定/共同宽度及工作区限制；通过真实可视树命中和生产移动回调验证工具栏背景及消息标题可移动、控件/可选文本/缩放 grip 不能启动移动、点击阈值、尺寸/对齐/前台保持、手动位置恢复及单栏移动。原生 Markdown 控件与流式合并/最终刷新、默认提示词设置编辑器、底部发送按钮命令和 Agent 流式回复、断流正文保留和防重发同时检查。保留处理状态、截图详情、异步取消的窗口生命周期检查，通过注入发送回调验证 Codex 提交/等待/完成持续可见、不改变前台、后台回调不丢回复、取消及防重发。结果与生成图片位于 `diagnostics/floating-ui-test.txt`、`floating-*.png`。预览气泡明确标为布局示例，不是真实 Codex 回复。此诊断使用独立 Mutex，不抓取桌面、不读写系统剪贴板、不连接 Agent 接口、不执行真实发送；可视树命中和回调验证没有注入系统鼠标事件，不代替实际鼠标拖动、混合 DPI 多屏及真实兼容接口发送验收。正常启动使用新悬浮 UI，旧 `ToolbarWindow` 只保留给既有 smoke/toolbar 诊断。

旧 UIA 窗口识别专项诊断：`ScreenShot.App.exe --codex-window-test` 通过旧 UIA 发送定位器只读检查 Codex 主窗口和工作区匹配，结果位于 `diagnostics/codex-window-test.txt`。需先打开 Codex 并展开项目列表；此诊断使用独立 Mutex，不激活窗口、不导航、不读写剪贴板、不发送消息，日志只记录检查结果、数量或异常类型/HRESULT。

旧 UIA 欢迎页就绪专项诊断：将 Codex 主窗口停在指定工作区的空欢迎页，运行 `ScreenShot.App.exe --codex-ready-test "工作区名称"`，检查旧 UIA 发送使用的工作区/空草稿判断、错误工作区拒绝、无附件及提交上下文准备。结果位于 `diagnostics/codex-ready-test.txt`；此诊断同样只读，不自动切换聊天、清空草稿、访问剪贴板或发送消息。它不能代替图片上传和回复回显的真实验收。


框选专项诊断：运行 `ScreenShot.App.exe --overlay-test` 检查生成的测试图在真实 GDI 缓冲区中的像素对齐、裁剪、反向选区、选区边框、资源释放、增量/完整绘制一致性、失效区覆盖与重复窗口生命周期，并输出 4K 生成画面合成基准，结果写入 `diagnostics/overlay-test.txt`。此诊断使用独立 Mutex，可与正常实例并行，不连接 Agent 接口。运行 `ScreenShot.App.exe --toolbar-test` 检查原生工具栏浅色/深色渲染、布局和会话退出，结果写入 `diagnostics/toolbar-test.txt`；仅保存生成的工具栏界面，不抓取或记录桌面，不执行发送。工具栏诊断也使用独立 Mutex，不连接 Agent 接口，可与正常实例并行，并验证 Codex 成功退出截图及失败/未知保留截图。这两项诊断不代替真实 Codex 发送、实际鼠标拖动和混合 DPI 多屏验收，合成基准不代表桌面帧率。

## 验证边界

混合 DPI、多屏鼠标操作、高对比度、真实模型图片输入及会话恢复仍需实机验收。深色 RenderTargetBitmap 的既有白块问题只说明诊断预览的局限，不能凭预览断言实际窗口显示。当前记录见 [PROJECT_STATUS.md](../PROJECT_STATUS.md)。

宣传图维护见 [素材说明](IMAGE_ASSETS.md)。
