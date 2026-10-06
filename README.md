# AI ScreenShot

Windows 11 x64 原生托盘截图工具。`.NET 10 + WinUI 3 + Windows App SDK + Win32 + FlaUI.UIA3`。

## 运行

运行发布目录中的 `ScreenShot.App.exe`，屏幕右侧显示 52px 的原生悬浮球（尺寸为逻辑像素，随 DPI 缩放）。拖动后吸附到显示器工作区左侧或右侧；左键展开工具栏和消息栏目，再次点击收起两者，右键选择隐藏或设置。隐藏后可通过托盘“显示悬浮球”恢复。默认 `Ctrl+Alt+S` 框选，托盘也提供截图、设置和退出。

悬浮面板使用原生 WinUI 控件、系统浅色/深色主题、图标提示和无障碍名称。上方横向工具栏为 `Codex | ChatGPT | 复制 | 另存为 | 更多 | 下拉箭头`，下方为消息栏目；两者初始共同宽度约 520px，工具栏高度为 56px，消息栏目高度为 540px。两者左右边缘始终对齐，间距为 12px，在小工作区内限制窗口尺寸。左键拖动任一栏的四条边或四个角可调整大小：宽度同步变化，其余边缘分别调整各栏高度。当消息栏可见时，拖动窗口顶部外侧留白、工具栏顶部边缘或顶部角会改变整个窗口高度，保留工具栏高度，通过调整消息栏高度保持窗口底边位置。拖动范围限制在当前显示器工作区内，尺寸随 DPI 缩放。

按住工具栏空白处，或消息栏的“消息”标题及非滚动区空白背景，左键拖动可以改变面板位置。两栏一起移动并保持对齐，只有一栏可见时也能拖动。移动超过 4 个逻辑像素才开始调整位置；按钮、输入框、滚动区和可选择文本保留原有操作，边缘仍用于缩放。位置按鼠标所在显示器的工作区限制，并适配目标 DPI。本次运行中收起再展开、截图操作恢复及新截图会保留手动移动后的位置；退出重启后仍使用悬浮球附近的默认位置。

面板只保留一个显隐菜单箭头。两栏可见时，它位于工具栏右侧；点击先展开“隐藏工具栏”和“隐藏消息栏”两个选项，选择后才执行对应操作。隐藏消息栏后箭头仍留在工具栏；隐藏工具栏后同一个箭头移到消息标题栏右侧，供恢复操作。菜单文字随状态切换为“显示工具栏”或“显示消息栏”，选择恢复后保留消息、当前截图、提示词及原有高度。交替隐藏两栏时会显示另一栏，始终保留菜单入口。新截图确认后重新显示消息栏。

消息标题栏选择 Codex 工作目录，下方显示状态、对话、当前截图缩略图及提示词输入框。输入框旁的“发送”按钮默认发送到 Codex provider，与工具栏的 Codex 按钮使用相同流程。

用户截图和提示词显示在左侧气泡，Codex 回复显示在右侧气泡。左键点击缩略图可打开可缩放的截图详情。本工具的消息面板只保留内存中的最多 24 个气泡；已发送的 Codex 会话由 Codex 自身保存。“更多”可清空消息、发送到两端、复制提示词、退出截图或打开设置。再次点击悬浮球或在面板内按 `Esc` 会整体收起工具栏和消息栏并退出截图，当前图片和消息仍可在下次展开时查看。单独收起消息栏同样退出截图遮罩，但保留工具栏和当前内容。

用户提示词和 Codex 正文由原生 Markdown 渲染器展示，支持标题、粗体、斜体、删除线、行内代码、代码块、引用、列表、表格、分隔线及链接。代码块和表格支持横向滚动，文本可以选择。流式快照按约 80ms 合并渲染，最终回复立即刷新。链接仅允许有效的 HTTP(S) 地址；HTML 和 Markdown 图片语法保留为字面文本，不执行 HTML 或加载远程图片。此渲染器使用 WinUI 控件，没有 Web UI 依赖。

启动截图前隐藏自身悬浮窗口。无边框顶层 Overlay 覆盖整个虚拟桌面（所有显示器），冻结当前画面并显示半透明深色遮罩，鼠标为十字准星。拖动时按物理像素绘制原亮度选区；松开后保留选区并展开面板，尚未手动移动时定位在悬浮球附近。可以直接在遮罩上重新框选；零面积拖动不确认截图。遮罩上的 `Esc` 或右键退出；快捷键或“截取屏幕”重新获取桌面。

