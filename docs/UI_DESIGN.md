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
- 自定义主按钮使用所选 RGB，按对比度选择黑/白前景，并生成 Hover / Pressed 颜色；强调文字针对正文主题背景调整可读性。HighContrast 字典不覆盖强调色，保留 WinUI 系统高对比度资源。底板、正文和控件描边仍为中性 ThemeResource，三块面板外缘按边框设置使用强调色，不将整个窗口染成强调色。实现依据 [WinUI 主题资源](https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/xaml-theme-resources) 与 [Windows 主题监听](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-windows-themes)。
- 外观页使用两项原生 ComboBox，以及带色样/HEX 的按钮和原生 [ColorPicker](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/color-picker) Flyout，关闭 alpha 编辑；跟随 Windows 时禁用颜色编辑。沿用设置行及窄窗堆叠，实时预览同步所有已注册窗口，保存持久化，关闭撤销未保存草稿。

## 布局与材质

- 设置窗口使用原生 `NavigationView`，分类为常规与快捷键、外观与材质、Codex、ChatGPT、关于。各页编辑控件缓存于窗口中，切换分类保留未保存输入。每行只有一层卡片：标题和说明在左、控件在右；窄内容宽度时上下排列。修饰键使用原生 CheckBox 的两列布局。
- 设置窗口仍使用 Mica；悬浮窗口底板改用可调 Desktop Acrylic，工具条和消息面板各放置一个原生 `SystemBackdropElement`，使用独立控制器和圆角。Acrylic 由 DWM 合成窗口后方当前可见的画面，随着后方窗口变化，不使用壁纸采样、桌面截图或轮询目标窗口。
- 悬浮入口 HWND 仍为 52 逻辑像素；48px 按钮留出微缩放空间，原有拖动、贴边与点击回调保留。系统禁用动画时不运行缩放过渡。
- 工具条继续横向排列，五个动作使用相同尺寸和图标规则；最小 420px 面板内等宽缩小。单一下拉菜单支持两栏显隐及工具条缩小/扩展：缩小后仅 Codex / ChatGPT 和菜单箭头，目标宽度约 202 逻辑像素；消息面板宽度保留。仅小工具条可见时 HWND 同步收窄，移动/高度缩放保留，扩展恢复原宽度。正常双栏仍共享宽度；原生窗口非客户区按实际 client rect 计算，避免双栏边缘错位。
- 截图预览、提示词和发送按钮放在同一输入 Surface。流式消息区区分尚未落地的程序滚动与用户交互；用户滚轮、拖动和导航键会使当前自动跟随失效，回到底部后恢复跟随。

材质实现遵循 [Windows App SDK 系统材质文档](https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops)，使用原生 `SystemBackdrop`，不引入 Web UI、CommunityToolkit 或新生产依赖。

## 可调材质

