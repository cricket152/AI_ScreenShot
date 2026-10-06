# 项目状态

## 已完成
- 2026-10-06 完成主题色边框及已确认图标接入：窗口底板、工具栏和消息栏边框使用窗口作用域的原始强调色，系统色/自定义色和三种主题实时同步；外观页新增原生 ToggleSwitch 与 0.5–3px Slider，默认开启/1px，关闭保留宽度，支持预览/保存/默认/关闭撤销。描边为不接收输入的原生叠加 Border，关闭 DWM 重复灰色外框，内容留白/窗口几何/缩放/过渡保持稳定；隐藏底板时同时隐藏公共描边。高对比度通过 Windows App SDK ThemeSettings 监听并使用系统资源。
- 用户已确认此前 imagegen 图标：从原始透明 PNG 转换为十尺寸 ICO（16–256px）及 256px PNG，ApplicationIcon 嵌入 EXE，所有已注册窗口通过 AppWindow.SetIcon 使用同一 ICO，托盘加载独立 HICON 并在退出释放，悬浮入口采用同设计 PNG。转换脚本及原图/提示词保留，无重新绘画、第三方依赖或业务逻辑变更。
- 本轮验证：边框和图标分别 Release 编译 0 warning / 0 error，核心回归新增四项兼容/持久化/有限宽度断言，共 712 项；扩展 15 项及 JS 语法 3 项通过。实际构建 --floating-ui-test exit 0：自定义粉色及当前 Windows 色同步三处边框、三种主题、0.5/1/3px、关闭/恢复/保留宽度、窗口/内容几何不变及不拦截输入、EXE 图标提取、窗口 HICON、托盘资源加载和悬浮图像检查通过，既有材质/底板区域/运动/菜单/移动/缩放/流式/防重发回归保留。修复原生检查发现的动态 ThemeResource 未取得作用域颜色问题；高对比度改用桌面 ThemeSettings。最终 scripts/verify.ps1 exit 0，App / Native Host 0 warning / 0 error，712 核心 + 15 扩展 + 3 语法检查通过；dist/AIScreenShot-win-x64-accent-borders-icon 经 scripts/publish.ps1 生成，实际 EXE --floating-ui-test exit 0。App/Core DLL、App.xbf、ICO/PNG SHA-256 与验证构建一致；提取的 EXE 32px 图标与已确认 ICO 像素完全一致。没有桌面截图、系统剪贴板访问、浏览器连接或真实发送，旧包/既有修改保留，无 commit/push。
- 2026-10-06 完成应用主题与强调色：外观设置新增“明 / 暗 / 系统”及“跟随 Windows / 自定义颜色”，使用原生 ComboBox、色样/HEX 按钮和无 alpha 的 ColorPicker Flyout。AppThemeController 统一管理已打开设置、悬浮入口、面板、截图详情及旧诊断窗口；通过 UISettings 监听系统主题/强调色，UI 线程更新根 RequestedTheme 和作用域主题刷，原生按钮/选中/链接实时更新。自定义按钮自动选择黑/白前景，正文/底板保持中性 ThemeResource，HighContrast 不覆盖系统色。沿用实时预览、独立保存、恢复默认和关闭撤销。参考 DeskBox ThemeService/AccentResourceScope/外观页设计独立实现，无源码/资源复制、新依赖或业务逻辑修改。
- 主题验证：控制器、设置及诊断分别 Release 编译通过；最终 scripts/verify.ps1 的 App / Native Host 0 warning / 0 error，708 项核心断言、15 项扩展测试及 3 项 JS 语法检查通过。验证构建的实际 --floating-ui-test exit 0：三种主题跨窗口/材质同步、实际原生按钮背景与文字对比度、黑/白自定义色、系统色阶恢复、HighContrast 字典未覆盖、真实 ColorPicker 打开/改色及窄窗布局通过；既有区域/动效/菜单/移动/缩放/流式/防重发回归保留。没有实际更改 Windows 主题或系统色，也没有截图/剪贴板/浏览器/真实发送。新版 dist/AIScreenShot-win-x64-theme-accent 经 scripts/publish.ps1 生成，实际 EXE --floating-ui-test exit 0；ScreenShot.App.dll / ScreenShot.Core.dll / App.xbf SHA-256 与验证构建一致。旧包和用户既有修改保留，无 commit/push。
- 2026-10-06 根据用户截图修复隐藏底板后仍保留的外围矩形：此前仅清除 backdrop alpha 和 DWM 描边不足以移除 HWND 原生区域；新增 PanelWindowRegion，隐藏时使用两栏圆角区域并集，实际排除外围留白、两栏间隙和圆角外区域；保留两栏原生材质/描边及控件。基于真实 client origin 和 DPI 映射物理区域，成功转移 HRGN 所有权，临时区域 finally 释放，关闭时解除布局订阅。区域外不再接收点击，栏自身边缘缩放/空白拖动继续保留。
- 修复工具栏扩展轨迹：过渡全程保持 Left 对齐，固定左端/顶部、宽度向右单调增长，完成才恢复 Stretch，避免固定 Width 被 Stretch 临时居中。消息栏收起时原生底板区域随当前布局同步收缩；整体收起同时动画两栏高度/间距至零并同步收缩外侧留白，完成先隐藏 HWND 再恢复布局，消除内容先消失、底板等完成回调才跳变的问题。保持 180ms token、应用/系统动画开关、快速反向切换和截图前立即隐藏，无业务逻辑或依赖变更。
- 底板/动效修复验证：区域接入及轨迹诊断分别 Release 编译通过；最终 scripts/verify.ps1 的 App / Native Host 0 warning / 0 error、703 项核心断言、15 项扩展测试和 3 项 JS 语法检查通过。验证构建与新便携包 dist/AIScreenShot-win-x64-panel-region-motion-fix 的 --floating-ui-test 均 exit 0；实际 HWND region 排除外围/间隙/圆角外并保留栏中心，运动采样确认左端/顶部固定及宽度单调向右增长、消息栏/整体收起时底板边界在过程中同步缩小。既有材质、双主题/窄窗设置、菜单、移动/缩放、流式、防重发及前台保持回归通过；ScreenShot.App.dll / ScreenShot.Core.dll / App.xbf SHA-256 与验证构建一致。旧包、用户既有修改保留，无 commit/push；没有抓取桌面、访问系统剪贴板、连接浏览器或真实发送。
- 2026-10-06 完成隐藏底板与面板过渡：外观设置新增“隐藏窗口底板”CheckBox 和“界面过渡动画”ToggleSwitch，沿用实时预览、独立保存、恢复默认及关闭撤销。隐藏底板释放公共 Acrylic、使用零 alpha 原生色刷并关闭 DWM 外框，工具条/消息仍各有材质与边框；底板参数保留且编辑暂禁用。唯一菜单增加“缩小/扩展工具栏”，缩小后只保留 Codex、ChatGPT 和同一个箭头；消息宽度不变，仅小工具条可见时收窄为胶囊，扩展恢复五动作及原宽度。未修改截图/provider/ChatGPT/Native Messaging 业务逻辑，无新依赖。
- 原生 UiTransition 使用 180ms CubicEase / EaseOut 同步过渡高度、宽度、透明度和双栏间距，覆盖工具条尺寸、两栏显隐、悬浮球打开面板；快速反向切换从当前值继续，取消旧完成事件。应用和 Windows 动画开关共同控制，关闭动画立即提交目标状态。截图/操作前隐藏保持同步，恢复不抢前台。修复初始菜单按钮尚无 Parent 时重复加入控件、正常宽度未扣原生 client 差值导致双栏错位；退出/中断动画均释放事件。小胶囊保留移动/高度缩放，正常宽度在扩展后恢复。
- 本轮各区域编译及最终 scripts/verify.ps1 通过：App / Native Host 0 warning / 0 error，703 项核心断言、15 项扩展测试、3 项 JS 语法检查。dist/AIScreenShot-win-x64-panel-transitions 经 scripts/publish.ps1 生成，实际 EXE --floating-ui-test exit 0；验证隐藏底板零 alpha/两栏材质保留、双主题小胶囊、两/五动作和窄 HWND、显隐/快速反向/中途关闭动画/操作立即隐藏与恢复、内容及前台保持，既有设置/移动/缩放/流式/防重发回归通过。诊断最初将主动隐藏前台面板后 Windows 正常转移焦点误判为失败，已分别断言隐藏前的焦点保持和恢复后不抢焦点，再实跑通过。生成预览不捕获桌面、系统剪贴板、浏览器或真实发送；旧包和既有未提交修改保留，无 commit/push。
- 2026-10-06 完成可调桌面材质：悬浮底板替换 Mica Alt，工具条/消息区域各使用原生 SystemBackdropElement + 独立 DesktopAcrylicController，可选 Acrylic、Thin Acrylic、透明色层并调节 0–100% 底色不透明度。默认底板/工具条/消息为 32/42/82%，文字、截图和按钮不随背景变淡；色值取 ThemeResource。独立配置保持 IsInputActive=true，不抢焦点，同步高对比度和透明效果关闭策略，悬浮球同样保持失焦材质。新增“外观与材质”设置，实时预览、单独保存、恢复默认和关闭撤销未保存外观。只增加 UI 外观持久字段，旧 settings.json 兼容；业务/provider/Native Messaging 及依赖未改变。
- 可调材质验证：区域接入/设置页分别 Release 编译通过，最终 scripts/verify.ps1 的 App/Native Host 构建 0 warning / 0 error，701 项核心断言、15 项扩展测试和 3 项 JS 语法检查通过。原生 --floating-ui-test 检查三种材质和 0/37/100%、双主题 tint 变化、独立原生 target、非交互背景、不透明内容、前台保持及失焦配置；本机三个控制器失焦时实际 State 为 Active/Active/Active。五分类双主题/520px 设置、草稿保留、420px 工具条、既有移动/缩放/流式/防重发等回归通过。修复原生 external backdrop link 拒绝任意 CompositionColorBrush 的切换问题；透明区域使用原生 Border 色层，底板使用 Composition brush。生成预览不捕获后方桌面、不读写系统剪贴板、不连接浏览器、不真实发送。
- dist/AIScreenShot-win-x64-adjustable-materials 经 scripts/publish.ps1 生成，实际包 --floating-ui-test exit 0；ScreenShot.App.dll、ScreenShot.Core.dll、App.xbf SHA-256 与验证构建一致。旧包及用户既有未提交修改保留，无 commit/push。设置双主题与窄窗预览位于包内 diagnostics/settings-appearance-*.png；这些图片不包含系统背景材质，仅作为布局预览。
- 2026-10-06 完成 DeskBox 设计参考的原生 Fluent UI 重构：独立实现四分类 NavigationView 设置窗口、标题/说明/右侧控件统一行和窄窗堆叠；Mica/Mica Alt 基础层、Acrylic 胶囊工具条及 52px 悬浮入口，正文保持清晰背景。App.xaml 集中维护 Spacing、CornerRadius、Typography、Surface、Motion；保留原生按钮模板与 Segoe Fluent Icons，支持双主题和系统禁用动画。底部截图预览/提示词/发送整合为一块输入区，用户/助手气泡保留左右层级。未复制 DeskBox 源码/资源、未引入生产依赖、未修改截图/provider/ChatGPT/Native Messaging 业务协议。
- 本轮 UI 验证通过：各区域 App Release 编译 0 warning / 0 error；最终 scripts/verify.ps1 的 App/Native Host 构建、693 项核心断言、15 项扩展测试和 3 项 JS 语法检查全部通过。新增原生诊断验证五个动作的统一尺寸与四种原生模板状态、420px 工具条、520px 设置窄窗、四类页面双主题、编辑控件不超出行/视口、主题资源更新及未保存输入保留。修复布局暴露的初始 10px 程序滚动被误认为用户上滚的 UI 竞争；原有底部跟随/用户上滚/恢复跟随及渲染等待期间的上滚保护通过。此前显隐菜单、移动/缩放、焦点、流式、停止、生命周期与防重发回归保留。dist/AIScreenShot-win-x64-fluent-ui 经 scripts/publish.ps1 生成，DLL/App.xbf SHA-256 与验证构建一致，实际包 --floating-ui-test exit 0；生成图诊断未抓取桌面、读写系统剪贴板、连接浏览器或真实发送。
- 2026-10-06 新增左键移动工具栏和消息栏：工具栏空白处、消息标题及非滚动区空白背景捕获真实鼠标，超过 4 逻辑像素后按累计屏幕坐标共同移动，两栏保持对齐，单栏显示也可移动。排除按钮、输入框、滚动区、可选择文本和缩放 grip，保留边缘缩放；菜单、隐藏、缩放开始、关闭及捕获丢失时结束移动。目标显示器工作区限制位置，移动后读取 DPI 调整逻辑布局，使用 NoActivate/NoZOrder 保持前台及层级。本次运行中的手动位置由重新展开、操作恢复及新截图保留，重启仍按悬浮球默认定位；未新增持久位置配置或生产依赖。
- 2026-10-06 修复顶部红框拖动高度不变：外侧顶部 padding 接入原生 grip，消息可见时 Toolbar Top/顶部角及 Window 边缘通过消息高度改变整体窗口高度，保持工具栏高度和窗口底边锚定，解决原 Toolbar 初始 56 到下限无法缩小的问题。后续按用户新要求合并显隐入口：只保留工具栏右侧一个下拉箭头，移除消息标题收起及额外恢复箭头；原生 MenuFlyout 提供“隐藏工具栏”和“隐藏消息栏”，点击箭头仅展开，选择条目后才执行。对应栏隐藏后菜单项改为“显示…”，工具栏隐藏时同一个按钮移至消息标题右侧；始终保留可见栏与恢复入口。消息、截图、提示词及高度保留，新截图恢复消息栏，悬浮球/Esc 仍整体收起。隐藏消息同时结束截图遮罩，provider 接收不取消，隐藏消息不自动滚动或重新展开。
- 2026-10-04 完成七项悬浮界面优化：再次点击悬浮球收起工具栏/消息栏；底部“发送”默认走 Codex provider；用户及 AI 气泡使用可选择文本的原生 Markdown（标题、强调、引用、列表、代码、表格、分隔线及安全 HTTP(S) 链接），HTML/图片语法按文字显示；两栏四边四角支持左键拖动，共享宽度、各自高度并限制在显示器工作区；ChatGPT 官网发送只显示状态，不新增面板消息；设置新增默认提示词，手动输入优先、空设置恢复内置提示词；工具栏箭头独立隐藏，消息标题栏保留恢复箭头。正常面板两栏初始共同约 520 逻辑像素宽。流式 Markdown 以 80ms 合并最新快照，最终回复立即刷新，保留底部跟随和用户上滚位置。未增加生产依赖。
- 2026-10-04 修复用户报告的 provider“回复失败、桌面没有新聊天”：自动查找原先选中 PATH/npm CLI 0.144.6，而当前桌面内置版本为 0.160.0；公开 config/read 确认配置模型为 gpt-6.1-sol，旧版本 model/list 不含此模型，新版本包含。生成图真实对照复现旧版本 terminal failed、无正文、错误涉及 model，新版本返回正确固定文本、流式输出及 completed。自动选择现优先正在运行/已安装的桌面内置 CLI，仍尊重显式路径；发送前检查所选 cwd 的模型目录、图片能力及 reasoning effort，不自动改模型或全局配置。无法回溯上一版未记录的用户请求错误全文，不将该对照声称为读取其原始失败详情。
- 按用户确认使用独立 Codex provider，参考 D:/Git_Project/Amadeus 的 SDK/App Server 事件路由独立实现 .NET stdio JSONL 客户端。正常入口不操作桌面 UIA 或系统剪贴板；用户选择 cwd，近期目录/CLI 路径保存在设置中。当前 thread/start 使用 ephemeral=false、只读 sandbox、never 批准，并以 AI ScreenShot 标题命名；明确拒绝命名不阻止发送。PNG data URL 与提示词一次提交，按 threadId/turnId/itemId 路由，最终由 turn/completed 确认，final_answer 优先。失败显示协议类别/合法 HTTP 状态，保留正文，不回显自由格式错误详情；连接重试事件不提前结束或再次提交。停止等待原子解绑并保留最新正文，已接受的后台请求排空至终态；退出关闭自有进程。未引入生产依赖、未访问内部数据库、未记录内容、未修改 Amadeus。
- 真实生成图验证通过：直接调用生产 CodexProviderTarget，自动选择桌面运行时，沿用 gpt-6.1-sol 默认配置，经模型预检后 Sent / 流式正文 / Completed，固定回复完全一致。独立公开 App Server 测试的持久会话在新进程 thread/read 可读，Codex 桌面的公开 read_thread 同样确认会话存在且已完成；未操作内部数据库，未重发用户的截图或“123”提示词。
- 最新 scripts/verify.ps1 通过：App/Native Host 构建 0 warning / 0 error，693 项核心断言（含新增 33 项移动策略）、15 项扩展测试与 3 项 JS 语法检查，既有顶部/默认提示词/Markdown/provider/UIA/剪贴板回归保留。panel-move 经 scripts/publish.ps1 生成，DLL SHA-256 与验证构建一致；实际包 --floating-ui-test exit 0，新增真实可视树移动区域命中、控件/可选文本/grip 排除、点击阈值、生产回调的实际 HWND 位置变化、尺寸/对齐/前台保持、收起/操作恢复位置保留及单栏移动验证。既有唯一显隐菜单 Invoke、内容保留、顶部可视树命中、窗口底边锚定、共享宽度、Markdown/设置/发送/防重发/流式/焦点/停止/生命周期检查均通过。此诊断使用生成图、公开原生控件 Invoke、可视树命中及生产移动/尺寸回调，没有注入操作系统鼠标、真实发送或桌面截图；不将无发送诊断当作真实图片请求。
- 2026-10-04 修复图片剪贴板 `COMException / 0x800401D0`：本机安全日志确认阶段为“写入图片剪贴板”，Submitted=False；原方法合并 SetContent/Flush，无法确定哪个调用被拒绝。现备份、写入、Flush 和恢复分别只对剪贴板无法打开有界异步重试（最多 12 次，间隔 100ms），每次检查 sequence；写入成功后不重放写入、粘贴或发送，取消/用户复制新内容时停止。恢复使用独立 3 秒 token；未固化的 DataPackage/图片或备份 stream 保留至替换/退出，资源保留不授予覆盖用户内容的权限。原生复制按钮调用同步改为 async await；诊断阶段拆分写入/固化。新增 14 项核心回归，174 项最小断言通过，实际系统锁占用与新请求发送仍待验收。
- 2026-10-04 修复再次报告的“Codex 已回复但仍正在发送、切换聊天后 Unknown”：真实完成页生产读取复现用户消息含正文及本地化星期时间 sibling，用户数/草稿/最终 footer 正常，但提示词匹配为 false。改为同一角色父分支中唯一 `bg-user-message` 正文子树匹配，排除旁边时间与活动；正文时间/重复行完整保留，多正文、跨父与不连续树拒绝。未知旧布局保留原有保守兼容路径。修复后同一真实完成页匹配为 true，以合成提交前基线调用生产 ConfirmSubmissionAsync / ObserveAsync，约 1.9 秒产生 Thinking / Completed、无 Unavailable，正文仅内存比对，前台不变、未发送新消息。
- 桌面悬浮 UI：启动显示 52 逻辑像素悬浮球，拖动后左右贴边吸附；左键展开/收起，右键隐藏/设置、托盘恢复。原生 WinUI 工具栏包含 Codex、ChatGPT、复制、另存为、更多及折叠箭头；初始两栏共同约 520 宽，工具栏高 56、消息栏高 540，间距 12，按工作区限制窗口尺寸，支持浅色/深色。
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
- 实际 Windows 应用主题/强调色变化通知、系统高对比度切换的桌面验收。原生三模式切换、当前系统调色板与未覆盖 HighContrast 资源的检查已通过，不将其声称为实际系统设置切换。
- 新版材质的后方窗口像素合成、透明效果关闭/省电/高对比度降级、系统动画关闭、真实鼠标交互及混合 DPI 多屏验收。双主题/窄窗预览、原生切换、失焦 IsInputActive 和本机 Active 状态已通过；RenderTargetBitmap 不包含系统 backdrop，设置预览临时使用主题 fallback，不能作为后方画面的像素实测。
- 生产 provider 的真实生成图、流式正文和终态已通过；新版从实际框选、目录选择到消息面板回显的完整用户操作，以及回复期间手动切换 Codex 桌面聊天的联动仍待桌面验收。原生 UI 注入回调和后台真实发送分别通过，不声称已经共同完成此操作验收。
- 旧 UIA ready-fix 的送达及真实完成页只读解析证据保留为历史诊断，正常入口已转 provider，不再将旧桌面发送全过程作为新版验收路径。
- 实际鼠标移动两栏、拖动悬浮球与两栏四边/四角缩放、贴边、混合 DPI 跨屏、屏幕边缘面板、截图拖动顺滑程度、保存对话框、高对比度和剪贴板并发修改的桌面验收。移动/缩放策略与真实 WinUI 生产回调通过，不代表真实鼠标及多屏操作验收。
- Chrome 重新加载原扩展后的自动连接、浏览器先启动/程序后启动、程序重启、worker 休眠恢复，以及官网图片上传/发送。Node 模拟测试和 Host 握手不能替代真实浏览器验收。

