# 项目状态

## 已完成
- 2026-10-09 修复消息只能在单段内选择的问题：每条消息共用单个原生 RichTextBlock，标题、引用、列表、代码和表格文字进入连续选择范围；代码自动换行，表格显示为带列分隔符的连续文本行。新回答和历史回答底部左侧增加 14px 复制图标、28px 透明无描边点击区域，悬停提示“复制全文”，不显示大号文字按钮；复制完整 Markdown 原文，保留点击快照、剪贴板序列保护、处理中禁用和成功/失败重试反馈。未新增依赖。
- 选择/复制图标修复验证：scripts/verify.ps1 通过（0 warning / 0 error，851 项断言），dist/AIScreenShot-win-x64-conversations-v4 已发布，DLL 与最终构建 SHA256 一致。--startup-test --message-selection-test 和完整 --floating-ui-test 均 exit 0；覆盖明暗主题、跨段 Select/全文 SelectAll、嵌套引用/列表/代码/表格/Unicode、小图标尺寸与无描边/悬停提示、按钮位于正文下方、完整 Markdown 复制快照、流式内容、复制失败重试及空/长回复。使用临时数据库和注入复制写入器，不读写用户剪贴板或发送真实模型请求；git diff --check 通过。
- 正常入口为 Windows 11 x64 托盘截图工具，.NET 10 / WinUI 3 / Windows App SDK / Win32。截图使用冻结的虚拟桌面与物理像素坐标；框选双缓冲、原生 Markdown、截图详情、纯文本/截图发送、默认提示词、主题/强调色、独立材质/描边/阴影、移动/缩放/过渡和已确认图标均保留。
- ChatGPT 官网发送、BrowserBridge、extension、Native Host 项目源码与注册脚本已移除（本地提交 f5e245e）；Agent 直接调用 OpenAI compatible Chat Completions。设置显示 Model name / Base URL / API key，密钥以当前 Windows 用户 DPAPI 密文保存在 settings.json，截图/提示词/远端错误正文不写日志。
- 2026-10-07 清理复查修改已保留并纳入本次提交：移除无调用的 CodexConversation.CanPrepareNewChat 和 Native.StretchDIBits，Agent 诊断移除旧 Browser 命名，README 修正官网扩展残留说明。旧 UIA / FlaUI / 剪贴板保护仍被专项诊断使用；保留旧设置迁移和发布目录污染检查。
- 2026-10-08 消息栏新增同窗口紧凑水平标签、“＋”（Ctrl+T）、历史入口、关闭与右键重命名；标签支持水平滚动和方向键/Home/End。每个标签独立保存 UI、草稿、截图、发送任务、取消状态、工作区和历史；新建仅继承当前工作区/模式，后续不联动。不同标签并行，停止只作用于当前标签；关闭标签保留后台任务和历史，重开能查看完成内容。
- SQLite 使用 Windows 自带 winsqlite3.dll，无新增生产依赖、数据库服务或手动初始化。%LOCALAPPDATA%/AIScreenShot/conversations.db 保存标题、消息、状态、客户端、thread/turn ID、工作区、草稿、打开标签/顺序/选中状态；screenshots 保存 PNG。WAL / FULL 同步，每次消息/状态更新及时提交。启动恢复打开标签；未完成发送转为 Unknown，不自动重发。
- Codex 每个标签复用自己的原生 thread，后续通过公开 thread/resume 继续；启动/历史重开时通过 thread/read 核对未确认的终态。Agent 使用该标签内已完成的 Agent 消息与历史截图恢复上下文，不共享其他标签或 Codex 上下文。正常路径不访问 Codex 内部数据库或剪贴板。
- 新对话默认标题；首条完整回复后向当前客户端发起独立异步 AI 标题请求，不加入用户上下文。手动重命名优先，迟到标题不能覆盖；失败可手动命名。历史入口支持重新打开与确认删除本应用的消息/截图，运行中禁用彻底删除，Codex 原生历史仍由 Codex 管理。
- 数据异常处理：坏记录跳过并提示，原始行保留；损坏数据库先保留 damaged 备份再创建可用数据库；权限/磁盘错误提示并保留已有数据；图片路径限定 GUID PNG，不允许目录逃逸。删除时保护其他对话引用的截图。
- 悬浮球的实际 ApplyBounds 通知两栏跟随，保持在显示器工作区内并选择球的可用侧。2026-10-09 按最新要求移除装饰尖角及其 Shape/主题/生命周期回调，球与工具栏垂直中心对齐，可见间距缩短为 8 个逻辑像素；开窗、面板移动/缩放/显隐后同步，屏幕边缘夹取时同步球位置并防止位置回调重入。原有动画高度边界、断开 backdrop 后迟到主题回调、切换标签后迟到滚动修复保留。
- 验证：scripts/verify.ps1 已通过（App 0 warning / 0 error；851 项 Core、Codex 协议、Agent 与剪贴板断言）。自包含发布包 dist/AIScreenShot-win-x64-conversations-v1 的 --floating-ui-test 已通过 exit 0，未出现未处理异常；覆盖旧界面回归、并行/取消/关闭重开、上下文隔离、原生球移动/尖角显隐、SQLite 标签/选中状态/工作区/草稿/模型/消息恢复与 Unknown 锁、自动标题与手动命名竞态。原生 UI 使用生成图和注入回复；本轮未发送真实请求、读取用户截图或操作剪贴板。最终移动/DPI 与删除提示收尾改动也已构建验证；git diff --check 通过。
- 2026-10-08 修复新版普通启动失败：已有历史时消息面板先于悬浮球创建，Windows.UI.Composition.Compositor 因缺少 Windows.System 消息队列抛出 0x80070005，表现为 WinUI 启动崩溃。OnLaunched 在首个窗口前调用当前 UI DispatcherQueue.EnsureSystemDispatcherQueue；新增安全启动阶段/异常类型/HRESULT/调用栈日志，启动失败不再留下无托盘的单实例进程。--startup-test 使用临时 SQLite 数据专门覆盖先恢复透明面板、再创建悬浮球的冷启动顺序，不使用用户历史或发送请求。
- 启动修复验证：再次通过 scripts/verify.ps1（0 warning / 0 error，851 项断言）、发布包 --startup-test 和完整 --floating-ui-test（均 exit 0）。dist/AIScreenShot-win-x64-conversations-v1 已重新发布；实际普通启动读取现有本地历史后到达 ready，52×52 悬浮球 HWND 可见且进程持续运行。启动前后数据库均 13 条对话、quick_check=ok，无历史清空；修复版已留在桌面运行。
- 2026-10-09 标签选中/悬停/按下的背景和文字接入现有窗口范围的强调色；标签溢出时下方常驻 10px 原生 ScrollBar，可拖动滑块/点击轨道/键盘滚动。新建或切换时滚入目标标签，后台进度不抢走用户滚动。删除确认框以原生 ContentDialog 和内容按钮组合显示主题色删除、白底黑字取消，保持取消初始焦点；仅显式确认记录删除意图，取消/关闭不会确认，忙碌/窗口关闭后的保护继续保留。弹框使用独立主题资源作用域，无新增依赖。
- 本版外观验证：scripts/verify.ps1 已通过（0 warning / 0 error，851 项断言）；v2 发布包 --startup-test --conversation-appearance-test 和完整 --floating-ui-test 均 exit 0，覆盖冷启动恢复、旧界面/发送/并行会话回归、球与工具栏中心对齐、溢出首尾滚动、Light/Dark 绿色选中标签与删除按钮、白色取消/初始焦点及取消/确认意图区分；生成图和临时数据库自测不读取或删除用户历史，不发送模型请求。普通启动再次到达 ready，进程持续运行；启动前后用户数据库均 18 条对话，quick_check=ok，无历史清空。
- 2026-10-09 优化历史删除流程：历史列表与确认在同一个原生 ContentDialog 内切换，取消返回列表，确认成功后立即刷新列表；持久化删除不依赖标签打开状态，运行中/已关闭窗口再次检查，失败在列表提示。清理删除对话的标题版本和恢复标记，保留其他标签与截图引用保护。重命名弹窗接入面板主题色，保存与聚焦下划线在 Light/Dark 均同步；保存拒绝空白标题，取消保留原名。新增 --startup-test --conversation-history-test 使用临时数据库和生成图检验真实原生按钮流程；与外观专项组合已通过 exit 0，覆盖冷启动未创建界面的历史删除/取消/截图清理/列表刷新/存储重开不复活及重命名保存/取消/空白校验，不操作用户历史。
- v3 修复包验证：scripts/verify.ps1 通过（0 warning / 0 error，851 项断言），发布 DLL 与最终构建一致。完整 --floating-ui-test 首次因测试期间前台窗口变化而未通过焦点断言，重跑 exit 0；未移除或放宽检查。普通启动到达 ready 并持续运行，启动前后现有数据库均 4 条对话、quick_check=ok；旧实例此前已退出，v3 留在桌面运行。git diff --check 通过。

