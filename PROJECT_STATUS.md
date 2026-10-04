# 项目状态

## 已完成
- 2026-10-04 修复用户报告的 provider“回复失败、桌面没有新聊天”：自动查找原先选中 PATH/npm CLI 0.144.6，而当前桌面内置版本为 0.160.0；公开 config/read 确认配置模型为 gpt-6.1-sol，旧版本 model/list 不含此模型，新版本包含。生成图真实对照复现旧版本 terminal failed、无正文、错误涉及 model，新版本返回正确固定文本、流式输出及 completed。自动选择现优先正在运行/已安装的桌面内置 CLI，仍尊重显式路径；发送前检查所选 cwd 的模型目录、图片能力及 reasoning effort，不自动改模型或全局配置。无法回溯上一版未记录的用户请求错误全文，不将该对照声称为读取其原始失败详情。
- 按用户确认使用独立 Codex provider，参考 D:/Git_Project/Amadeus 的 SDK/App Server 事件路由独立实现 .NET stdio JSONL 客户端。正常入口不操作桌面 UIA 或系统剪贴板；用户选择 cwd，近期目录/CLI 路径保存在设置中。当前 thread/start 使用 ephemeral=false、只读 sandbox、never 批准，并以 AI ScreenShot 标题命名；明确拒绝命名不阻止发送。PNG data URL 与提示词一次提交，按 threadId/turnId/itemId 路由，最终由 turn/completed 确认，final_answer 优先。失败显示协议类别/合法 HTTP 状态，保留正文，不回显自由格式错误详情；连接重试事件不提前结束或再次提交。停止等待原子解绑并保留最新正文，已接受的后台请求排空至终态；退出关闭自有进程。未引入生产依赖、未访问内部数据库、未记录内容、未修改 Amadeus。
- 真实生成图验证通过：直接调用生产 CodexProviderTarget，自动选择桌面运行时，沿用 gpt-6.1-sol 默认配置，经模型预检后 Sent / 流式正文 / Completed，固定回复完全一致。独立公开 App Server 测试的持久会话在新进程 thread/read 可读，Codex 桌面的公开 read_thread 同样确认会话存在且已完成；未操作内部数据库，未重发用户的截图或“123”提示词。
- 最新 scripts/verify.ps1 通过：App/Native Host 构建 0 warning / 0 error，437 项核心断言、15 项扩展测试与 3 项 JS 语法检查。包含安全错误分类、失败保留正文、服务端重试不重发、模型/图片/effort 预检、持久化/命名拒绝、阻塞写入期限、提交边界取消、后台释放等。provider-runtime-fix 独立包经 scripts/publish.ps1 生成，App DLL SHA-256 与验证构建一致；实际包 --floating-ui-test / --codex-provider-test 均 exit 0，分别验证原生面板注入回调和真实 CLI 账号/图片模型兼容。真实图片请求证据来自上项生产路径生成图测试，不将无发送诊断当作真实请求。
- 2026-10-04 修复图片剪贴板 `COMException / 0x800401D0`：本机安全日志确认阶段为“写入图片剪贴板”，Submitted=False；原方法合并 SetContent/Flush，无法确定哪个调用被拒绝。现备份、写入、Flush 和恢复分别只对剪贴板无法打开有界异步重试（最多 12 次，间隔 100ms），每次检查 sequence；写入成功后不重放写入、粘贴或发送，取消/用户复制新内容时停止。恢复使用独立 3 秒 token；未固化的 DataPackage/图片或备份 stream 保留至替换/退出，资源保留不授予覆盖用户内容的权限。原生复制按钮调用同步改为 async await；诊断阶段拆分写入/固化。新增 14 项核心回归，174 项最小断言通过，实际系统锁占用与新请求发送仍待验收。
- 2026-10-04 修复再次报告的“Codex 已回复但仍正在发送、切换聊天后 Unknown”：真实完成页生产读取复现用户消息含正文及本地化星期时间 sibling，用户数/草稿/最终 footer 正常，但提示词匹配为 false。改为同一角色父分支中唯一 `bg-user-message` 正文子树匹配，排除旁边时间与活动；正文时间/重复行完整保留，多正文、跨父与不连续树拒绝。未知旧布局保留原有保守兼容路径。修复后同一真实完成页匹配为 true，以合成提交前基线调用生产 ConfirmSubmissionAsync / ObserveAsync，约 1.9 秒产生 Thinking / Completed、无 Unavailable，正文仅内存比对，前台不变、未发送新消息。
- 2026-10-02 桌面悬浮 UI 第一版：启动显示 52 逻辑像素悬浮球，拖动后左右贴边吸附；左键展开、右键隐藏/设置、托盘恢复。原生 WinUI 工具栏包含 Codex、ChatGPT、复制、另存为、更多，基准尺寸 360 × 56；消息栏目 520 × 540，左边缘对齐、间距 12，按工作区限制窗口尺寸，支持浅色/深色。
- 消息栏目支持空状态、工作区选择、截图缩略图/详情、提示词、用户左侧/Codex 右侧气泡、正在思考/处理状态、停止等待及清空消息。最多保留 24 个内存气泡。正常截图使用新面板，旧 ToolbarWindow 保留给既有专项诊断。
- 2026-10-02 Codex 等待修复：用户确认图片和提示词已送达，但安全日志显示“确认消息提交 / Submitted: True / TimeoutException”。删除全窗口同文节点计数及提交后 30 秒确认上限；发送前记录对应聊天的旧用户消息，发送后以新增且匹配提示词的用户角色和草稿清空/处理状态/已完成回复确认。短提示词、分割文本及非空 TextPattern 的处理状态都可确认，不只依赖空输入框。
- 2026-10-03 Codex 欢迎页修复：安全日志为“记录提交上下文 / Submitted: False / InvalidOperationException”，图片和提示词仍在草稿。原实现错误要求输入框位于消息滚动区内；现分别识别主内容区域、输入状态与消息区，允许欢迎页尚无消息滚动区，提交后等待其出现，回复完成判断不再依赖输入框在消息节点之后。
- 2026-10-03 Codex 窗口查找修复：最新日志为“查找窗口 / Submitted: False / InvalidOperationException”。只读探测复现同一 ChatGPT 进程的 MainWindowHandle 指向 QuickChat 辅助输入窗口，真实主窗口另有 HWND；改为枚举目标进程可见顶级窗口，按工作区新聊天按钮及选定工作区筛选唯一目标。未就绪返回 null 供准备阶段等待，多个匹配窗口明确报错，不选择第一个窗口。
- 当前真实 UIA 扁平列表省略 MainContentFrame 布局祖先；回复观察补从 ProseMirror 父级链发现主内容范围，按已捕获 scopeKey 与 RuntimeId 去重，保留窗口、区域、用户消息及侧栏身份关联检查。只读生产源码探针验证普通查找/MathLearning 查找均返回真正主窗口、不存在工作区返回 null、Prepare/Read 与范围重获取成功、前台未改变；未提交的观察仍为 Waiting，已有消息/草稿仍拒绝作空新聊天。
- 2026-10-03 欢迎页就绪修复：用户确认已跳转正确工作区但未粘贴，日志为“确认工作区 / Submitted: False / TaskCanceledException”。用户保留 MathLearning 空欢迎页后，只读探测复现 Chromium 将占位提示暴露为非空 Value/Text；RawView 有唯一展示用 placeholder 分支、单一标签和空 ProseMirror-trailingBreak。现严格检查分支及非 control/content 标记，稳定重读值后才按空草稿处理，真实同名输入、额外文本、残缺树、未知标记和跨读取修改仍保留非空。
- 工作区就绪改为同一次唯一输入框/隔离主范围读取，按实际项目 selector 确认目标，不再额外重复八层扫描。本次真实欢迎页原标签在第六层，因此八层并非当次主因。附件等待只接受该工作区零旧用户消息草稿中唯一移除附件按钮，兼容附件与输入框 sibling；旧全窗口 screenshot 字符串在空页面已有 8 个误命中，现不再将 AI_ScreenShot 侧栏项目当作图片到达证据。
- 2026-10-03 用户已确认 ready-fix 的图片和提示词正常送达，但完成后仍显示“正在发送”。真实已完成目标只读复现：扁平列表漏掉消息滚动区及用户角色，恢复 scroll 后其 FindAllDescendants 仍漏掉用户；现从消息角色父级恢复同一范围的唯一 scroll，仅在其中读取 RawView，排除 sr-only 角色内部标签子叶。新增且匹配提示词的用户证据可正确确认，保留侧栏/窗口/历史关联防护。
- 已完成空输入框真实只有语音入口、没有 Send/Stop；完成条件改为最新助手真实 footer、非空回复、无 Stop、可确认空草稿或 Send，并连续两次读取稳定，不依赖必须出现 Send。流式观察持续提供公开工作状态、可读取的工具摘要和正文快照，在同一气泡更新并去重；只在底部跟随，停止/中断保留已收到正文，终态后迟到进度不能覆盖最终结果。不展开收起的活动历史，不读取私人推理或写内容日志。
- 新建聊天后先确认工作区、唯一输入框、真实空草稿、无旧用户消息和无附件，再开始粘贴，避免手动重试叠加到旧草稿；优先读取 ValuePattern，仅在不支持时访问 TextPattern，不仅凭文字猜占位，也不自动清除草稿。聊天范围拒绝侧栏、工作区导航按钮、多个输入框/消息区及整个 Document/Window 兜底；侧栏身份仅取真实聊天行，排除项目文件夹。
- 点击 Codex 即退出截图遮罩，保留当前图片、消息栏与工具栏；从提交到回复完成都不隐藏面板，发送状态与真实回复分开。准备阶段仍有 90 秒上限，提交确认与回复读取没有固定截止时间，控件暂缺/失效或安全可确认的树重建继续等待；用户可从发送开始主动停止等待。
- 公开 UI Automation 观察绑定窗口、聊天区域与最新用户消息。回复尾部复制控件、空草稿/发送按钮与停止状态共同确认结束，并要求两次稳定读取，代码块复制不判定完成。记录旧侧栏身份，避免切到同文旧聊天误读；明确窗口关闭/切换/后续用户消息才结束关联，不自动重发，不取消 Codex 任务。
- 图片粘贴仍短暂需要 Codex 输入焦点。提示词确认及发送按钮就绪后释放原剪贴板，提交后只恢复一次原窗口，尊重期间用户的剪贴板/焦点修改；等待与完成不激活或置顶 Codex。后台回调结束前先处理已排队进度，避免真实完成回复被发送回执覆盖。
- 窗口生命周期保护：取消/重新选区后旧图片解码不重新打开面板；收起或操作期间关闭截图详情，旧解码不抢回焦点；Alt+F4 关闭面板同时结束截图会话。截图前隐藏自身窗口，ChatGPT/保存暂时隐藏，Codex 保持面板可见；关闭后旧回调不影响新窗口。
- scripts/verify.ps1 最新通过：App/Native Host 构建 0 warning / 0 error，437 项核心/Codex provider/UIA/剪贴板断言、15 个扩展测试与三个 JS 语法检查；既有 UIA 与剪贴板回归保留。
- 真实已完成聊天生产源码只读探针 15 项断言通过：读取 1 个用户且匹配、最终 footer/正文与公开进度，HasSend=False / HasStop=False / 草稿清空仍正确 Completed。以明确标记的合成提交前基线验证生产 ConfirmSubmissionAsync 与 ObserveAsync，产生 Thinking/Completed 并在约 2.4 秒返回，正文仅内存比对，前台未改变；已完成聊天正确拒绝作为新草稿。未导航、粘贴、发送，不将其声称为真实发送端到端验收。
- 最新 ready-fix 包 --codex-ready-test MathLearning 实跑 exit 0：实际 EXE 在用户保留的真实空欢迎页确认目标输入框就绪、占位规范为空、无附件、错误工作区拒绝及 Prepare 成功；没有激活、导航、剪贴板访问或提交消息。
- 最新 ready-fix 包 --codex-window-test 实跑 exit 0：从实际 EXE 调用生产定位器，识别当前真实 Codex 发送窗口、5 个唯一工作区动作，指定工作区仍对应同一主窗口。只读公开控件，不激活窗口、不导航、不读写剪贴板、不提交消息；日志不记录工作区名称或聊天内容。
- 最新 ready-fix 包 --floating-ui-test 实跑 exit 0：原有尺寸、浅色/深色、布局、详情和关闭检查通过；注入真实 UI 发送路径验证提交/思考/完成始终可见、不抢前台、后台回调保留真回复、未确认时用户取消保留 Unknown 防重发、停止已提交等待，以及收起/关闭不复活。只用生成图和注入发送回调，没有桌面捕获、系统剪贴板、浏览器连接或真实发送。既有旧工具栏诊断此前通过。
- 新 stream-fix 包 --floating-ui-test 实跑 exit 0：增加连续公开状态/正文、原位更新无重复气泡、底部跟随与用户上滚保留、停止/中断后正文保留、后台回调与迟到进度、完成及时解除忙碌等验证；生成回调与原生面板路径通过，未真实发送。
- 新 stream-fix 包 --codex-window-test 实跑 exit 0：生产定位器识别真实主窗口及 5 个唯一工作区动作，指定工作区对应相同窗口；未激活、导航或发送。
- 新 reply-fix 包 --floating-ui-test 实跑 exit 0：消息栏生成回调的提交/流式更新/完成、正文保留、焦点、停止等待及 Unknown 防重发验证通过；未抓取桌面、访问剪贴板、连接浏览器或真实发送。真实完成页生产观察已单独验证，不能替代新请求的完整发送验收。
- 新 clipboard-busy-fix 包 --clipboard-test / --floating-ui-test 均实跑 exit 0：12 项内存 WinRT 格式/独立流备份检查与原生消息面板生成回调的流式、完成、停止和防重发检查通过。没有系统剪贴板读写或真实发送；不将这些检查当作原生剪贴板锁占用及新请求发送全过程验收。
- Codex 控件修复：重新获取 ProseMirror Edit，UIA_E_ELEMENTNOTAVAILABLE (0x80040201) 读取有界重试；Invoke 后失效只观察、不重放动作。提交前确认提示词，提交后确认输入框清空及草稿/侧栏之外新增同内容消息，兼容非 Text 节点和空白差异。
- 剪贴板兼容修复：针对用户提交前 COMException / 0x800401D3，使用标准格式专用 API、可读自定义格式及逐格式处理 CLIPBRD_E_BAD_DATA；非空且全部不可读时不写入。位图、HTML 资源、自定义流独立内存复制，保留 RequestedOperation；--clipboard-test 的 12 项内存 WinRT 断言通过，没有访问系统剪贴板。
- 剪贴板写入后追踪 sequence，Flush 后仅在 owner 可确认时更新；恢复检查 sequence，不覆盖用户修改。恢复 Flush 失败时保留仍被剪贴板使用的流直到被替换/退出。codex-send.txt 区分备份/写入等阶段，空异常消息显示类型/HRESULT，不记录内容。
- 框选绘制使用 top-down DIB、物理坐标、持久 GDI 双缓冲；仅更新选区差集条带和旧/新边框，鼠标消息与 WM_PAINT 合成解耦。生成像素诊断通过 19,314 项断言、171,680 次增量/完整比对、失效区覆盖与资源生命周期；4K 生成画面基准完整 4.168 ms/帧、增量 0.154 ms/帧，仅代表合成路径。
- 扩展 0.1.1 在安装/更新、浏览器启动、MV3 worker 重建时自动握手，断开按 1/2/5/10/30 秒退避，alarms 每分钟兜底；不自动重发截图/提示词，异步结果绑定原 Port。
- Native Messaging 部署由 Explorer 桌面进程执行，验证物理路径/全量 SHA-256 后写 HKCU 双视图注册，解决 Codex MSIX AppData 重定向导致 Chrome 找不到 Host；支持 -VerifyOnly、-HostPath、-WhatIf。194 个运行文件及真实 Chrome 协议/Named Pipe 握手验证通过。
- 已部署 manifest：C:/Users/Lenovo/AppData/Local/AIScreenShot/native-messaging/Chrome.json；Host：C:/Users/Lenovo/AppData/Local/AIScreenShot/native-messaging/host/9f121322b96baa69/ScreenShot.NativeHost.exe。
- 已有功能：托盘、全局快捷键、矩形多屏截图、PNG 保存、独立目标发送、设置、MV3 扩展和分块/长度/哈希校验。历史版本曾实测读取 22 个唯一 Codex 工作区并确认生成测试图提交；不代表本次回复读取或剪贴板修复的端到端验收。

