# 项目状态

## 已完成
- 2026-10-02 剪贴板兼容修复：用户日志确认提交前“准备图片剪贴板”报 COMException / 0x800401D3。改为标准格式专用 API、可读自定义格式备份和逐格式处理 CLIPBRD_E_BAD_DATA；非空且全部不可读时不写剪贴板。位图、HTML 资源及自定义流独立复制，保留 RequestedOperation；空数据不引入额外元数据。
- 图片/提示词写入后立即追踪剪贴板 sequence，并在 Flush 结束且非零 owner 不变时更新；恢复时仍检查 sequence。恢复 Flush 失败时保留仍在剪贴板使用的流直到被替换/退出，避免提前释放。诊断区分“备份原剪贴板”与“写入图片剪贴板”，空异常消息显示类型/HRESULT。
- 最新 scripts/verify.ps1 通过：App/Native Host 构建 0 warning / 0 error，35 项核心/Codex/备份策略断言、15 个扩展测试与三个脚本语法检查通过。新增 --clipboard-test 实测 12 项内存 WinRT 克隆断言通过，验证旧数据源释放后位图/HTML/custom stream 仍可读取；未读写系统剪贴板，未执行真实 Codex 发送。独立新版 dist/AIScreenShot-win-x64-clipboard-fix 已生成。
- 2026-10-02 Codex 发送修复：重新获取 ProseMirror Edit 输入框，读取控件时对 UIA_E_ELEMENTNOTAVAILABLE (0x80040201) 进行有界重试；Invoke 后控件失效只观察结果，不重放动作。提交前确认提示词，提交后同时确认输入框清空和草稿/侧栏之外新增同内容消息；适配非 Text 消息节点及空白差异。
- Codex 主按钮确认 Sent 后结束截图会话，失败/Unknown 保留截图及防重发；ChatGPT、保存和发送到两端反馈保留。新增 codex-send.txt 失败阶段诊断，不记录截图、提示词、聊天或剪贴板内容。
- 本轮 scripts/verify.ps1 通过：App/Native Host 构建 0 warning / 0 error，27 项核心/Codex 自动化断言、15 项扩展测试和三个扩展脚本语法检查通过。独立 --toolbar-test 实测通过浅色/深色布局、显隐联动、成功退出、失败/Unknown 保留、两端反馈保留及退出后不恢复。生成独立修复包 dist/AIScreenShot-win-x64-codex-fix；尚未在本轮执行真实 Codex 图片发送。
- 2026-10-02 扩展 0.1.1 自动连接：扩展安装/更新、浏览器启动、MV3 worker 重建时主动握手；断开后按 1/2/5/10/30 秒退避重连，alarms 每分钟提供休眠后唤醒兜底。握手超时、Host 缺失、重复浏览器连接均保留错误反馈并重试，保留“立即重连”排错入口。
- 自动重连仅恢复连接，不重放截图/提示词；清除旧的未完成分块，异步上传回复绑定收到请求的原始 Port，防止旧结果写入新连接。未修改 Native Host、Pipe 协议或官网上传流程，不需要因本次升级重新注册 Host。
- Chrome Default 的 Secure Preferences 确认实际加载目录为 B:/Code/Dev/AI_ScreenShot/extension，源目录已经更新。当前自动化仅提供 Codex IAB/MCP Apps，未能操作 Chrome 扩展管理页；需用户重新加载一次原扩展，真实 Chrome 自动连接/程序重启恢复尚未验收。
- 2026-10-02 框选绘制修复：统一 top-down DIB、物理像素坐标和持久 GDI 缓冲区，避免中间暗帧和源坐标错位。随后针对拖动卡顿改为保留未变化的选区内部，仅合成选区差集条带及旧/新边框；鼠标消息只更新选区和局部失效区，WM_PAINT 按最新选区合成，边框 Pen 缓存复用。
- 框选会话优化：虚拟桌面无边框顶层遮罩、十字准星、松开后保留选区并显示工具栏；支持直接重选，Esc/右键/关闭同时退出；异步裁剪采用版本检查，取消或重选后不会弹出旧工具栏。
- 工具栏参考 WinUI 3 Gallery CommandBar 示例，使用原生 CommandBar/AppBarButton、主题资源、图标、Tooltip 和无障碍名称；选区下方居中，边缘处自动上移/限制工作区。保存与发送期间暂时隐藏两个窗口，取消的会话不恢复。
- 新增 --overlay-test 与 --toolbar-test 专项运行诊断，仅使用生成像素/控件渲染，不记录桌面、不执行发送。像素检查最新通过：19,314 次 GDI 像素断言、171,680 次增量/完整绘制比对及失效区覆盖、选区边框、GDI 资源 4→4、重复窗口生命周期。上一轮浅色/深色工具栏布局、显隐联动、取消后不恢复检查通过，本轮未更改工具栏。
- 本机 4K 生成画面合成基准（80 帧、每帧 GdiFlush）：完整合成 4.168 ms/帧，增量合成 0.154 ms/帧；选区边缘移动 1 像素的更新面积 17,997/8,294,400（0.217%）。仅衡量合成路径，不代表实际桌面帧率或用户体感验收。
- 本次 scripts/verify.ps1 最终通过：App/Native Host 构建 0 warning / 0 error，核心 22 项断言、扩展 15 个测试（13 项连接状态/重连测试及 2 项协议测试）、background.js/connection.js/popup.js 语法检查通过，注册脚本 PowerShell 语法检查通过。scripts/publish.ps1 已生成独立 dist/AIScreenShot-win-x64-auto，原发布目录保留。
- 本地 .NET SDK 10.0.401；WinUI 3 / Windows App SDK 2.5.1 / FlaUI.UIA3 5.0.0。
- Win32 托盘、全局快捷键、GDI 矩形截图、多屏物理坐标、取消、复制与 PNG 保存。
- 原生浮动工具栏、工作区选择、设置、两端独立发送及失败/未知状态反馈。
- Chrome/Edge MV3 扩展、Native Messaging Host、当前用户 Named Pipe、分块/长度/哈希校验。
- 修复发布遗漏 App.xbf，以及 Codex 桌面扫描阻塞和导航校验时序问题。
- Codex 实测读取 22 个唯一工作区，并在 AI_ScreenShot 新聊天发送生成的测试图及提示词，提交消息已确认。
- 运行诊断通过：PNG crop、重复框选窗口生命周期、工具栏创建与原生控件渲染。
- 2026-10-02 修复 Native Messaging 部署重定向：通过 Explorer 桌面进程部署完整 Host，检查文件句柄物理路径及全量 SHA-256，再写入 HKCU 32/64 位注册表；支持 -VerifyOnly，保留 -HostPath 与 -WhatIf。
- 宿主验证通过：真实 Chrome.json 与 Host exe 存在，194 个运行文件的物理路径正确；manifest name/type/origin 与注册值一致。重复注册、WhatIf、不存在来源拒绝、LocalCache 来源拒绝、错误 origin 拒绝、便携脚本验证均通过。
- 已部署 manifest：C:/Users/Lenovo/AppData/Local/AIScreenShot/native-messaging/Chrome.json。
- 已部署 Host：C:/Users/Lenovo/AppData/Local/AIScreenShot/native-messaging/host/9f121322b96baa69/ScreenShot.NativeHost.exe；真实 Chrome 协议分帧与 Named Pipe 握手通过。