## 未完成
- 跨段鼠标拖选和实际剪贴板粘贴需用户桌面验收；原生诊断验证 Select/SelectAll 和注入复制写入器，不读写用户剪贴板。
- 用户实际 OpenAI compatible 服务与完整鼠标流程、多会话真实模型并发、真实 Codex 连续多轮/重启恢复仍需桌面验收；本轮模拟协议和原生注入检查不代表真实服务端到端通过。
- 混合 DPI 多屏鼠标拖动、屏幕边缘的实际观感、系统主题/高对比度/动画关闭/透明效果关闭与省电降级仍需实机验收。RenderTargetBitmap 不包含系统 Acrylic 后方画面。
- 长期记忆、向量数据库、RAG、tools 和跨对话共享不在第一版范围，留待用户讨论。

## 当前 Bug
- 既有深色 RenderTargetBitmap 预览部分按钮/气泡出现白块、文字不可见，上一版也可复现。布局/原生状态检查通过不能替代实际深色桌面观感验收，本轮未改该既有问题。
- 旧 UIA 专项诊断仍可能受到 Codex 可访问性结构变化影响；正常 provider 不依赖该路径。之前欢迎页/占位提示/消息身份/剪贴板占用修复保留，不把旧诊断当作新版发送路径。
- Markdown 暂不支持数学公式、远程图片或完整 CommonMark；HTML 不执行，链接只允许 HTTP(S)。
- 旧 NativeHost 仅剩被 .gitignore 排除的 bin/obj，此前递归清理被工具自动审批拒绝，未执行删除；旧 dist 包保留，不视为当前版本。源码及新包无官网组件。