点击 Codex 后退出截图遮罩，保留当前图片、工具栏和消息栏目；从提交到回复结束始终保持面板可见。确认提交后持续更新 provider 的公开工作状态和回复文本，尚无输出时显示“正在思考 / 正在处理”，完成后展示最终回复并结束等待。正文在同一个气泡中更新；处于底部时自动跟随，向上翻阅时保留位置。发送状态与 Codex 回复分别标识，回复等待没有固定超时，生成时间长不会被误判为发送结果未知。失败或真实无法确认的提交仍提供明确反馈，禁止自动重发。ChatGPT 和保存对话框期间仍临时隐藏自身窗口，结束后恢复。点击 ChatGPT 直接通过浏览器扩展打开官网新对话，发送当前截图及有效提示词；此次用户内容和发送回执都不加入消息气泡，面板状态区显示成功、失败或未知结果，回复在官网查看。用户主动收起或关闭面板时，异步完成不会重新打开它。

拖动绘制采用保留的内存缓冲区，仅更新选区变化条带及旧/新边框；鼠标消息记录最新区域，像素合成在 `WM_PAINT` 执行，避免高频鼠标移动反复拷贝整个虚拟桌面。

设置页的“默认提示词”支持多行内容；发送时用户输入的非空提示词优先，输入留空或只有空白时使用该配置。默认值为“请描述并分析这张截图”，将设置留空时也恢复此值；Codex、ChatGPT 和“复制提示词”使用同一规则。消息标题栏选择已配置的 Codex 工作目录，工具栏 Codex 或输入框旁的“发送”按钮发送；ChatGPT 每次新建官网对话。两端独立反馈。已提交或结果未知的目标对当前图片不允许再次发送；新截图会重置该限制。发送和等待回复期间禁用会改变当前任务的操作，消息栏目中的停止按钮可结束回复读取，不会取消或重发已提交的 Codex 消息。

## ChatGPT 扩展安装

1. 打开 `edge://extensions` 或 `chrome://extensions`，打开开发者模式，选择“加载解压缩的扩展”，选择发布目录 `extension`。
2. 复制扩展 ID。在源码根目录或发布目录中运行以下命令（无需管理员权限；请替换 ID，Chrome 用户将 Edge 改为 Chrome）：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\register-extension.ps1 -ExtensionId <扩展ID> -Browser Edge
```

脚本优先查找所在发布目录的 `native-host/ScreenShot.NativeHost.exe`；在源码目录运行时依次查找 `dist/AIScreenShot-win-x64-final`、`dist/AIScreenShot-win-x64`。找不到时先运行 `scripts/publish.ps1`。自定义发布位置可显式指定 `-HostPath 'D:\自定义目录\native-host\ScreenShot.NativeHost.exe'`，这是部署来源；添加 `-WhatIf` 可仅检查来源而不部署或修改注册。

正式运行文件会完整复制到宿主 Windows 的 `%LOCALAPPDATA%/AIScreenShot/native-messaging/host/<运行文件指纹>/`，manifest 写到同级 `Chrome.json` 或 `Edge.json`。注册脚本通过已登录用户的 Explorer 启动隐藏的桌面 PowerShell；桌面进程核对文件句柄的物理路径和所有部署文件的 SHA-256，再写入 HKCU 32/64 位注册表。这避免从 Codex 等 MSIX 应用启动时，AppData 写入被重定向到应用的 `Packages/.../LocalCache`，却将不可见的逻辑路径注册给 Chrome。Explorer 不可用、发生重定向或验证失败时脚本报错；可从普通 Windows PowerShell 窗口重新运行。无需管理员权限，不修改 MSIX 或浏览器安全政策。

仅检查现有注册（同样通过宿主桌面进程核验真实文件）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\register-extension.ps1 -ExtensionId <实际扩展ID> -Browser Chrome -VerifyOnly
```

3. 启动截图工具。扩展安装/更新、浏览器启动或后台重新启动时会自动连接；程序退出或重启后自动重连，无需每次打开扩展弹窗。设置页可检测连接，弹窗保留“立即重连”用于排错。
4. 浏览器必须登录 ChatGPT，并具有图片上传能力。Chrome 用户在设置和注册命令中选择 Chrome。

