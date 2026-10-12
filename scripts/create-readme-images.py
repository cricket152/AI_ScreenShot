"""Compose README artwork from existing screenshots; never change the originals.

Requires Python 3.10+ and Pillow. Run from any directory on Windows:
    python scripts/create-readme-images.py
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont


ROOT = Path(__file__).resolve().parents[1]
IMAGES = ROOT / "docs" / "images"
INK = "#25382F"
MUTED = "#617269"
ACCENT = "#526D42"
FONT_ROOT = Path("C:/Windows/Fonts")
SOURCES = ("use.png", "desktop.png", "setup.png", "setup2.png", "setup3.png", "setup4.png", "setup5.png")


def font(size: int, bold: bool = False, latin: bool = False) -> ImageFont.FreeTypeFont:
    filename = ("seguisb.ttf" if bold else "segoeui.ttf") if latin else ("msyhbd.ttc" if bold else "msyh.ttc")
    return ImageFont.truetype(str(FONT_ROOT / filename), size)


def text(canvas: Image.Image, xy: tuple[int, int], value: str, size: int = 26,
         bold: bool = False, color: str = INK, latin: bool = False) -> None:
    draw = ImageDraw.Draw(canvas)
    for index, line in enumerate(value.splitlines()):
        draw.text((xy[0], xy[1] + index * (size + 16)), line,
                  font=font(size, bold, latin), fill=color, anchor="lt")


def background(size: tuple[int, int]) -> Image.Image:
    width, height = size
    canvas = Image.new("RGB", size, "#F8F9F4").convert("RGBA")
    # Low-frequency, soft color fields leave the text and screenshots in focus.
    for bounds, color, opacity in (
        ((int(width * .49), -280, width + 360, height + 90), "#DFECD3", 180),
        ((int(width * .12), height - 190, int(width * .82), height + 400), "#E0EFEB", 190),
        ((-350, -320, 600, 360), "#F5EDD5", 95),
    ):
        mask = Image.new("L", size)
        ImageDraw.Draw(mask).ellipse(bounds, fill=opacity)
        field = Image.new("RGBA", size, color)
        field.putalpha(mask.filter(ImageFilter.GaussianBlur(135)))
        canvas = Image.alpha_composite(canvas, field)
    return canvas


def rounded(image: Image.Image, radius: int) -> Image.Image:
    result = image.convert("RGBA")
    mask = Image.new("L", result.size)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, result.width - 1, result.height - 1), radius, fill=255)
    result.putalpha(mask)
    return result


def place(canvas: Image.Image, image: Image.Image, xy: tuple[int, int], width: int,
          radius: int = 14, shadow: bool = True) -> None:
    height = round(image.height * width / image.width)
    image = rounded(image.resize((width, height), Image.Resampling.LANCZOS), radius)
    if shadow:
        layer = Image.new("RGBA", canvas.size)
        layer.paste(Image.new("RGBA", image.size, (35, 58, 41, 35)), (xy[0], xy[1] + 14), image.getchannel("A"))
        canvas.alpha_composite(layer.filter(ImageFilter.GaussianBlur(20)))
    canvas.alpha_composite(image, xy)


def pill(canvas: Image.Image, xy: tuple[int, int], value: str, size: int = 23) -> int:
    face = font(size)
    width = round(face.getlength(value)) + 40
    ImageDraw.Draw(canvas).rounded_rectangle((xy[0], xy[1], xy[0] + width, xy[1] + 48),
                                            radius=24, fill=(255, 255, 255, 190), outline="#D9E2D1", width=1)
    text(canvas, (xy[0] + 20, xy[1] + 12), value, size, color=ACCENT)
    return width


def footer(canvas: Image.Image, topic: str) -> None:
    y = canvas.height - 50
    text(canvas, (80, y), "AI ScreenShot", 20, bold=True, latin=True, color=ACCENT)
    text(canvas, (canvas.width - 290, y), topic, 18, color=MUTED)


def heading(canvas: Image.Image, number: str, title: str, description: str) -> None:
    text(canvas, (80, 65), f"AI SCREENSHOT   /   {number}", 20, bold=True, latin=True, color=ACCENT)
    text(canvas, (80, 112), title, 54, bold=True)
    text(canvas, (80, 191), description, 25, color=MUTED)


def panel_parts() -> tuple[Image.Image, Image.Image]:
    with Image.open(IMAGES / "use.png") as source:
        toolbar = source.crop((855, 129, 1335, 185))
        panel = source.crop((855, 197, 1335, 800))
    return toolbar, panel


def hero() -> Image.Image:
    canvas = background((1800, 900))
    with Image.open(ROOT / "src/ScreenShot.App/Assets/AppIcon.png") as icon:
        canvas.alpha_composite(icon.convert("RGBA").resize((72, 72), Image.Resampling.LANCZOS), (112, 142))
    text(canvas, (204, 165), "为 Windows 桌面而生", 25, color=MUTED)
    text(canvas, (112, 269), "AI ScreenShot", 86, bold=True, latin=True)
    text(canvas, (116, 398), "截屏即提问，", 59, bold=True)
    text(canvas, (116, 480), "让 AI 跟上你的思路。", 59, bold=True)
    text(canvas, (118, 591), "从屏幕上的一小块，开始一段完整对话。", 29, color=MUTED)
    x = 118
    for label in ("截图问答", "多对话", "原生 WinUI 3"):
        x += pill(canvas, (x, 665), label) + 14
    text(canvas, (118, 805), "Codex  /  兼容 API  /  本地会话历史", 23, color=MUTED)
    toolbar, panel = panel_parts()
    # Real screenshot components are only cropped, scaled and rounded at their existing edges.
    place(canvas, toolbar, (1090, 112), 510, radius=30)
    place(canvas, panel, (1090, 187), 510, radius=18)
    with Image.open(IMAGES / "use.png") as source:
        crop = source.crop((887, 347, 1086, 459))
    tile = Image.new("RGBA", (288, 232), "#FCFDF9")
    text(tile, (22, 22), "从这张截图开始", 21, bold=True, color=ACCENT)
    tile.alpha_composite(crop.convert("RGBA").resize((244, 137), Image.Resampling.LANCZOS), (22, 67))
    place(canvas, tile, (825, 276), 240, radius=18)
    text(canvas, (1090, 850), "真实界面 · 截图中的主题色可自定义", 18, color=MUTED)
    return canvas


def screenshot_ai() -> Image.Image:
    canvas = background((1600, 900))
    heading(canvas, "01", "看见问题，截图就能问。", "框选屏幕，把画面与问题一起交给 Codex 或兼容 API。")
    for y, number, title, body in (
        (323, "01", "截取你关注的部分", "Ctrl+Alt+S 框选屏幕\n也可复制图片或另存 PNG"),
        (477, "02", "把问题一起带过去", "选择 Codex 或 Agent\n输入提示词，Ctrl+Enter 发送"),
        (631, "03", "在桌面继续读与问", "原生面板展示流式回复\n支持 Markdown 与数学公式"),
    ):
        ImageDraw.Draw(canvas).rounded_rectangle((80, y, 126, y + 40), radius=12, fill="#E1EBCF")
        text(canvas, (89, y + 8), number, 18, bold=True, latin=True, color=ACCENT)
        text(canvas, (146, y + 1), title, 29, bold=True)
        text(canvas, (146, y + 48), body, 23, color=MUTED)
    with Image.open(IMAGES / "use.png") as source:
        # Retain the actual question, response and toolbar in their original spatial relationship.
        source = source.crop((55, 117, 1340, 809))
    place(canvas, source, (565, 290), 952, radius=20)
    text(canvas, (565, 819), "截图示例：数学题的图片输入与 Agent 回复", 20, color=MUTED)
    footer(canvas, "截图 · 提问 · 继续对话")
    return canvas


def multi_conversation() -> Image.Image:
    canvas = background((1600, 900))
    heading(canvas, "02", "一个面板，容纳多条思路。", "切换标签继续对话，工作目录、草稿和生成任务各自保留。")
    # Crop away the unrelated desktop widgets and wallpaper outside the application.
    with Image.open(IMAGES / "desktop.png") as source:
        empty = source.crop((60, 309, 580, 913))
    _, active = panel_parts()
    text(canvas, (112, 264), "新对话 · 随时开始", 23, bold=True, color=ACCENT)
    place(canvas, empty, (112, 306), 408, radius=14)
    text(canvas, (567, 264), "已有对话 · 接着思考", 23, bold=True, color=ACCENT)
    place(canvas, active, (567, 306), 408, radius=14)
    for y, title, description in (
        (344, "独立上下文", "Codex 与 Agent 各自延续\n当前标签的对话记录。"),
        (506, "后台继续生成", "不同标签可同时生成回复；\n停止只作用于当前标签。"),
        (668, "历史留在本机", "关闭标签保留历史；\n重启恢复已保存的对话。"),
    ):
        ImageDraw.Draw(canvas).line((1050, y + 3, 1050, y + 95), fill="#A9C18E", width=3)
        text(canvas, (1076, y), title, 29, bold=True)
        text(canvas, (1076, y + 50), description, 23, color=MUTED)
    footer(canvas, "多标签 · 独立任务 · 本地历史")
    return canvas


def customization() -> Image.Image:
    canvas = background((1600, 900))
    heading(canvas, "03", "让桌面工具，更像你的桌面。", "明暗主题、强调色与原生动效，都由你决定。")
    for x, name, label in ((80, "setup2.png", "外观与材质"), (830, "setup3.png", "动效设置")):
        text(canvas, (x, 263), label, 24, bold=True, color=ACCENT)
        with Image.open(IMAGES / name) as source:
            place(canvas, source, (x, 302), 690, radius=14)
    footer(canvas, "Windows 主题色 / 自定义颜色")
    return canvas


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=IMAGES, help="Image output root (defaults to docs/images)")
    args = parser.parse_args()
    codex_source = IMAGES / "setup4.png"
    if not codex_source.is_file():
        codex_source = IMAGES / "sanitized/setup-codex.png"
    source_paths = [IMAGES / name for name in SOURCES if name != "setup4.png"] + [codex_source]
    missing = [str(path.relative_to(IMAGES)) for path in source_paths if not path.is_file()]
    if missing:
        parser.error("Missing original screenshots: " + ", ".join(missing))
    for filename in ("msyh.ttc", "msyhbd.ttc", "segoeui.ttf", "seguisb.ttf"):
        if not (FONT_ROOT / filename).is_file():
            parser.error(f"Missing Windows font: {FONT_ROOT / filename}")
    hashes = {path: hashlib.sha256(path.read_bytes()).hexdigest() for path in source_paths}
    products = {
        "brand/readme-hero.png": hero(),
        "features/screenshot-ai.png": screenshot_ai(),
        "features/multi-conversation.png": multi_conversation(),
        "features/customization.png": customization(),
    }
    # Privacy exception: obscure only the local folder path, leaving the real settings UI intact.
    with Image.open(codex_source) as source:
        safe = source.convert("RGB")
    draw = ImageDraw.Draw(safe)
    draw.rounded_rectangle((234, 189, 479, 208), radius=3, fill="#EEF0EC")
    draw.text((238, 191), "[本机工作目录已隐藏]", font=font(12), fill=MUTED, anchor="lt")
    products["sanitized/setup-codex.png"] = safe
    results = []
    for relative, image in products.items():
        path = args.output / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        image.convert("RGB").save(path, optimize=True)
        results.append({"path": relative, "size": list(image.size), "bytes": path.stat().st_size})
    if hashes != {path: hashlib.sha256(path.read_bytes()).hexdigest() for path in source_paths}:
        raise RuntimeError("Original screenshots changed during generation")
    print(json.dumps({"originals_unchanged": True, "generated": results}, ensure_ascii=True, indent=2))


if __name__ == "__main__":
    main()