## 未完成
- 生产 provider 的真实生成图、流式正文和终态已通过；新版从实际框选、目录选择到消息面板回显的完整用户操作，以及回复期间手动切换 Codex 桌面聊天的联动仍待桌面验收。原生 UI 注入回调和后台真实发送分别通过，不声称已经共同完成此操作验收。
- 旧 UIA ready-fix 的送达及真实完成页只读解析证据保留为历史诊断，正常入口已转 provider，不再将旧桌面发送全过程作为新版验收路径。
- 实际鼠标拖动悬浮球、贴边、混合 DPI 跨屏、屏幕边缘面板、截图拖动顺滑程度、保存对话框、高对比度和剪贴板并发修改的桌面验收。
- Chrome 重新加载原扩展后的自动连接、浏览器先启动/程序后启动、程序重启、worker 休眠恢复，以及官网图片上传/发送。Node 模拟测试和 Host 握手不能替代真实浏览器验收。

## 当前 Bug
- 本轮已修复默认选择旧 CLI 与当前模型不兼容、临时会话不保存及统一失败消息掩盖错误类别的问题，真实生产路径生成图回复通过。原始用户请求的远端错误全文未保存，不能回溯唯一原因。显式配置旧 CLI 时仍尊重配置，但模型预检会在提交前提示升级；不会静默替换配置。正常发送已移除桌面聊天切换/UIA 元数据和剪贴板占用依赖，provider 断线或缺失提交回执仍明确返回 Unknown/Unavailable，不推测成功、不自动重发。
- 旧 UIA 图片发送在提交前报 0x800401D0，本轮补齐剪贴板有界异步重试和保护；本机日志未提供占用进程，也无法回溯区分原失败发生于 SetContent 或 Flush，不推测具体程序。当前用注入失败验证策略，尚未操作真实系统剪贴板复现锁占用或发送截图。
- 旧 UIA 报告“Codex 已回复但仍正在发送，切换后 Unknown”已再次在真实完成页复现为本地化星期时间被拼入用户正文，提示词匹配失败；本轮已修复并通过生产确认/回复只读观察。新请求从截图发送到流式更新和完成的全过程仍需新版实际验收，未将合成基线验证当作真实发送验收。
- 此前 0x800401D3、0x80040201 的逐格式备份/控件重获取修复保留；具体原始剪贴板格式未识别，不将本次单次送达当作所有格式验收。
- 用户报告的框选拖动卡顿已修复全屏同步合成路径并通过生成像素/性能检查；真实多屏顺滑程度和闪动/错位仍待验收。
- Codex 可访问性结构可能随版本变化，无法可靠确认提交时返回 Failed/Unknown，无法继续读取已提交回复时保留已有正文并明确转到 Codex 查看。仅显示公开且可读取的文本/工作摘要，不还原全部 Markdown、图片或收起的工具历史；ChatGPT 只展示发送回执。
- 当前 Computer Use 仅提供 Codex 内置浏览器，没有 Chrome 自动化入口，无法替用户重新加载已安装扩展及验收官网发送。
- 旧 Codex LocalCache 部署文件保留，不作为正式 Host 依赖；Codex 内 Test-Path 不能证明 Chrome 可见，应使用 -VerifyOnly 检查。