自包含便携目录仍需上述一次性 Native Messaging 部署与注册。移动发布目录不影响已经部署的 Host；更新 Native Host 后重新注册以部署新版。仅升级本次自动连接扩展（0.1.1）时，在扩展管理页重新加载原扩展即可，不需要更换扩展 ID 或重新注册 Host。旧运行文件保留，避免覆盖正在运行的 Host。不会读取 Cookie 或调用非公开 ChatGPT HTTP 接口。

自动连接增加 `alarms` 权限，用于 MV3 service worker 休眠后的后台唤醒，不扩大网页访问范围。断开后按 1/2/5/10/30 秒退避尝试连接；浏览器后台休眠后由每分钟的 alarm 恢复检查，浏览器忙碌、休眠或设备睡眠可能导致延迟（参考 [Chrome alarms 文档](https://developer.chrome.com/docs/extensions/reference/api/alarms)）。重连只发送握手、清除未完成的分块，不缓存或自动重发截图与提示词；旧连接的发送结果不会写入新连接。

卸载桥接：`powershell -ExecutionPolicy Bypass -File .\scripts\unregister-extension.ps1 -Browser Edge`；再从浏览器移除扩展。脚本仅移除本工具的注册项。

### 扩展连接故障

如果显示 `Specified native messaging host not found`，先使用最新版注册脚本运行 `-VerifyOnly`，确认输出 `PhysicalFilesVerified: true`。仅在 Codex 环境执行 `Test-Path` 可能读到 MSIX 合并视图，不能证明 Chrome 可以访问该文件。也可在普通 Windows PowerShell 中执行 `Test-Path "$env:LOCALAPPDATA\AIScreenShot\native-messaging\Chrome.json"`，再读取 JSON 并检查其 `path`；两者应为 `True`，路径不应包含 `Packages/.../LocalCache`。注册后在浏览器扩展管理页重新加载 AI ScreenShot Bridge，等待自动连接；也可打开弹窗点击“立即重连”。自动重连不能修复缺失的 Host 注册。

如果重连仍失败，在扩展详情页复制实际 ID，用同一浏览器的参数重新注册。请保留新的完整错误信息；`Native host has exited` 表示连接已进入 Host 启动阶段，此时先确认截图程序正在运行。错误分类参考 [Chrome Native Messaging 排错说明](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging#debug-native-messaging)。

## Codex

正常发送现使用 Codex CLI 的公开 App Server provider。无需打开 Codex 桌面窗口；发送后切换、收起或关闭桌面聊天不影响截图工具接收回复。实现参考 Amadeus 的 provider 事件路由，使用 .NET `Process` / JSONL 独立接入，不引入 Python、Electron、Web UI 或新的生产依赖。

1. 安装并登录 Codex CLI。若检测提示未登录，在终端运行 `codex login`。程序复用 CLI 管理的登录，不读取或保存登录令牌。
2. 在消息标题栏下拉菜单中点击“选择工作目录…”，使用原生文件夹选择器选择目录。最近选择的目录会保留；不会自动把旧的桌面项目名称推测为路径。
3. 截图并填写提示词，点击工具栏 Codex 或输入框旁的“发送”。图片以内存中的 PNG data URL 和有效提示词一起发送，助手回复在当前气泡中持续更新并渲染 Markdown。

留空 CLI 路径时，程序优先使用正在运行的 Codex 桌面内置 `codex.exe`，其次查找已安装的桌面运行时，再回退到 PATH 或标准 npm Windows 安装。设置中的显式 `.exe` 完整路径优先。设置页“检测 Codex provider”检查初始化、登录及所选工作目录的模型配置和图片能力，不创建聊天或发送消息。旧 CLI 的模型目录可能不包含桌面正在使用的新模型；检查失败时请使用桌面内置版本或升级 CLI，程序不会自动更换模型或降低 reasoning effort。

每次截图发送通过公开 `thread/start` 创建独立的持久会话（`ephemeral=false`），使用所选 `cwd`、只读 sandbox 和 `approvalPolicy=never`。会话标题以 `AI ScreenShot ·` 开头，可在 Codex 中查找；标题设置被拒绝时仍继续发送。助手用于分析并回答；需要交互或扩大权限的服务端请求不自动批准。沿用 CLI 的模型配置。图片、提示词和回复由 Codex 按正常聊天保存，不写入本工具内容日志；截图工具不访问 Codex 内部数据库。

先进行 `initialize` / `initialized` 握手，再调用 `thread/start` 和一次 `turn/start`。准备与本地提交回执分别最多等待 90 秒，生成阶段持续读取到真实终态。有效 `turn/start` 回执才表示已提交；发送写入开始后丢失回执返回 Unknown，禁止自动重发。回复按 `threadId + turnId + itemId` 绑定，`item/agentMessage/delta` 更新同一气泡，完整 `item/completed` 修正正文，只有 `turn/completed` 才结束等待。最终优先使用 `final_answer`，不将过程文字或私人推理当作最终答复。协议以本机 CLI 生成的 schema 为准，官方流程见 [Codex App Server 文档](https://learn.chatgpt.com/docs/app-server)。

“停止等待”保留已经收到的正文，停止当前消息栏的后续更新。已经接受的请求仍在后台排空到终态，不取消、不重发；退出截图程序会关闭它自己启动的 provider 进程。流中断会结束忙碌状态并明确显示未完整接收，已提交的消息不会变回可重发状态。此发送路径不读写系统剪贴板，也不查找、激活或操作 Codex 桌面控件。

回复失败显示公开协议的错误类别及有效 HTTP 状态（如授权、额度、模型配置或连接问题），并保留已经收到的正文；自由格式的远端错误消息不会写入日志或直接回显。服务端连接重试期间继续等待 `turn/completed`，本工具不会再次提交截图或提示词。

旧 UI Automation 发送和剪贴板保护代码保留给已有的窗口、欢迎页、手动发送专项诊断，不再作为正常 Codex 发送入口。ChatGPT 浏览器扩展流程不变。

## 构建与检查

以下命令在源码目录执行；运行自包含发布目录不需要 SDK。

SDK 已放置在 `.tools/dotnet`。没有该目录时安装 .NET 10 SDK。首次还原下载 Windows App SDK、Windows SDK BuildTools 和 FlaUI.UIA3；FlaUI 是现成 UI Automation COM 封装，减少底层代码。UI 使用原生控件，无 Web UI 依赖。

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

输出为 `dist/AIScreenShot-win-x64`。SDK 构建、协议测试、真实桌面联调的实际结果见 `PROJECT_STATUS.md`，不能将构建成功视为两端发送成功。

本轮发布目录为 `dist/AIScreenShot-win-x64-panel-move`，新增工具栏空白处和消息标题/空白背景的左键拖动，两栏共同移动并保留本次运行中的手动位置。此前唯一显隐菜单、顶部拖动高度调整、悬浮球再次点击收起、底部发送、原生 Markdown、对齐缩放、ChatGPT 官网直发、默认提示词，以及 provider 的 CLI 选择、模型兼容检查和聊天保存修复保留，旧包保留。请先从托盘退出旧实例，再启动新版，避免单实例锁使旧版继续响应快捷键。本轮核心测试通过 693 项断言，含新增 33 项移动阈值、累计位移、工作区和坐标边界检查；详细验证结果见 `PROJECT_STATUS.md`。

运行诊断（可选）：`ScreenShot.App.exe --smoke-test --browser Chrome`。生成的 `diagnostics` 仅用于本机测试；界面图片由 WinUI 控件渲染，不保存窗口后方的桌面。真实发送测试参数 `--test-codex-workspace AI_ScreenShot` 和 `--test-chatgpt` 仅与 `--smoke-test` 一起使用，发送的是生成的渐变测试图。使用之前关闭已有实例；默认正常启动不执行发送测试。

Provider 连接专项诊断：`ScreenShot.App.exe --codex-provider-test` 检查真实 CLI 的 stdio 初始化、账号就绪及配置模型的图片能力和 reasoning effort 兼容性，使用独立 Mutex。结果位于 `diagnostics/codex-provider-test.txt`；不创建 thread/turn，不提交截图或提示词，也不记录账号内容。

悬浮界面专项诊断：`ScreenShot.App.exe --floating-ui-test` 检查 52px 悬浮球、空状态、浅色/深色布局、球点击展开/收起、单个显隐菜单入口、打开菜单不改变两栏状态、两项文字及菜单选项执行后的折叠/恢复和内容保留；通过原生 Button 与 MenuFlyoutItem 的 Invoke 验证同一箭头在可见栏之间移动、选择后关闭菜单。另检查顶部留白和工具栏边缘的可视树命中、生产 resize 回调的顶部高度变化/底边锚定/共同宽度及工作区限制；通过真实可视树命中和生产移动回调验证工具栏背景及消息标题可移动、控件/可选文本/缩放 grip 不能启动移动、点击阈值、尺寸/对齐/前台保持、手动位置恢复及单栏移动。原生 Markdown 控件与流式合并/最终刷新、默认提示词设置编辑器、底部发送按钮命令和 ChatGPT 不产生面板气泡的状态反馈同时检查。保留处理状态、截图详情、异步取消的窗口生命周期检查，通过注入发送回调验证 Codex 提交/等待/完成持续可见、不改变前台、后台回调不丢回复、取消及防重发。结果与生成图片位于 `diagnostics/floating-ui-test.txt`、`floating-*.png`。预览气泡明确标为布局示例，不是真实 Codex 回复。此诊断使用独立 Mutex，不抓取桌面、不读写系统剪贴板、不连接浏览器、不执行真实发送；可视树命中和回调验证没有注入系统鼠标事件，不代替实际鼠标拖动、混合 DPI 多屏及官网上传发送验收。正常启动使用新悬浮 UI，旧 `ToolbarWindow` 只保留给既有 smoke/toolbar 诊断。

旧 UIA 窗口识别专项诊断：`ScreenShot.App.exe --codex-window-test` 通过旧 UIA 发送定位器只读检查 Codex 主窗口和工作区匹配，结果位于 `diagnostics/codex-window-test.txt`。需先打开 Codex 并展开项目列表；此诊断使用独立 Mutex，不激活窗口、不导航、不读写剪贴板、不发送消息，日志只记录检查结果、数量或异常类型/HRESULT。

旧 UIA 欢迎页就绪专项诊断：将 Codex 主窗口停在指定工作区的空欢迎页，运行 `ScreenShot.App.exe --codex-ready-test "工作区名称"`，检查旧 UIA 发送使用的工作区/空草稿判断、错误工作区拒绝、无附件及提交上下文准备。结果位于 `diagnostics/codex-ready-test.txt`；此诊断同样只读，不自动切换聊天、清空草稿、访问剪贴板或发送消息。它不能代替图片上传和回复回显的真实验收。

本机程序已启动时，可以运行 `node tests/native-host-smoke.cjs` 验证发布包 Host 的二进制帧和 Named Pipe 握手。检查已部署的 Chrome Host 时运行 `node tests/native-host-smoke.cjs '<注册脚本输出的HostPath>' Chrome <实际扩展ID>`。检查前暂时断开同一浏览器的扩展连接，避免重复握手。该检查不代表浏览器官网发送已通过。

框选专项诊断：运行 `ScreenShot.App.exe --overlay-test` 检查生成的测试图在真实 GDI 缓冲区中的像素对齐、裁剪、反向选区、选区边框、资源释放、增量/完整绘制一致性、失效区覆盖与重复窗口生命周期，并输出 4K 生成画面合成基准，结果写入 `diagnostics/overlay-test.txt`。此诊断使用独立 Mutex，可与正常实例并行，不连接浏览器桥接。运行 `ScreenShot.App.exe --toolbar-test` 检查原生工具栏浅色/深色渲染、布局和会话退出，结果写入 `diagnostics/toolbar-test.txt`；仅保存生成的工具栏界面，不抓取或记录桌面，不执行发送。工具栏诊断也使用独立 Mutex，不连接浏览器桥接，可与正常实例并行，并验证 Codex 成功退出截图及失败/未知保留截图。这两项诊断不代替真实 Codex 发送、实际鼠标拖动和混合 DPI 多屏验收，合成基准不代表桌面帧率。

## 范围与隐私

V1 支持静态矩形截图、复制和 PNG 保存；不支持录屏、滚动截图、OCR、标注、HDR 色彩还原或受保护内容。截图与提示词不写日志。设置保存在 `%LOCALAPPDATA%/AIScreenShot/settings.json`。浏览器桥接使用当前用户 Named Pipe 和扩展 origin 白名单。

参考 [ShareX](https://github.com/ShareX/ShareX) 的交互流程，未复制 GPL 源码；浏览器上传参考 [ChromeGPT](https://github.com/blixt/ChromeGPT) 并独立适配。浏览器 DOM、Codex provider 协议及旧 UIA 诊断结构可能随版本变化，失败时需更新适配器。