## 未完成
- 使用最新 clipboard-fix 包复测真实 Codex 新聊天发送（包含短提示词/多行提示词、附件上传和原剪贴板多格式）；当前窗口读取确认 Edit 输入框提供 TextPattern、内存备份检查通过，但不等于端到端发送已通过。若仍报错，提供新版 codex-send.txt。
- 用户在 Chrome 重新加载现有扩展后的真实自动握手、浏览器先启动/程序后启动、程序重启以及 worker 休眠恢复验收；Node 中模拟的 Chrome API 测试不能当作浏览器实测。
- 新 Overlay 的实际鼠标连续拖动、混合 DPI 跨屏对齐、屏幕边缘工具栏与保存对话框交互验收；自动像素检查不能代替肉眼无闪动验收。
- 本次部署修复后的 Chrome 扩展实际连接、官网图片上传与发送验收。先前测试等待连接超时，未上传测试图；不得将 Host 握手通过视为官网发送通过。
- 高对比度、剪贴板并发修改等桌面验收；工具栏浅色/深色自动渲染已通过。

## 当前 Bug
- 用户日志 0x800401D3 发生在旧“准备图片剪贴板”复合阶段，尚未确认具体原始格式或读取/写入哪一步；已修复全格式 generic GetDataAsync 一处失败终止整个备份的兼容问题并细分阶段，真实发送待最新包复测。
- 用户报告 Codex 发送结果未知和 0x80040201：确认原代码长期持有上传前输入框、未处理控件失效及结果仅匹配 Text 节点，已补强；具体触发阶段和真实发送修复效果待新版复测。
- 用户报告框选区域变化时卡顿：代码确认原实现每次鼠标移动同步完整合成虚拟桌面并全屏失效，已改为 WM_PAINT 合并与差集条带局部更新并通过像素/性能检查；真实鼠标顺滑程度、多屏及闪动/错位效果仍待用户验收。
- 已确认并修复注册脚本根因：Codex 启动的进程看到 MSIX 合并视图，逻辑 AppData 写入物理落到 Packages/OpenAI.Codex_2p2nqsd0c76g0/LocalCache/Local；Chrome 读取宿主路径时找不到文件。此前 Codex 内的 Test-Path 和 Host 握手产生了错误的可访问性判断，不能证明 Chrome 可见。
- 旧包缓存文件保留，不作为正式 Host 依赖；从 Codex 直接读取旧逻辑路径可能仍看到旧缓存，使用 -VerifyOnly 获取宿主验证结果。
- 当前会话 Computer Use 只提供 Codex 内置浏览器，没有 Chrome 自动化入口，无法代替用户完成已安装扩展的重新加载与真实浏览器连接验证。