## 最近修改文件
- src/ScreenShot.App/CodexProviderExecutable.cs、CodexProviderModelPolicy.cs、CodexProviderFailure.cs、CodexProviderTarget.cs、CodexProviderEvents.cs：本轮桌面运行时优先、模型预检、安全错误反馈、持久会话/命名及失败流式处理。CodexRpcClient.cs：此前 JSONL 传输与期限。CodexDirectoryPicker.cs、FloatingPanelWindow.cs、App.xaml.cs、ToolbarWindow.cs、CodexTarget.cs、src/ScreenShot.Core/Contracts.cs：原生目录选择、设置及正常入口；App 连接诊断同步检查模型；旧 UIA 保留诊断。
- tests/ScreenShot.Tests/CodexProviderTests.cs、ScreenShot.Tests.csproj、Program.cs：平台无关模拟协议、安全失败分类/预检/命名拒绝及既有身份/终态/取消/阻塞写入回归；README.md：provider 操作、持久化、CLI 优先级与诊断。
- src/ScreenShot.App/FloatingBallWindow.cs、FloatingPanelWindow.cs：悬浮球、横向工具栏、原生消息面板、截图详情和操作状态。
- src/ScreenShot.App/CodexConversation.cs、CodexScopePolicy.cs、CodexObservationPolicy.cs：本次用户正文子树隔离、排除本地化星期时间、正文时间保留与歧义拒绝。CodexReplyStreamPolicy.cs、CodexTarget.cs、Native.cs：此前消息区恢复、语音空草稿完成、公开流式快照、工作区/附件就绪、无固定时限等待、焦点恢复与剪贴板释放。
- src/ScreenShot.App/CodexWindowLocator.cs、CodexWindowPolicy.cs：枚举同进程可见顶级窗口、工作区语义过滤、排除辅助窗口及唯一候选保护。
- src/ScreenShot.App/App.xaml.cs、TrayService.cs：默认入口、截图/面板生命周期、托盘恢复、--floating-ui-test 与只读 --codex-window-test / --codex-ready-test。
- src/ScreenShot.App/ClipboardAccessPolicy.cs、ClipboardService.cs、CodexTarget.cs、FloatingPanelWindow.cs、ToolbarWindow.cs：本次剪贴板占用异步重试、独立 Flush、sequence 并发保护、恢复/资源生命周期及 async 调用方。ClipboardBackup.cs、ClipboardBackupPolicy.cs、Native.cs、CodexAutomation.cs：此前格式备份与 UIA 控件修复。
- src/ScreenShot.App/SelectionOverlay.cs、OverlaySurface.cs、DesktopCapture.cs、RuntimeDiagnostics.cs、ToolbarWindow.cs：此前框选优化及既有诊断。
- tests/ScreenShot.Tests/Program.cs、ScreenShot.Tests.csproj、extension/connection.js、connection.test.js、background.js、manifest.json、popup.html、package.json：核心断言及自动连接回归。
- scripts/verify.ps1、register-extension.ps1、native-messaging-desktop.ps1、tests/native-host-smoke.cjs：验证与宿主部署。
- README.md、PROJECT_STATUS.md：新版操作、布局、回复读取范围与实际验收状态。

