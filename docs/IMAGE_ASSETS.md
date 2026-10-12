# README 宣传素材

[返回 README](../README.md)

## 设计与组织

参考 [DeskBox README](https://github.com/Tianyu199509/DeskBox) 的产品介绍、用户下载优先顺序、双列设置截图及技术/隐私信息组织。图片为 AI ScreenShot 原创排版，没有使用 DeskBox 品牌资源、截图或文案。

以暖白背景、低饱和鼠尾草绿、柔和渐变、圆角及轻量阴影呼应现有软件截图；产品名称沿用原拼写，品牌图标使用现有 AppIcon.png。绿色是本套素材配色，不代表软件固定主题。Windows 本机 Microsoft YaHei / Segoe UI 用于栅格文字，字体文件不随仓库分发。

Hero 左侧为名称、宣传语与功能关键词，右侧为真实工具栏和对话；功能图统一为编号、价值标题、简短说明和真实截图。没有生成虚构 UI，也没有改动原截图中的按钮、模型名称或功能状态。

## 素材对应关系

| 输出（相对 docs/images） | 尺寸 | 原始素材及处理 |
| --- | --- | --- |
| `brand/readme-hero.png` | 1800 × 900 | `use.png` 的工具栏、消息面板与用户截图裁切；现有 `src/ScreenShot.App/Assets/AppIcon.png` |
| `features/screenshot-ai.png` | 1600 × 900 | `use.png` 的题目、工具栏及真实 Agent 回复，保持原有空间关系 |
| `features/multi-conversation.png` | 1600 × 900 | `desktop.png` 的空白对话、`use.png` 的已有对话；是两个真实状态的对照，不是两个独立窗口或并行生成抓拍 |
| `features/customization.png` | 1600 × 900 | `setup2.png` 与 `setup3.png`，按等比缩放展示完整设置窗口 |
| `sanitized/setup-codex.png` | 941 × 706 | `setup4.png`，只遮盖本机工作目录并标记“本机工作目录已隐藏” |

README 另引用原始 `setup.png` 与 `setup5.png`。后者的 key 已由原生 PasswordBox 显示为圆点，没有明文密钥；模型和服务地址仍保留原样，它们只是配置示例，不代表图片能力认证或服务推荐。工作区显示名不是账号标识，保留真实状态。含本机目录的 `setup4.png` 留在本机并由 .gitignore 排除，公开仓库仅包含脱敏版；其他原始截图保留不变。

`desktop.png` 中的软件外桌面组件与大部分壁纸裁掉，避免将其他软件误认为本产品功能。面板内原有 Acrylic 背景仍按截图保留。`use.png` 的教育内容只作为真实截图操作示例，本任务没有重绘、补写 AI 回答或替换题目。

## 重新生成

Windows 上需要 Python 3.10+、Pillow 和已安装的 Microsoft YaHei / Segoe UI。不增加应用生产依赖；`scripts/verify.ps1` 与 `scripts/publish.ps1` 保持原样。

```powershell
python .\scripts\create-readme-images.py

# 可选：输出到独立目录进行比对，输入仍来自 docs/images 原图
python .\scripts\create-readme-images.py --output .\.tools\readme-preview
```

脚本根据自身位置定位仓库，默认仅写入上述五个衍生图片路径。Codex 配置图优先使用本机 `setup4.png`，不存在时使用公开的 `sanitized/setup-codex.png`，克隆仓库后可直接重生成。裁切坐标对应当前截图；更换截图后先调整坐标并逐张检查，不在 README 中继续使用不对应实际版本的界面。

输出为优化压缩的 RGB PNG，不量化颜色，不捆绑字体。四张宣传图当前每张小于 350 KB；可点开查看原尺寸。脚本生成前后校验七个实际输入文件的 SHA256，并输出尺寸及文件字节数。原图不被覆盖、重命名或写入。

## 发布前检查

- 核对原始截图权限和隐私内容；新增个人目录、账号或敏感输入时，只在衍生图中脱敏并更新这里的说明。
- 在 GitHub README 的宽度下检查标题、正文和设置页布局；缩放后的 UI 细节可通过点击图片放大查看。
- 确认每个图示功能仍与代码一致。素材不能代替真实 Codex / Agent 兼容性和多屏实机验收。
- 公开仓库与便携 Preview 下载链接已接入 README；主项目许可证仍待维护者确定，不添加猜测的许可徽章。