## 最近修改文件
- 跨段选择与复制图标：src/ScreenShot.App/MarkdownMessageView.cs、FloatingPanelWindow.cs、FloatingPanelConversations.cs、App.xaml；原生选择/复制回归：RuntimeDiagnostics.cs、StartupDiagnostics.cs、App.xaml.cs；README.md、docs/UI_DESIGN.md、PROJECT_STATUS.md 同步说明。
- 会话与存储：src/ScreenShot.Core/ConversationStore.cs、Contracts.cs、AgentClient.cs；src/ScreenShot.App/FloatingPanelConversations.cs、FloatingPanelWindow.cs、CodexProviderTarget.cs、CodexTarget.cs。
- 原生联动/生命周期：FloatingBallWindow.cs、FloatingPanelPlacement.cs（替代已移除的 FloatingPanelAnchor.cs）、AdjustableBackdrop.cs、TransparentPanelBackdrop.cs、App.xaml.cs、RuntimeDiagnostics.cs；启动初始化/安全日志/冷启动回归：StartupDiagnostics.cs。标签/滚动条/弹框外观：FloatingPanelWindow.cs、FloatingPanelConversations.cs、App.xaml、AppThemeController.cs。
- 检查：src/ScreenShot.App/ConversationDiagnostics.cs；tests/ScreenShot.Tests/ConversationTests.cs、AgentTests.cs、CodexProviderTests.cs、Program.cs。
- 文档：README.md、docs/UI_DESIGN.md、THIRD_PARTY_NOTICES.md、PROJECT_STATUS.md。之前清理的 CodexConversation.cs、Native.cs 修改保留。

## 已确认设计决策
- 每条回答底部仅显示小号复制图标，使用悬停提示和可访问名称说明用途；复制该条回答完整 Markdown 原文。连续选择使用一个原生文本控件，代码/表格不再使用独立文本控件或横向滚动区。
- 2026-10-09 用户明确授权将本次跨段选择、回答复制图标及相关诊断/文档提交到本地 Git；发布包与本地用户数据不纳入提交，不推送远端。
- 全部 UI 为原生控件；参考 Astryx 的短标签、选中/溢出/键盘导航和无障碍模式，不引入 React/Web UI 或新的生产包。标签属于同一个面板窗口，工具栏/外观共享，会话状态隔离。
- 第一版仅本地会话记忆；用户要求的历史内容/截图持久化与日志分开，API key 不进入 SQLite，继续使用 DPAPI。数据库/screenshots/运行日志均在 Windows 用户本地应用数据目录；显式 EXE 自测的生成图与检查报告留在发布目录 diagnostics，不涉及用户内容。
- 优先原生恢复：Codex 使用公开 thread/start/resume/read、turn/start/interrupt；只读 sandbox / never approval，不改 CLI 全局配置。Agent Chat Completions 无原生持久会话 ID，按本标签历史恢复；不自动截断或跨对话复用上下文。
- Sent/Unknown 不自动重发；Unknown 只保留本地记录并尝试只读核对。停止 Codex 精确作用于本次 thread/turn，一次 interrupt 后等待终态，15 秒未确认提示未知；Agent 取消本地读取，远端结果仍可能未知。
- 关闭标签不销毁任务，不删除历史；关闭整个面板/退出取消所有本工具等待。彻底删除明确只针对本应用记录和截图，原生 Codex 历史由 Codex 管理。
- 截图、提示词、回复及 API key 不写入内容日志；不读取 Codex 内部数据库，不覆盖用户并发剪贴板修改，不用固定坐标定位目标控件。
- 沿用 .tools/dotnet、scripts/verify.ps1 和 scripts/publish.ps1；未经明确要求不 commit/push。2026-10-09 用户授权将累计的多会话/本地记忆、启动修复、界面调整和历史删除/重命名修复提交到本地 Git；不包含发布包、数据库、截图、日志或密钥，未推送远端。f5e245e 为此前用户授权的本地提交。

## 后续计划
- 最新修复包为 conversations-v4；运行中的 v3 未强制退出，用户退出旧版后启动 v4 验收跨段拖选和复制图标。继续按实际截图/纯文本→并行标签→停止/切换→关闭重开→重启恢复的流程验收，并检查历史重命名/删除、标签溢出/主题色、球与栏对齐及真实服务恢复。
- 与用户讨论 Agent 应用场景和工具扩展；暂不增加长期知识库或跨会话共享，先保持基础会话隔离和稳定恢复。