## 已确认设计决策
- 用户明确选择“直接用 provider，在截图工具消息栏显示回复，工作目录由我选择”，后续报告期望 Codex 中存在新聊天；正常请求现创建独立持久 thread（ephemeral=false）并命名，不依赖桌面当前聊天。未指定 CLI 时优先桌面内置版本；沿用其模型/登录，经公开 account/read、config/read 和 model/list 预检，不读登录令牌。准备/本地回执各有 90 秒期限，回复读取无固定期限；连接检测 30 秒。只读 sandbox 与 never 批准，不放宽权限或改变 CLI 全局配置；正常 Codex 发送不访问剪贴板，ChatGPT 路径不变。
- .NET 10 / WinUI 3 / Windows App SDK / Win32 / FlaUI.UIA3；原生 Windows 桌面控件，借鉴 Astryx 的图标提示、状态与无障碍约定，不引入 Web UI 或生产依赖。
- 默认 52 逻辑像素入口，工具栏负责操作，大消息栏目负责内容；用户气泡左侧、Codex 气泡右侧。本工具的图片/提示词/回复只保留在内存，不写入内容日志；Codex 自身按普通持久聊天保存，截图工具不访问其内部数据库。
- 点击 Codex 就退出截图状态并保留面板和当前图片，发送/确认/回复都不隐藏面板。已提交或未知目标禁止对当前图片重发；只有实际提交证据才能标为 Sent。停止等待只取消本工具的流程，不取消或重发 Codex 任务；无固定回复截止时间，等待阶段不改变前台窗口。
- 欢迎页无消息滚动区属于正常状态；只在可隔离侧栏的主内容区域读取聊天，输入框与消息区分别定位。新建聊天必须实际可确认空草稿且无旧消息/附件，不覆盖用户草稿。
- 占位提示仅在公开 RawView 证明完整展示分支、空段及稳定值时按空草稿读取，不依赖语言白名单或 Value==Name。图片附件确认仅使用相同工作区新聊天草稿的唯一附件动作，不能使用侧栏或全窗口同文标签。
- 不依赖 Process.MainWindowHandle 定位 Codex；仅在目标进程可见顶级窗口中匹配工作区动作，多个匹配目标明确停止。主内容布局祖先可从输入框 Parent 链恢复，仍需通过唯一输入框和消息身份校验，不退回全窗口读取。
- 用户消息匹配优先使用同一角色直属父级内的唯一正文气泡，按 RawView 连续子树隔离 UI 元数据，不按语言猜测星期时间，不裁掉实际正文中的时间；旧未知布局仍保守读取，结构歧义继续等待。
- 截图基于开始时冻结的虚拟桌面，统一物理像素坐标，保持双缓冲局部绘制。正常入口使用新面板；旧工具栏仅保留在原有运行诊断。
- 剪贴板仅恢复可读格式，单格式 CLIPBRD_E_BAD_DATA 不阻塞其他格式，其他读取错误仍传递，非空且全部不可读时停止；不覆盖发送期间的用户修改。
- 剪贴板占用仅在单次读/写/固化/恢复阶段有界重试；每次核对 sequence，保持 UI 同步上下文。成功写入、粘贴和提交均不重放；恢复不使用已取消的发送 token。资源保留与恢复授权分开，owner 未变不能作为覆盖已变化 sequence 的依据。
- Native Host 名 com.aiscreenshot.bridge；正式文件在宿主真实 LocalAppData 按完整运行文件指纹部署，保留旧版、不覆盖运行文件，无需管理员权限或降低安全设置。
- 实际 Chrome 扩展 ID 为 dmcdjpekhmjmcbjcaiillldpekdopocd，注册应使用浏览器实测 ID。alarms 不新增网页权限，重连不重放业务任务，无法修复错误注册。

## 后续计划
- 最新便携包 dist/AIScreenShot-win-x64-provider-runtime-fix 已通过 scripts/publish.ps1 生成，旧 codex-provider / clipboard-busy-fix 等包保留。先从托盘退出旧实例，再运行新版 ScreenShot.App.exe，并从消息标题栏选择工作目录。若设置中曾手动填写旧 CLI 路径，清空使其自动选择桌面版本，或显式指定新版。
- 完成真实 Codex 回复与多屏桌面验收；Chrome 重新加载原扩展后验证自动连接，再验证官网发送。
- 当前工作区为 Git 仓库；本轮按用户明确要求整理上述修改并执行本地 Git 提交，不执行 push。提交包括原生悬浮消息面板、Codex provider/持久会话、旧 UIA 与剪贴板修复、回归测试及文档；构建产物和临时诊断不纳入。