## 当前 Bug
- 本轮边框与图标在已执行的编译/原生诊断范围内无未解决失败。实际系统色/高对比度切换、多屏、Explorer 图标缓存及真实托盘展示仍待桌面验收，不以 HICON 加载代替视觉验收。
- 用户截图指出的隐藏底板外围矩形和扩展轨迹/退场底板滞后已修复，实际便携包的 HWND 区域及运动过程采样通过；当前已执行的检查无未解决失败。此前零 alpha 断言不足以验证原生矩形消失，现已补充真实窗口形状与过渡过程检查。真实桌面观感及混合 DPI 仍需验收。
- 本轮 UI 在已执行的编译和原生无发送诊断范围内无未解决失败；设置 Style 类型、NavigationView 属性名、修饰键最小宽度、窄工具条 padding 与初始流式跟随问题已修复。既有真实发送和多屏验收限制仍保留，不将生成回调当作完整业务端到端结果。
- 本轮已修复默认选择旧 CLI 与当前模型不兼容、临时会话不保存及统一失败消息掩盖错误类别的问题，真实生产路径生成图回复通过。原始用户请求的远端错误全文未保存，不能回溯唯一原因。显式配置旧 CLI 时仍尊重配置，但模型预检会在提交前提示升级；不会静默替换配置。正常发送已移除桌面聊天切换/UIA 元数据和剪贴板占用依赖，provider 断线或缺失提交回执仍明确返回 Unknown/Unavailable，不推测成功、不自动重发。
- 旧 UIA 图片发送在提交前报 0x800401D0，本轮补齐剪贴板有界异步重试和保护；本机日志未提供占用进程，也无法回溯区分原失败发生于 SetContent 或 Flush，不推测具体程序。当前用注入失败验证策略，尚未操作真实系统剪贴板复现锁占用或发送截图。
- 旧 UIA 报告“Codex 已回复但仍正在发送，切换后 Unknown”已再次在真实完成页复现为本地化星期时间被拼入用户正文，提示词匹配失败；本轮已修复并通过生产确认/回复只读观察。新请求从截图发送到流式更新和完成的全过程仍需新版实际验收，未将合成基线验证当作真实发送验收。
- 此前 0x800401D3、0x80040201 的逐格式备份/控件重获取修复保留；具体原始剪贴板格式未识别，不将本次单次送达当作所有格式验收。
- 用户报告的框选拖动卡顿已修复全屏同步合成路径并通过生成像素/性能检查；真实多屏顺滑程度和闪动/错位仍待验收。
- 旧 UIA 诊断仍受 Codex 可访问性结构变化影响；正常 provider 使用公开事件确认提交/终态，读取中断保留正文并显示状态。原生 Markdown 支持已列常见格式，暂不支持数学公式、远程图片或完整 CommonMark；HTML 不执行。ChatGPT 发送仅显示状态，不在面板回显对话；本次尚未执行真实官网图片上传/发送验收。
- 旧 Codex LocalCache 部署文件保留，不作为正式 Host 依赖；Codex 内 Test-Path 不能证明 Chrome 可见，应使用 -VerifyOnly 检查。

