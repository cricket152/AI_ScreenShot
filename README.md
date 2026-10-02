# AI ScreenShot

Windows 11 x64 原生托盘截图工具。`.NET 10 + WinUI 3 + Windows App SDK + Win32 + FlaUI.UIA3`。

## 运行

运行发布目录中的 `ScreenShot.App.exe`，默认 `Ctrl+Alt+S` 框选；右键托盘打开设置或退出。程序没有复杂主界面。

启动截图后，无边框顶层 Overlay 覆盖整个虚拟桌面（所有显示器），冻结当前画面并显示半透明深色遮罩，鼠标为十字准星。拖动时按物理像素绘制原亮度选区；松开后保留选区，在下方居中弹出工具栏，空间不足时移到上方并限制在目标显示器工作区内。可以直接在遮罩上重新框选；零面积拖动不确认截图。`Esc`、遮罩右键或工具栏“退出截图”同时收起遮罩和工具栏；快捷键或“重新截图”会重新获取桌面。

工具栏参考 [WinUI 3 Gallery CommandBar 示例](https://github.com/microsoft/WinUI-Gallery/blob/main/WinUIGallery/Samples/CommandBar/CommandBarPage.xaml)，使用原生 `CommandBar` / `AppBarButton`、主题资源、圆角、图标提示和无障碍名称。布局为：

`提示词输入框 | Codex ▾ | ChatGPT`

`选区像素尺寸 | 复制 | 保存 | 重新截图 | 更多 | 退出截图`

发送和保存对话框期间临时隐藏遮罩与工具栏。点击 Codex 主按钮并确认发送成功后退出截图状态；发送失败或结果未知时恢复截图与反馈，禁止对未知结果自动重发。ChatGPT、发送到两端及保存结束后仍恢复反馈；截图模式已被取消时不再恢复。

拖动绘制采用保留的内存缓冲区，仅更新选区变化条带及旧/新边框；鼠标消息记录最新区域，像素合成在 `WM_PAINT` 执行，避免高频鼠标移动反复拷贝整个虚拟桌面。

空提示词使用“请描述并分析这张截图”。Codex 下拉选择已配置的工作区，主按钮发送；ChatGPT 每次新建官网对话。“更多”支持发送到两端及复制提示词。两端独立反馈。已提交或结果未知的目标在当前截图会话不允许再次发送。

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

打开 Codex 并展开项目列表。只支持 UI Automation 能识别的已配置本地项目；重名项目不自动发送。程序不会修改 Codex 的内部数据库、权限或配置。工作区、输入框、附件或发送结果无法可靠确认时会停止，并保留截图供手动粘贴。

Codex 导航或附件上传可能替换输入框，程序会重新读取控件，并对 `UIA_E_ELEMENTNOTAVAILABLE (0x80040201)` 重试读取；点击、粘贴和提交不自动重放。提交前确认提示词已经进入输入框，提交后确认输入框清空及输入框/侧栏之外新增同内容消息，不依赖消息必须是 `Text` 类型。确认成功后退出截图状态。

发送失败时，可提供 `%LOCALAPPDATA%/AIScreenShot/diagnostics/codex-send.txt`，包含最近一次失败的时间、阶段、是否尝试提交、异常类型与 HRESULT，不记录截图、提示词、剪贴板或聊天内容。从 Codex 启动程序时，文件可能位于 `%LOCALAPPDATA%/Packages/OpenAI.Codex_2p2nqsd0c76g0/LocalCache/Local/AIScreenShot/diagnostics/`。旧版没有此诊断文件。

Codex 发送前备份原剪贴板：标准格式使用对应的读取/写入 API，可读自定义格式也会保留，位图和 HTML 资源/自定义流复制到独立内存。仅跳过返回 `CLIPBRD_E_BAD_DATA (0x800401D3)` 的无效格式，这些无法读取的格式无法恢复；非空剪贴板如果没有任何可备份数据，会停止发送并保留原内容。备份、写入图片和写入提示词使用独立诊断阶段。发送期间发现剪贴板已被用户修改时停止，并且不恢复旧内容覆盖用户修改。

`ScreenShot.App.exe --clipboard-test` 使用生成数据在内存中验证 Text、RTF、HTML/资源、Bitmap、链接、自定义字符串/流、RequestedOperation 及空数据备份，结果在 `diagnostics/clipboard-test.txt`。此诊断使用独立 Mutex，可与正常实例并行；不读取或修改系统剪贴板、不发送 Codex 消息，不能代替真实发送验收。

## 构建与检查

以下命令在源码目录执行；运行自包含发布目录不需要 SDK。

SDK 已放置在 `.tools/dotnet`。没有该目录时安装 .NET 10 SDK。首次还原下载 Windows App SDK、Windows SDK BuildTools 和 FlaUI.UIA3；FlaUI 是现成 UI Automation COM 封装，减少底层代码。UI 使用原生控件，无 Web UI 依赖。

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

输出为 `dist/AIScreenShot-win-x64`。SDK 构建、协议测试、真实桌面联调的实际结果见 `PROJECT_STATUS.md`，不能将构建成功视为两端发送成功。

运行诊断（可选）：`ScreenShot.App.exe --smoke-test --browser Chrome`。生成的 `diagnostics` 仅用于本机测试；界面图片由 WinUI 控件渲染，不保存窗口后方的桌面。真实发送测试参数 `--test-codex-workspace AI_ScreenShot` 和 `--test-chatgpt` 仅与 `--smoke-test` 一起使用，发送的是生成的渐变测试图。使用之前关闭已有实例；默认正常启动不执行发送测试。

本机程序已启动时，可以运行 `node tests/native-host-smoke.cjs` 验证发布包 Host 的二进制帧和 Named Pipe 握手。检查已部署的 Chrome Host 时运行 `node tests/native-host-smoke.cjs '<注册脚本输出的HostPath>' Chrome <实际扩展ID>`。检查前暂时断开同一浏览器的扩展连接，避免重复握手。该检查不代表浏览器官网发送已通过。

框选专项诊断：运行 `ScreenShot.App.exe --overlay-test` 检查生成的测试图在真实 GDI 缓冲区中的像素对齐、裁剪、反向选区、选区边框、资源释放、增量/完整绘制一致性、失效区覆盖与重复窗口生命周期，并输出 4K 生成画面合成基准，结果写入 `diagnostics/overlay-test.txt`。此诊断使用独立 Mutex，可与正常实例并行，不连接浏览器桥接。运行 `ScreenShot.App.exe --toolbar-test` 检查原生工具栏浅色/深色渲染、布局和会话退出，结果写入 `diagnostics/toolbar-test.txt`；仅保存生成的工具栏界面，不抓取或记录桌面，不执行发送。工具栏诊断也使用独立 Mutex，不连接浏览器桥接，可与正常实例并行，并验证 Codex 成功退出截图及失败/未知保留截图。这两项诊断不代替真实 Codex 发送、实际鼠标拖动和混合 DPI 多屏验收，合成基准不代表桌面帧率。

## 范围与隐私

V1 支持静态矩形截图、复制和 PNG 保存；不支持录屏、滚动截图、OCR、标注、HDR 色彩还原或受保护内容。截图与提示词不写日志。设置保存在 `%LOCALAPPDATA%/AIScreenShot/settings.json`。浏览器桥接使用当前用户 Named Pipe 和扩展 origin 白名单。

参考 [ShareX](https://github.com/ShareX/ShareX) 的交互流程，未复制 GPL 源码；浏览器上传参考 [ChromeGPT](https://github.com/blixt/ChromeGPT) 并独立适配。DOM 与 Codex 可访问性结构可能随版本变化，失败时需更新适配器。