## 最近修改文件
- src/ScreenShot.App/ClipboardBackup.cs、ClipboardBackupPolicy.cs、ClipboardService.cs、CodexTarget.cs、Native.cs：独立内存备份、逐格式兼容、sequence/owner 跟踪、恢复资源生命周期及细分错误提示。
- src/ScreenShot.App/RuntimeDiagnostics.cs、App.xaml.cs、tests/ScreenShot.Tests/Program.cs、ScreenShot.Tests.csproj：内存 WinRT 专项诊断和备份策略回归。
- src/ScreenShot.App/CodexTarget.cs、CodexAutomation.cs：控件重新获取、失效读取重试、提交确认和无内容失败诊断。
- src/ScreenShot.App/ToolbarWindow.cs、App.xaml.cs：Codex 主按钮成功退出、结果反馈处理及隔离工具栏专项诊断。
- tests/ScreenShot.Tests/Program.cs、ScreenShot.Tests.csproj、README.md、PROJECT_STATUS.md：失效/取消/超时回归验证和新版操作说明。
- extension/connection.js、connection.test.js、background.js、manifest.json、popup.html、package.json：自动连接状态机、alarms、原 Port 结果绑定与连接测试；版本 0.1.1。
- src/ScreenShot.App/BrowserBridge.cs、App.xaml.cs：未连接错误提示及设置说明适配自动连接。
- scripts/verify.ps1、register-extension.ps1、README.md、PROJECT_STATUS.md：新增测试/语法检查、注册完成说明与升级操作。
- src/ScreenShot.App/SelectionOverlay.cs、OverlaySurface.cs：保留缓冲区、差集条带局部更新、鼠标/绘制消息解耦及 Pen 复用。
- src/ScreenShot.App/App.xaml.cs、ToolbarWindow.cs：截图会话协调、异步版本检查、WinUI 原生工具栏与保存/发送显隐。
- src/ScreenShot.App/Native.cs、DesktopCapture.cs、RuntimeDiagnostics.cs：DIB/诊断 API、生成像素入口、专项运行检查。
- scripts/register-extension.ps1：委托 Explorer 桌面进程执行部署/注册和只读验证。
- scripts/native-messaging-desktop.ps1：真实目录完整部署、依赖哈希、物理文件路径与双视图注册校验。
- tests/native-host-smoke.cjs：支持指定已部署 Host、浏览器与扩展 ID。
- README.md、PROJECT_STATUS.md；两个 dist 发布目录同步新版注册脚本、桌面 worker 与 README。

## 已确认设计决策
- 剪贴板只恢复可读取格式；CLIPBRD_E_BAD_DATA 的单格式不阻塞其他格式，其他读取错误仍传递，非空且全部不可读时不写入。备份不访问磁盘保存内容，不引入生产依赖。
- 截图以启动时冻结画面为准，整个虚拟桌面共用物理像素坐标；遮罩保留到用户取消、重新截图、打开设置或 Codex 主按钮确认发送成功。Codex 失败/Unknown、ChatGPT 和发送到两端完成后恢复工具栏反馈，Unknown 防重发逻辑保留。
- 增量更新仍在内存合成后显示，保持前次消除闪动的双缓冲路径；不引入生产依赖。--overlay-test 使用独立诊断 Mutex，可在旧实例运行时执行生成像素测试，不连接浏览器桥接或修改旧实例。
- 借鉴 Astryx 图标按钮的 Tooltip/无障碍约定，原生实现遵循 WinUI 3 Gallery；不引入 React、Web UI 或额外依赖。
- WinUI 3 原生 UI；Windows x64 自包含便携包；Chrome/Edge MV3 扩展。
- Native Host 名保持 com.aiscreenshot.bridge；本次只改变扩展连接生命周期，官网上传和发送业务逻辑保留。
- `alarms` 是低敏感度后台定时权限，不新增网页权限/生产依赖。定时重连受浏览器调度和设备休眠影响，不承诺即时连接；自动重连不能修复错误的 Host 注册。
- 实际已安装 Chrome 扩展 ID 为 dmcdjpekhmjmcbjcaiillldpekdopocd；用户后续粘贴的 dmcdjpekhmjmcbjcaillldpekdopcocd 与安装记录不一致，注册使用实测 ID。
- Host 正式运行文件在真实 LocalAppData 下按完整运行文件指纹部署，不依赖源码、便携包或 Codex LocalCache；保留旧版本，避免覆盖运行中的 Host。无需管理员权限，不修改安全政策。
- Codex 通过 UI Automation 在已配置项目新建聊天，不访问内部数据库。
- 两端独立反馈；Unknown 不自动重发；不覆盖用户在剪贴板中的并发修改。

## 后续计划
- 在 Chrome 重新加载原 AI ScreenShot Bridge 扩展，确认无需点击连接即可握手及程序重启后恢复；再使用生成的 fixture.png 验证官网发送。
- 完成剩余桌面验收；最新发布包为 dist/AIScreenShot-win-x64-clipboard-fix，包含之前的 Codex 控件/成功退出修复及拖动性能优化，原发布包保留。先从托盘退出旧实例，再运行新版 ScreenShot.App.exe，以免单实例互斥锁使旧版继续响应快捷键。
- 当前目录无 .git；没有执行 Git init/commit/push。
