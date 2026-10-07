# 原生 UI 设计

本项目参考 [DeskBox SettingsWindow](https://github.com/Tianyu199509/DeskBox/blob/main/src/DeskBox/Views/SettingsWindow.xaml)、[设置样式](https://github.com/Tianyu199509/DeskBox/blob/main/src/DeskBox/Styles/SettingsOverviewResources.xaml)和 [WidgetShell](https://github.com/Tianyu199509/DeskBox/blob/main/src/DeskBox/Controls/WidgetShell.xaml) 的分类导航、设置行和胶囊布局，独立实现 UI；未复制源码、图标或其他资源。Astryx 仅用于参考单一主操作、设置项说明及无障碍约定。

## Design Token

`src/ScreenShot.App/App.xaml` 的共享 `ResourceDictionary` 是尺寸与样式的来源。C# 创建的原生控件通过 `UiDesign` 应用相同资源。

| 分类 | 资源 | 约定 |
| --- | --- | --- |
| Spacing | `SS.Spacing.XS/S/M/L/XL/XXL` | 4 / 8 / 12 / 16 / 24 / 32 逻辑像素 |
| CornerRadius | `SS.Corner.Control/Surface/Capsule/Ball` | 8 / 12 / 28 / 24 |
| Typography | `SS.Typography.Caption/Body/Section/Title` | 12 / 14 / 18 / 28；Segoe UI Variable |
| Control | `SS.ControlHeight`、`SS.ToolbarButtonWidth` | 常规高度 36；工具按钮默认宽 64，窄面板等宽缩小 |
| Surface | `SS.WindowSurfaceStyle`、`SS.ToolbarSurfaceStyle`、`SS.MessageSurfaceStyle`、`SS.ComposerSurfaceStyle` | 独立材质背景、轻透工具条、稳重消息区、单层输入区 |
| Chat | `SS.UserMessageStyle`、`SS.AssistantMessageStyle` | 用户左、助手右；用填充和角色标题区分，无重复描边 |
| Motion | `SS.Motion.Fast/Normal`、`SS.Motion.HoverScale/PressedScale` | 120 / 180ms；1.04 / 0.96；面板过渡 CubicEase / EaseOut；遵循设置及系统动画策略 |
| Settings | `SS.Settings.*` | 统一窗口、导航、内容上限、右侧控件宽度与布局断点 |

颜色使用系统 `ThemeResource`：正文/说明分别使用 `TextFillColorPrimaryBrush` / `TextFillColorSecondaryBrush`，Surface 和描边使用系统 fill/stroke brushes。按钮保留 `DefaultButtonStyle` / `AccentButtonStyle` 的原生模板、Hover、Pressed、Disabled 和键盘焦点状态；图标使用系统 Segoe Fluent Icons。

## 应用主题与强调色

参考 DeskBox 的 [ThemeService](https://github.com/Tianyu199509/DeskBox/blob/main/src/DeskBox/Services/ThemeService.cs)、[AccentResourceScope](https://github.com/Tianyu199509/DeskBox/blob/main/src/DeskBox/Helpers/AccentResourceScope.cs) 和 [AppearanceSettingsSection](https://github.com/Tianyu199509/DeskBox/blob/main/src/DeskBox/Views/SettingsSections/AppearanceSettingsSection.xaml) 的统一窗口主题、窗口资源作用域与外观设置方式，独立实现 `AppThemeController`；未复制代码、调色公式或资源，也未引入控件库。

- `PanelAppearance` 保存 `Theme`（System / Light / Dark）、`AccentMode`（System / Custom）及 `CustomAccentColor`（不透明 `#RRGGBB`）。默认跟随系统，备用自定义色为 `#4F6BED`；旧配置自动使用默认值，非法枚举或颜色规范化。
- 设置、悬浮入口、面板、截图详情和既有诊断工具栏注册到同一控制器。通过根 `FrameworkElement.RequestedTheme` 应用明/暗；系统模式读取 `UISettings` 的前景色判断实际应用主题，监听 `ColorValuesChanged`，在 UI Dispatcher 上更新已打开窗口。材质继续由根 `ActualThemeChanged` 更新，标题栏同步 DWM 深浅配置；不改注册表，不激活窗口。
- 每个窗口根资源增加 Light / Dark 主题字典，复用可更新的 `SolidColorBrush`。强调色覆盖语义资源及原生按钮、导航选中指示、Slider、ToggleSwitch、CheckBox、进度和链接的对应资源；原生模板、Disabled 与焦点状态保留。系统模式使用 `UISettings` 提供的浅/深强调色阶及原生透明度，颜色变化实时同步。
- 自定义主按钮使用所选 RGB，按对比度选择黑/白前景，并生成 Hover / Pressed 颜色；强调文字针对正文主题背景调整可读性。HighContrast 字典不覆盖强调色，保留 WinUI 系统高对比度资源。正文和控件描边仍为中性 ThemeResource，两块面板外缘按边框设置使用强调色，材质通过混入比例调整色调。实现依据 [WinUI 主题资源](https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/xaml-theme-resources) 与 [Windows 主题监听](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-windows-themes)。
- 外观页使用两项原生 ComboBox，以及带色样/HEX 的按钮和原生 [ColorPicker](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/color-picker) Flyout，关闭 alpha 编辑；跟随 Windows 时禁用颜色编辑。沿用设置行及窄窗堆叠，实时预览同步所有已注册窗口，保存持久化，关闭撤销未保存草稿。

## 布局与材质

- 设置窗口使用原生 `NavigationView`，分类为常规与快捷键、外观与材质、Codex、ChatGPT、关于。各页编辑控件缓存于窗口中，切换分类保留未保存输入。每行只有一层卡片：标题和说明在左、控件在右；窄内容宽度时上下排列。修饰键使用原生 CheckBox 的两列布局。
- 设置窗口仍使用 Mica；悬浮宿主始终透明且没有底板，工具条和消息面板各放置一个原生 `SystemBackdropElement`，使用独立控制器和圆角。Acrylic 由 DWM 合成窗口后方当前可见的画面，随着后方窗口变化，不使用壁纸采样、桌面截图或轮询目标窗口。
- 悬浮入口 HWND 仍为 52 逻辑像素；48px 按钮留出微缩放空间，原有拖动、贴边与点击回调保留。系统禁用动画时不运行缩放过渡。
- 工具条继续横向排列，五个动作使用相同尺寸和图标规则；最小 420px 面板内等宽缩小。单一下拉菜单支持两栏显隐及工具条缩小/扩展：缩小后仅 Codex / ChatGPT 和菜单箭头，目标宽度约 202 逻辑像素；消息面板宽度保留。仅小工具条可见时 HWND 同步收窄，移动/高度缩放保留，扩展恢复原宽度。正常双栏仍共享宽度；原生窗口非客户区按实际 client rect 计算，避免双栏边缘错位。
- 截图预览、提示词和发送按钮放在同一输入 Surface。流式消息区区分尚未落地的程序滚动与用户交互；用户滚轮、拖动和导航键会使当前自动跟随失效，回到底部后恢复跟随。

材质实现遵循 [Windows App SDK 系统材质文档](https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops)，使用原生 `SystemBackdrop`，不引入 Web UI、CommunityToolkit 或新生产依赖。

## 可调材质

参考 DeskBox 的[控制器材质接口](https://github.com/Tianyu199509/DeskBox/blob/main/src/DeskBox/Services/WidgetMaterialSystemBackdrop.cs)及[外观设置分区](https://github.com/Tianyu199509/DeskBox/blob/main/src/DeskBox/Views/SettingsSections/AppearanceSettingsSection.xaml)的设计模式，独立实现 `AdjustableBackdrop` 和两组原生 ComboBox / Slider。未搬用其计算公式、源码或资源。

| 区域 | 默认材质 | 默认底色不透明度 |
| --- | --- | --- |
| 工具条 | Thin Desktop Acrylic | 42% |
| 消息面板 | Desktop Acrylic | 82% |

- 每个区域可选择 Acrylic、轻透 Acrylic 或透明色层，底色不透明度范围 0–100%。数字越小越通透；Acrylic 在 0% 时仍保留原生模糊与纹理，100% 时由底色覆盖。标准 Acrylic 额外保留少量明度填充，使正文更稳重；轻透 Acrylic 减少填充。滑块不修改 Window / 控件整体 `Opacity`，文字、截图与按钮保持清晰。色值从系统 ThemeResource 解析，切换主题重新更新控制器 tint / fallback。
- 透明色层不额外模糊；工具条/消息使用非交互的原生 Border 色层，直接叠加在当前后方窗口上。若要两个面板均无额外模糊，两组材质均选择透明色层。区域外部 backdrop link 不接受任意 Composition brush，因此不向其赋值自建色刷。
- 各控制器使用独立 `SystemBackdropConfiguration`，保持 `IsInputActive=true`，并同步系统主题与高对比度信息；材质效果不依赖窗口实际输入焦点，也不激活或置顶后方应用。悬浮入口同样保留失焦材质。系统禁用透明效果、远程会话或硬件不支持时仍遵循原生 fallback；透明色层也响应 Windows 高级效果开关。
- 修改实时预览已创建的面板；“预览面板”以原有 NoActivate 流程展开，“保存外观”独立保存外观，不依赖快捷键或 CLI 输入验证。“恢复默认”先恢复预览，保存后持久化；关闭设置恢复尚未保存的外观。仅在现有 settings.json 增加 `PanelAppearance`，旧设置自动获得默认值，非法枚举、空对象及越界数值规范化，不改变业务协议。
- 材质背景不参与命中测试；按钮、选择文本、单一显隐入口、拖动与边缘缩放保留原有行为。控制器与透明刷在 UI 线程释放，外部 element backdrop link 由断开回调在所属线程关闭。

## 透明宿主与过渡设置

悬浮窗口不再有公共底板、外框或对应缩放区域。`TransparentPanelBackdrop` 只为 HWND 提供零 alpha 合成背景，不绘制材质；透明 Grid 承载工具栏和消息面板，两个 `SystemBackdropElement` / Border 分别直接绘制圆角矩形。移除 `PanelWindowRegion`、SetWindowRgn / CreateRoundRectRgn / CombineRgn 和额外 RectangleGeometry 裁切。DWM 宿主描边和系统圆角关闭，面板圆角由原生控件自身绘制。

零 alpha 刷本身不足以消除 HWND 的黑色背景：宿主关闭非客户区 frame，并通过 [DwmEnableBlurBehindWindow](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmenableblurbehindwindow) 启用客户区 alpha 合成，传入 Region=null、Flags=ENABLE，没有形状或区域裁切；WM_ERASEBKGND 清空原生背景 alpha。Windows 8 以后此 API 不产生模糊，本项目的实际模糊仍只由两个 SystemBackdropElement 实现。响应 DWM composition 变化，释放 DC 并使用系统 stock brush；不添加 WinUIEx 等依赖。

WinUI 的后续窗口样式更新可能重新加入 WS_DLGFRAME / WS_EX_WINDOWEDGE，仅在构造时清理不足以消除外围白框。宿主 subclass 按 [WM_STYLECHANGING](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-stylechanging) 过滤 frame / edge 位，保留其余样式；按 [WM_NCCALCSIZE](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-nccalcsize) 使客户区占据整个窗口矩形。首次安装时重新计算 frame，后续显隐、缩放和主题变化保持无框。没有禁用 DWM non-client rendering policy，以免连带关闭客户区 alpha 合成。

`PanelGeometry` 只测量两块面板的物理边界，提供阴影位置和鼠标命中计算，不改变窗口绘制区域。`PanelInputTransparency` 对宿主和其子 HWND 的空白/间隙/圆角外返回 HTTRANSPARENT，控件仍走原 WinUI 输入流程；销毁或关闭时解除 subclass。窗口级缩放区取消，两栏自己的四边四角及顶部缩放、空白/标题拖动保留。

`PanelAppearance` 移除 Base 和 HideWindowBase；旧 JSON 的两项字段被忽略，保存不再输出。工具条、消息材质以及主题/颜色/边框/阴影/混色/动画与业务配置均保留。外观页只显示两组原生材质与不透明度编辑器，移除隐藏底板及底板材质设置，实时预览/保存/默认/关闭撤销不变。

`UiTransition` 使用原生 Storyboard / DoubleAnimation，以 180ms CubicEase / EaseOut 同时过渡高度、不透明度、工具条宽度和双栏间距。工具条固定左端及顶部向右扩展，完成后才恢复 Stretch；隐藏时两栏与阴影按当前实际边界同步变化。进入前扩充透明宿主，退出完成先隐藏 HWND 再恢复下次布局，快速切换从当前展示值重定向并取消旧 Completed。不存在公共底板的滞后收缩。

“界面过渡动画”与 UISettings.AnimationsEnabled 共同控制过渡，关闭即提交目标状态；截图/操作立即隐藏，恢复保持 NoActivate。悬浮球保留已有缩放及系统动画策略。
## 验证

`scripts/verify.ps1` 验证 App / Native Host 编译、核心回归及扩展测试。`ScreenShot.App.exe --floating-ui-test` 使用生成图和注入发送回调，检查双主题、420px 工具条、520px 设置窗口、五类页面、未保存输入、原生按钮状态、菜单、移动、缩放、流式跟随和防重发。材质诊断切换三种材质和 0/37/100% 底色，检查独立连接、主题 tint、失焦配置、前台保持及不透明内容；结果记录系统当前实际控制器状态。

过渡诊断实际检查透明 HWND 零 alpha、没有 native region 和额外面板 Clip，两块原生材质/描边及空白命中；采样向右扩展的固定左端/顶部和收起过程中阴影几何同步。设置检查仅两组材质编辑器，没有底板项；保留菜单/移动/缩放/流式/防重发与主题/窄窗回归。

实际 DWM 透明检查在面板后方放置自建的无激活 Win32 双色色板，主动尝试重新加入原生 frame / edge 样式，关闭阴影后检查四条 HWND 最外沿、留白、角外与间隙，共 208 个样本。浅色/深色和边框关闭 / 0.5 / 1 / 3px 下，另检查 16 组直边和 64 组圆角：共享材质/描边边界、紧邻的阴影与本体/边框像素。无模糊的实色测试层使间隙可明确判断；不把它当作 Acrylic 模糊效果验收。结果写入 panel-boundaries.txt。测试结束销毁 HWND/类和 DC，恢复外观与焦点；只采样自有色板覆盖范围，不保存屏幕或读取用户内容。这项检查不代替 HDR、分数 DPI 动画或多屏的视觉验收。

主题诊断通过实际设置控件切换明/暗/系统与系统/自定义强调色，读取原生按钮背景及前景、检查对比度及跨窗口同步、材质配置主题、系统色阶恢复和 HighContrast 字典未覆盖资源；打开真实 ColorPicker Flyout 并编辑颜色，验证实时预览。核心回归覆盖旧配置默认值、JSON 持久化及非法颜色/枚举边界。实际修改 Windows 设置和进入系统高对比度仍保留为桌面验收项。

## 程序图标

[图标说明](icon-candidates/README.md)记录 imagegen 原始提示词与已确认 PNG：截图取景框、AI 星芒、蓝青至靛蓝圆角底板和透明外部。用户已确认接入，生成十尺寸 ICO 用于 EXE、窗口/任务栏与托盘，256px PNG 用于悬浮入口。窗口通过 [AppWindow.SetIcon](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.appwindow.seticon) 设置，托盘独立持有并释放 HICON；未新增依赖。

## 面板边框

外观页增加“面板边框”ToggleSwitch 和“边框粗细”Slider：默认开启、1 逻辑像素，可选 0.5–3；关闭后禁用滑块并保留宽度，沿用实时预览、保存、默认及关闭撤销。工具栏和消息栏从窗口 ThemeDictionaries 取得共享语义刷 `SystemControlHighlightAccentBrush`，并在主题/强调色变化时同步使用所选原始强调色，自定义粉色即显示粉色边框，系统模式跟随 Windows；高对比度使用系统资源。

两处描边使用非交互 Border 叠加层，原内容留白、布局和动画尺寸保持稳定，不改变拖动/缩放区域。关闭 DWM 原生外框，避免重复的灰色细线或不受粗细控制的描边；保留两栏独立设置。输入框、气泡和设置卡片维持原生中性描边，正文保持清晰。原生诊断检查实际资源色、三种宽度、开关、保留宽度、窗口与内容几何不变及图标加载；实际托盘/Explorer 显示仍需桌面验收。

面板承载 Border 的布局 BorderThickness 固定为 0，材质元素、主题色层与叠加描边铺满同一圆角矩形，阴影也使用此边界。可见描边向面板内部绘制，材质铺在描边下方；关闭边框后由材质直接接触外围阴影。避免旧版 1px 透明布局边使材质内缩、阴影仍按外缘生成的空隙；边框粗细只改变叠加描边，不改变布局。设置卡片与输入区的原生边框保持原样。

## 外围阴影与材质混色

旧版隐藏底板的裁切方案已移除。当前两块面板直接绘制，透明宿主不绘制背景、边框或阴影；实际桌面合成与多屏仍需视觉验收，RenderTargetBitmap 不能作为 DWM 材质截图。

DeskBox 的 [WindowShadowSettingsContractTests](https://github.com/Tianyu199509/DeskBox/blob/main/tests/DeskBox.Tests/WindowShadowSettingsContractTests.cs) 显示其窗口阴影选项修改 Windows 全局阴影策略。本项目要求独立扩散/透明度，沿用独立且可调的阴影实现，因此独立实现 `PanelShadowWindow`：非激活、工具型、透明的原生 layered HWND，紧邻主面板后方，使用 [UpdateLayeredWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow) 呈现预乘 BGRA 阴影；不修改 Windows 设置，不采样屏幕。返回 HTTRANSPARENT，并使用 WS_EX_TRANSPARENT / NOACTIVATE，阴影不接收鼠标和焦点。

- 设置新增面板阴影开关、0–32 逻辑像素扩散、0–50% 不透明度，默认开启 / 12px / 16%。关闭、扩散 0 或不透明度 0 时隐藏阴影，数值保留；高对比度禁用阴影。
- `PanelShadowMask` 只计算圆角边界外围的柔和衰减，清除全部面板内部的 alpha，避免把透明玻璃染黑；两栏独立阴影取包络，不累加变浓。只遍历边缘带，内部使用连续清零；几何/配置相同则复用已上传位图，移动只同步位置。动画按当前区域及 alpha 同步；截图/操作前立即隐藏，窗口关闭销毁阴影 HWND，临时 GDI 位图/DC finally 释放。此次约 1.9MB 阴影缓冲的单次诊断，构建运行记录重绘 2.42ms（mask 0.96ms），发布包记录 24.80ms（mask 23.16ms）；时序存在波动，不作为稳定帧率或正式基准。
- 材质混色参考 DeskBox 的 [WidgetMaterialVisualCalculator](https://github.com/Tianyu199509/DeskBox/blob/main/src/DeskBox/Services/WidgetMaterialVisualCalculator.cs) 与 [WidgetMaterialSystemBackdrop](https://github.com/Tianyu199509/DeskBox/blob/main/src/DeskBox/Services/WidgetMaterialSystemBackdrop.cs) 的 tint / fallback 接入方式，独立实现中性主题色与原始强调色的 RGB 插值。设置提供 0–100%“材质主题色混入”，默认 18%；两块面板同步，0% 保持原中性色，100% 使用强调色。Acrylic 保留原控制器与底色不透明度，只改变 tint / fallback；透明色层使用相同混色。主题/系统强调色变化实时更新，高对比度使用系统颜色，文字/截图整体不透明度不变。建议使用较低混入比例保持正文可读。

配置仍在 `PanelAppearance` 中，兼容旧 settings.json，非法/非有限值分别规范化；沿用外观实时预览、独立保存、恢复默认及关闭撤销。回归包括持久化、阴影内区 alpha=0/衰减/扩散与透明度独立/跨栏不染黑，以及实际原生阴影 HWND、命中、前台、移动、操作隐藏/恢复和三材质双主题 0/35/100% tint。

生成预览不包含用户桌面或真实发送。`RenderTargetBitmap` 不捕获系统 backdrop；设置页导出时临时使用当前主题的实色 fallback，导出后恢复透明根。预览用于检查布局与主题，后方画面的像素合成、系统开关/高对比度/省电降级和多屏鼠标操作仍需桌面验收。