参考 DeskBox 的[控制器材质接口](https://github.com/Tianyu199509/DeskBox/blob/main/src/DeskBox/Services/WidgetMaterialSystemBackdrop.cs)及[外观设置分区](https://github.com/Tianyu199509/DeskBox/blob/main/src/DeskBox/Views/SettingsSections/AppearanceSettingsSection.xaml)的设计模式，独立实现 `AdjustableBackdrop` 和三组原生 ComboBox / Slider。未搬用其计算公式、源码或资源。

| 区域 | 默认材质 | 默认底色不透明度 |
| --- | --- | --- |
| 窗口底板 | Thin Desktop Acrylic | 32% |
| 工具条 | Thin Desktop Acrylic | 42% |
| 消息面板 | Desktop Acrylic | 82% |

- 每个区域可选择 Acrylic、轻透 Acrylic 或透明色层，底色不透明度范围 0–100%。数字越小越通透；Acrylic 在 0% 时仍保留原生模糊与纹理，100% 时由底色覆盖。标准 Acrylic 额外保留少量明度填充，使正文更稳重；轻透 Acrylic 减少填充。滑块不修改 Window / 控件整体 `Opacity`，文字、截图与按钮保持清晰。色值从系统 ThemeResource 解析，切换主题重新更新控制器 tint / fallback。
- 透明色层不额外模糊；底板使用原生 CompositionColorBrush 的 alpha，工具条/消息使用非交互的原生 Border 色层，自然叠加在底板之上。若要整个面板完全无模糊，三个区域均选择透明色层。区域外部 backdrop link 不接受任意 Composition brush，因此不向其赋值自建色刷。
- 各控制器使用独立 `SystemBackdropConfiguration`，保持 `IsInputActive=true`，并同步系统主题与高对比度信息；材质效果不依赖窗口实际输入焦点，也不激活或置顶后方应用。悬浮入口同样保留失焦材质。系统禁用透明效果、远程会话或硬件不支持时仍遵循原生 fallback；透明色层也响应 Windows 高级效果开关。
- 修改实时预览已创建的面板；“预览面板”以原有 NoActivate 流程展开，“保存外观”独立保存外观，不依赖快捷键或 CLI 输入验证。“恢复默认”先恢复预览，保存后持久化；关闭设置恢复尚未保存的外观。仅在现有 settings.json 增加 `PanelAppearance`，旧设置自动获得默认值，非法枚举、空对象及越界数值规范化，不改变业务协议。
- 材质背景不参与命中测试；按钮、选择文本、单一显隐入口、拖动与边缘缩放保留原有行为。控制器与透明刷在 UI 线程释放，外部 element backdrop link 由断开回调在所属线程关闭。

## 底板与过渡设置

“隐藏窗口底板”将窗口 backdrop 切换为 alpha 为 0 的原生 CompositionColorBrush，关闭公共描边和 DWM 外框，并由 `PanelWindowRegion` 将 HWND 形状限制为工具条和消息栏两个圆角区域的并集。仅透明刷不能保证移除原生宿主的外围矩形；现在外侧留白、两栏间隙及圆角以外均不属于窗口绘制/输入区域，保留两栏独立的材质及描边。使用 [SetWindowRgn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowrgn)、CreateRoundRectRgn / CombineRgn，成功后区域所有权交给 Windows，临时区域在 finally 释放；根据真实 client origin 和 DPI 映射坐标，不使用固定屏幕坐标。隐藏状态不依赖系统透明效果开关；两栏自身仍遵循材质降级。设置页禁用底板材质编辑但保留原参数，取消隐藏即可恢复；默认关闭。该设置和动画开关沿用外观实时预览、独立保存及关闭撤销流程。

`UiTransition` 使用原生 Storyboard / DoubleAnimation，以 180ms CubicEase / EaseOut 同时过渡面板高度、不透明度、工具条宽度和双栏间距。工具条在整个尺寸过渡期间保持 Left 对齐，左端与顶部固定，只向右扩展；完成后才恢复正常 Stretch 布局，避免固定 Width 和 Stretch 同时使用导致先居中再回弹。内容按圆角 Surface 裁剪，额外动作渐隐/渐现，退场动作不接受点击。窗口进入前先扩充容纳区域，退出完成后再收窄 HWND；每次实际布局变化同步更新原生窗口形状，几何相同时不重复设置区域，不每帧调整桌面窗口尺寸。可见底板的圆角边界跟随当前面板边界收缩，整体收起同时将两栏高度与间距降至零、按同一进度收缩外侧留白，避免内容消失后矩形底板滞留到完成事件才跳变。完成收起时先隐藏 HWND 再恢复下次展开的布局。快速切换时读取当前展示值再重定向动画，取消旧 Completed 回调，完成后统一提交可见性与几何，关闭时释放动画事件及布局订阅。

“界面过渡动画”默认开启，与 `UISettings.AnimationsEnabled` 共同决定面板过渡；关闭任一开关会立即提交当前目标状态。该开关用于工具条缩小/扩展、两栏展开/隐藏和悬浮球展开面板。截图及其他既有操作前的隐藏始终同步执行，恢复可应用过渡，保持截图生命周期和 NoActivate 约定。悬浮球 Hover / Pressed 仍使用已有 120ms 过渡并遵循系统动画策略。

## 验证

`scripts/verify.ps1` 验证 App / Native Host 编译、核心回归及扩展测试。`ScreenShot.App.exe --floating-ui-test` 使用生成图和注入发送回调，检查双主题、420px 工具条、520px 设置窗口、五类页面、未保存输入、原生按钮状态、菜单、移动、缩放、流式跟随和防重发。材质诊断切换三种材质和 0/37/100% 底色，检查独立连接、主题 tint、失焦配置、前台保持及不透明内容；结果记录系统当前实际控制器状态。

过渡诊断除零 alpha 外，实际读取 HWND region，检查外围和两栏间隙不在区域内、圆角外不绘制、两栏中心保留；逐帧采样扩展的左端/顶部/窗口位置和宽度单调增长，并采样消息栏/全窗口收起过程中底板边界同步缩小。保留两栏独立材质、双主题小胶囊、工具栏两动作/五动作恢复及窄 HWND、快速反向切换、收起中重新展开、切换途中关闭动画、操作前立即隐藏与恢复、内容及前台保持。设置诊断检查底板编辑禁用、外观草稿和两项开关的主题/窄窗布局。

主题诊断通过实际设置控件切换明/暗/系统与系统/自定义强调色，读取原生按钮背景及前景、检查对比度及跨窗口同步、材质配置主题、系统色阶恢复和 HighContrast 字典未覆盖资源；打开真实 ColorPicker Flyout 并编辑颜色，验证实时预览。核心回归覆盖旧配置默认值、JSON 持久化及非法颜色/枚举边界。实际修改 Windows 设置和进入系统高对比度仍保留为桌面验收项。

## 程序图标

[图标说明](icon-candidates/README.md)记录 imagegen 原始提示词与已确认 PNG：截图取景框、AI 星芒、蓝青至靛蓝圆角底板和透明外部。用户已确认接入，生成十尺寸 ICO 用于 EXE、窗口/任务栏与托盘，256px PNG 用于悬浮入口。窗口通过 [AppWindow.SetIcon](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.appwindow.seticon) 设置，托盘独立持有并释放 HICON；未新增依赖。

## 面板边框

外观页增加“面板边框”ToggleSwitch 和“边框粗细”Slider：默认开启、1 逻辑像素，可选 0.5–3；关闭后禁用滑块并保留宽度，沿用实时预览、保存、默认及关闭撤销。底板、工具栏和消息栏从窗口 ThemeDictionaries 取得共享语义刷 `SystemControlHighlightAccentBrush`，并在主题/强调色变化时同步使用所选原始强调色，自定义粉色即显示粉色边框，系统模式跟随 Windows；高对比度使用系统资源。

三处描边使用非交互 Border 叠加层，原内容留白、布局和动画尺寸保持稳定，不改变拖动/缩放区域。关闭 DWM 原生外框，避免重复的灰色细线或不受粗细控制的描边；隐藏底板同时隐藏底板描边，保留子栏独立设置。输入框、气泡和设置卡片维持原生中性描边，正文保持清晰。原生诊断检查实际资源色、三种宽度、开关、保留宽度、窗口与内容几何不变及图标加载；实际托盘/Explorer 显示仍需桌面验收。

生成预览不包含用户桌面或真实发送。`RenderTargetBitmap` 不捕获系统 backdrop；设置页导出时临时使用当前主题的实色 fallback，导出后恢复透明根。预览用于检查布局与主题，后方画面的像素合成、系统开关/高对比度/省电降级和多屏鼠标操作仍需桌面验收。
