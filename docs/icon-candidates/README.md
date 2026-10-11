# AI ScreenShot 图标

状态：2026-10-06 用户明确要求将此图标替换为程序图标，已采用。程序 ApplicationIcon、WinUI 窗口/任务栏和托盘使用原始设计；2026-10-11 悬浮入口保留同一取景框与 AI 星芒符号，改用可随主题色更新的原生矢量形状。

候选文件：[ai-screenshot-fluent-v1.png](ai-screenshot-fluent-v1.png)，1254 × 1254，RGBA，外部透明。使用内置 imagegen 生成，未复制 DeskBox 或 AI 服务商标识。原始生成图另保留在 Codex generated_images 目录。

设计方向：四角截图取景框 + 中央 AI 星芒；蓝青至靛蓝渐变、圆角方形底板、轻微 Fluent 材质层次。使用粗线条及高对比主形，避免文字和细碎电路装饰。`scripts/create-app-icon.ps1` 使用 Windows System.Drawing 从已确认原图缩放/转换，保留透明度；`src/ScreenShot.App/Assets/AppIcon.ico` 包含 16/20/24/32/40/48/64/96/128/256px 十种尺寸，`AppIcon.png` 为保留的 256px 原图资源。没有重新生成或改画原始 PNG/ICO。

悬浮入口的 `FloatingBallVisual` 根据悬浮球参考图独立绘制紧凑的 32×32 PathIcon：四个对称的圆润取景框边角与中央曲线四角星芒，固定纯白 `#FFFFFF`，占 48px 圆形主体约 67%。背景使用当前主题强调色派生三段轻微半透明渐变、左上柔和高光、0.65px 细轮廓及小范围带色外阴影；明亮颜色自动压暗，包含主体 alpha 与高光后的白色图形对比度至少 3:1。参考图不作为资源嵌入，没有白色底板或多余透明留白；此变体只用于悬浮球，不更换应用/任务栏/托盘图标。

ApplicationIcon 嵌入 EXE，运行窗口通过原生 AppWindow.SetIcon 指向发布资产；托盘通过 LoadImage 加载独立 HICON，退出使用 DestroyIcon 释放。PNG/ICO 均由项目复制到构建与发布输出；正常构建无需重新生成图标或安装额外依赖。

## 生成提示词

Create one original polished Windows 11 desktop application icon for an app named AI ScreenShot, a lightweight screenshot capture tool that sends images to AI assistants. Exactly one isolated icon, square 1:1 composition, transparent background outside the icon. Center a rounded squircle tile occupying about 82 percent of canvas, cool blue to indigo satin gradient with a subtle cyan highlight and restrained layered depth, precise soft rounded edges, minimal Windows 11 Fluent icon design language, clean contemporary professional utility. On the tile place one large thick warm-white rounded screenshot viewfinder made from four separated rounded L-shaped corner brackets, with a small crisp four-point AI sparkle centered inside. The sparkle has confident simple silhouette, no tiny details, optimized for recognizability at 16, 24 and 32 pixel sizes. Gentle internal lighting and subtle self-shadow only, no expansive drop shadow outside icon. Front-facing, essentially flat with modest dimensional material finish. Strong balanced spacing and legible high contrast silhouette. No text, letters, labels, mockup, comparison sheet, extra icons, camera lens, robots, brain, circuit board, ChatGPT/OpenAI/Codex logos, DeskBox marks or borrowed brand assets. This is an approval candidate for a future executable/taskbar icon; produce the icon artwork alone.