## 最近修改文件
- 边框与图标：src/ScreenShot.Core/PanelAppearance.cs、tests/ScreenShot.Tests/AppearanceTests.cs（边框兼容配置/边界）；src/ScreenShot.App/SettingsWindow.cs、FloatingPanelWindow.cs、AppThemeController.cs（边框控件、叠加描边、语义色与桌面高对比度）；ScreenShot.App.csproj、Assets/AppIcon.ico、Assets/AppIcon.png、AppIcon.cs、Native.cs、TrayService.cs、FloatingBallWindow.cs（嵌入/窗口/托盘/入口）；RuntimeDiagnostics.cs（实际控件/描边/图标回归）；scripts/create-app-icon.ps1、README.md、docs/UI_DESIGN.md、docs/icon-candidates/README.md、PROJECT_STATUS.md。
- 应用主题/强调色：src/ScreenShot.App/AppThemeController.cs（窗口注册、系统通知、主题字典和原生语义色）；SettingsWindow.cs（主题/颜色控件、预览/保存/撤销）；App.xaml.cs、FloatingBallWindow.cs、FloatingPanelWindow.cs、ToolbarWindow.cs（所有窗口接入）；RuntimeDiagnostics.cs（原生主题/颜色/ColorPicker 回归）；src/ScreenShot.Core/PanelAppearance.cs、tests/ScreenShot.Tests/AppearanceTests.cs（兼容配置/边界）；README.md、docs/UI_DESIGN.md、docs/icon-candidates/README.md、ai-screenshot-fluent-v1.png、PROJECT_STATUS.md。
- 底板/动效缺陷修复：src/ScreenShot.App/PanelWindowRegion.cs（圆角 HWND 区域、同步退场边界、HRGN 生命周期）；FloatingPanelWindow.cs（固定左端扩展、整体收起高度/间距、布局区域同步与结束顺序）；Native.cs（region/client origin API）；RuntimeDiagnostics.cs（真实 native region 和运动过程采样）；README.md、docs/UI_DESIGN.md、PROJECT_STATUS.md。
- 隐藏底板与过渡：src/ScreenShot.App/FloatingPanelWindow.cs（菜单缩小/扩展、原生几何、两栏过渡）；UiTransition.cs（Storyboard 与中断/事件释放）；AdjustableBackdrop.cs、Native.cs（零 alpha 底板及 DWM 外框/client 尺寸）；SettingsWindow.cs（两开关与底板编辑禁用）；RuntimeDiagnostics.cs、App.xaml.cs（独立默认诊断配置、动画/材质/内容/前台验证）；src/ScreenShot.Core/PanelAppearance.cs、tests/ScreenShot.Tests/AppearanceTests.cs（兼容持久配置）；README.md、docs/UI_DESIGN.md、PROJECT_STATUS.md。
- 可调材质：src/ScreenShot.App/AdjustableBackdrop.cs（独立控制器/透明色层/失焦及主题策略）；FloatingPanelWindow.cs、FloatingBallWindow.cs、App.xaml（原生材质接入与背景样式）；SettingsWindow.cs（外观设置与预览）；RuntimeDiagnostics.cs、App.xaml.cs（原生材质回归及诊断堆栈）；src/ScreenShot.Core/PanelAppearance.cs、Contracts.cs（兼容外观配置）；tests/ScreenShot.Tests/AppearanceTests.cs、Program.cs（旧设置/独立持久化/非法值边界）；docs/UI_DESIGN.md、README.md、PROJECT_STATUS.md。
- 本轮 UI：src/ScreenShot.App/App.xaml、UiDesign.cs（共享 token/style）；SettingsWindow.cs、App.xaml.cs（分类设置、保留原回调及诊断接入）；FloatingPanelWindow.cs（胶囊工具条、输入区、消息层级、窄宽适配及 UI 滚动时序）；FloatingBallWindow.cs（材质、轻微缩放、系统动画开关）；MarkdownMessageView.cs（Typography/间距/圆角）；RuntimeDiagnostics.cs（双主题/窄窗/原生状态验证）；scripts/publish.ps1（携带 UI 说明）；docs/UI_DESIGN.md、README.md、PROJECT_STATUS.md（设计与验证）。
- 2026-10-06：src/ScreenShot.App/FloatingPanelWindow.cs（移动区域、窗口位置/DPI/工作区、手动位置及生命周期；此前唯一显隐菜单和顶部布局）；PanelMoveController.cs、PanelMovePolicy.cs（鼠标捕获/来源过滤/累计坐标及位置限制）；App.xaml.cs（原生移动区域命中与 HWND 几何/恢复回归；既有菜单 Invoke/尺寸检查）；tests/ScreenShot.Tests/PanelMoveTests.cs、Program.cs、ScreenShot.Tests.csproj（33 项移动策略断言及接入）；README.md、PROJECT_STATUS.md（操作与验证）。本日此前 PanelResizeController.cs、PanelResizePolicy.cs 和 tests/ScreenShot.Tests/PanelResizeTests.cs 的顶部命中、来源解析及 26 项回归完整保留；更早未提交七项优化完整保留。
- 本次七项优化：src/ScreenShot.App/FloatingBallWindow.cs、FloatingPanelWindow.cs、App.xaml.cs（交互、发送入口、设置及原生诊断）；PanelResizeController.cs、PanelResizePolicy.cs（鼠标捕获、共享宽度及边界策略）；MarkdownMessageView.cs、src/ScreenShot.Core/MarkdownDocument.cs（原生渲染与解析）；src/ScreenShot.Core/Contracts.cs、src/ScreenShot.App/ToolbarWindow.cs（默认提示词及兼容旧入口）；tests/ScreenShot.Tests/PromptTests.cs、MarkdownTests.cs、PanelResizeTests.cs、Program.cs、ScreenShot.Tests.csproj（回归）；README.md、PROJECT_STATUS.md（操作和验证）。以下为此前 provider/UIA 修复涉及文件。
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
- 三块面板外缘使用原始强调色，其他控件/正文/材质维持原生中性主题资源；描边叠加绘制而不改变内容 BorderThickness，保留既有布局/窗口区域/动画和命中几何。高对比度使用 Microsoft.UI.System.ThemeSettings 与系统主题刷，关闭时解除事件。边框默认 1px，粗细随 DPI 缩放，关闭仅停止描边，不丢失宽度配置。
- 应用主题与强调色为 UI 外观字段，默认跟随系统；系统模式读取 UISettings 并将实际明/暗写到各窗口根，监听系统颜色变化，不修改 Windows 设置。自定义颜色仅接受不透明 RGB，使用原生控件及窗口作用域 Light/Dark 资源，高对比度继续使用系统色；主按钮文字优先可读，正文和材质维持中性色。全部窗口共享外观草稿，保存后持久化，关闭设置撤销未保存草稿。
- 图标先生成候选，用户本轮明确要求替换程序图标后接入；原图及提示词保留，可执行文件、窗口/任务栏、托盘和悬浮入口使用同一已确认设计。
- 透明背景 alpha 与实际原生窗口区域分开处理：隐藏底板必须将 HWND 区域限制为两栏圆角并集。可见底板按实际内容边界形成一个圆角区域；LayoutUpdated 同步区域，几何未变则跳过，避免重复修改和逐帧 HWND 尺寸变化。底板退场与两栏高度/间距同一时序；扩展始终固定左端，完成后恢复正常 Stretch 布局。
- 隐藏底板默认关闭，面板过渡默认开启，旧配置自动获得默认值。底板隐藏仅影响公共背景及外框，子区域独立材质/边框不变；取消隐藏恢复既有材质参数。小工具条约 202 逻辑像素，保留菜单箭头作为恢复入口，消息栏不随之收窄；仅小工具条显示时 HWND 收窄，扩展恢复正常宽度。动画使用现有 Motion token 和原生 Storyboard，不按帧改 HWND；快速切换冻结当前值再重定向。应用/系统禁用动画即时完成，截图前同步隐藏保持业务生命周期。Windows 在主动隐藏已激活窗口后转移焦点属于原生行为，恢复时不抢回焦点。
- 本轮参考 DeskBox 的 NavigationView、统一设置行、Capsule 及可调控制器设计模式，使用现有 WinUI / Windows App SDK 独立实现，无新依赖或源码/资源复制。共享 ResourceDictionary 使用 ThemeResource 和原生按钮模板。设置窗口使用 Mica；悬浮底板/两栏使用 Desktop Acrylic（后方当前可见桌面内容），三组外观独立配置，IsInputActive 与真实焦点分离，仍遵循系统材质降级。透明色层叠加在底板上，三个区域均选它才关闭全部额外模糊。外观保存独立于业务字段校验，关闭设置撤销外观草稿；常规保存/快捷键恢复/目录即时保存/连接检测保持原流程。settings.json 只增加兼容的 PanelAppearance 字段。
- 面板仍为同一个无标题栏 WinUI 窗口，正常两块 surface 共享宽度并一起移动，工具栏缩小时消息宽度保留；工具栏空白处、消息标题和非滚动区空白背景用于移动，边缘/角用于缩放，控件与可选文本保留原有操作。本次运行内保留手动位置，不将位置持久化为设置。顶部外侧与 Toolbar Top/顶部角改变整体高度，其余栏边缘分别调整高度。只使用一个 Button + 原生 MenuFlyout，菜单提供两栏“隐藏/显示”和工具栏“缩小/扩展”；工具栏可见时按钮在其右端，隐藏时同一个按钮移至消息标题右端，工具栏尺寸项暂禁用，不产生额外箭头或无恢复入口的空窗口。菜单及拖动入口参考 Astryx 的短动作菜单、控件操作隔离和无障碍提示约定，仅借鉴交互，不增加依赖。拖动使用真实鼠标左键捕获与屏幕坐标累计位移，不固定坐标定位控件。底部发送与工具栏 Codex 复用同一发送/防重发流程。ChatGPT 保留既有官网上传提交流程，默认提示词与旧设置/API 兼容。
- Markdown 使用纯 .NET 解析和原生 RichTextBlock/Run/Hyperlink/ScrollViewer，不使用 WebView、不拉取远程图片、不执行 HTML；仅开放合法 HTTP(S) 链接，常见块/行内语法可逐步渲染。80ms 内只渲染最新流式快照，普通最终 Text 立即刷新；收起/卸载不会重新打开窗口。
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
- 最新包 dist/AIScreenShot-win-x64-accent-borders-icon 已生成并通过实际 EXE 原生无发送诊断及产物一致性检查；从托盘退出旧实例后运行新版，桌面验收系统主题/颜色变化、原生材质及多屏操作。旧 panel-region-motion-fix / panel-transitions / adjustable-materials / fluent-ui 等包保留。工作目录与默认提示词操作保留；显式旧 CLI 路径可清空使其自动选择桌面版本，或指定新版。
- 完成真实 Codex 回复与多屏桌面验收；Chrome 重新加载原扩展后验证自动连接，再验证官网发送。
- 此前本地提交 cc4d1d9 包含原生面板、Codex provider/持久会话及旧修复，七项界面优化/顶部缩放/唯一显隐菜单/面板移动已在此前用户授权下合并为一次本地提交。本轮 Fluent UI、可调材质/过渡/底板修复、主题/边框与已确认图标按用户 2026-10-06 明确要求纳入本地 Git 提交；未 push。构建产物、便携包与临时诊断不纳入仓库。
