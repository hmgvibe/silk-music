"""Draws the 絲音 app icon: an eighth note whose flag is a length of silk ribbon,
turning over once and ending in a swallowtail cut.

Writes the master PNG and the Windows .ico. Usage: python make_icon.py [preview.png]
With --android, writes the Android adaptive icon layers instead (see make_android).
"""
import math
import struct
import sys
from io import BytesIO
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
ICO_PATH = HERE.parents[1] / "src" / "Silk" / "silk.ico"
MASTER_PATH = HERE / "silk-1024.png"
ANDROID_RES = HERE.parents[1] / "android" / "app" / "src" / "main" / "res" / "mipmap-xxxhdpi"
# An adaptive icon layer is 108dp; launchers may mask all but the central 66dp circle.
ANDROID_LAYER = 432
ANDROID_MARK_HEIGHT = 0.44
ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
LARGEST_BITMAP_ENTRY = 48
LARGEST_SNAPPED_SIZE = 32

TILE_TOP = "#3A2550"
TILE_BOTTOM = "#21142F"
NOTE = "#FFF8EC"
RIBBON_FRONT_SHADE = "#EBD3C2"
RIBBON_BACK_TOP = "#E98AA6"
RIBBON_BACK_BOTTOM = "#F2B07F"

HEAD_CENTER = (0.375, 0.715)
HEAD_TILT = -28
STEM_TOP = 0.185
TWIST_AT = 0.52


def rgb(hex_color):
    h = hex_color.lstrip("#")
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], dtype=np.float32)


def detail_for(size):
    """Smaller icons get a larger mark, heavier strokes and a gentler twist so they stay readable."""
    if size <= 24:
        return dict(margin=0.0, zoom=1.12, head=(0.12, 0.084), stem=0.06, ribbon=0.058, narrow=0.34)
    if size <= LARGEST_SNAPPED_SIZE:
        return dict(margin=0.015, zoom=1.06, head=(0.115, 0.08), stem=0.05, ribbon=0.052, narrow=0.28)
    if size <= 64:
        return dict(margin=0.03, zoom=1.0, head=(0.11, 0.076), stem=0.04, ribbon=0.045, narrow=0.2)
    return dict(margin=0.04, zoom=1.0, head=(0.11, 0.076), stem=0.036, ribbon=0.042, narrow=0.16)


class Canvas:
    def __init__(self, size, zoom):
        self.size = size
        self.zoom = zoom
        self.scale = max(8, 4096 // size)
        self.full = size * self.scale

    def blank(self):
        image = Image.new("L", (self.full, self.full), 0)
        return image, ImageDraw.Draw(image)

    def place(self, x, y):
        """Mark coordinates to canvas fractions, enlarged about the centre for small sizes."""
        return (x - 0.5) * self.zoom + 0.5, (y - 0.5) * self.zoom + 0.5

    def points(self, values, placed=False):
        out = []
        for x, y in values:
            if not placed:
                x, y = self.place(x, y)
            out.append((x * self.full, y * self.full))
        return out

    def finish(self, image):
        return np.asarray(image.reduce(self.scale), dtype=np.float32) / 255


def squircle(margin, exponent=5.0, steps=720):
    half = 0.5 - margin
    points = []
    for i in range(steps):
        a = 2 * math.pi * i / steps
        c, s = math.cos(a), math.sin(a)
        points.append((
            0.5 + half * math.copysign(abs(c) ** (2 / exponent), c),
            0.5 + half * math.copysign(abs(s) ** (2 / exponent), s),
        ))
    return points


def note_head(rx, ry):
    cx, cy = HEAD_CENTER
    rot = math.radians(HEAD_TILT)
    points = []
    for i in range(240):
        a = 2 * math.pi * i / 240
        x, y = rx * math.cos(a), ry * math.sin(a)
        points.append((cx + x * math.cos(rot) - y * math.sin(rot), cy + x * math.sin(rot) + y * math.cos(rot)))
    return points


def bezier(p0, p1, p2, p3):
    def at(t):
        m = 1 - t
        return (m**3 * p0[0] + 3 * m * m * t * p1[0] + 3 * m * t * t * p2[0] + t**3 * p3[0],
                m**3 * p0[1] + 3 * m * m * t * p1[1] + 3 * m * t * t * p2[1] + t**3 * p3[1])
    return at


def twist_width(half, narrow, span=0.26):
    """A ribbon turning over narrows gradually, like fabric seen edge-on."""
    def at(t):
        k = min(1, abs(t - TWIST_AT) / span)
        return half * (narrow + (1 - narrow) * math.sin(k * math.pi / 2) ** 1.5)
    return at


def draw_ribbon(canvas, curve, width, steps=480):
    """Returns coverage for the ribbon's front face (before the twist) and its reverse."""
    front, draw_front = canvas.blank()
    back, draw_back = canvas.blank()
    points = [curve(i / steps) for i in range(steps + 1)]
    for i in range(steps):
        edge = []
        for j in (i, i + 1):
            a, b = points[max(j - 1, 0)], points[min(j + 1, steps)]
            dx, dy = b[0] - a[0], b[1] - a[1]
            length = math.hypot(dx, dy) or 1
            h = width(j / steps)
            edge.append((points[j], -dy / length * h, dx / length * h))
        (p0, x0, y0), (p1, x1, y1) = edge
        quad = [(p0[0] + x0, p0[1] + y0), (p1[0] + x1, p1[1] + y1), (p1[0] - x1, p1[1] - y1), (p0[0] - x0, p0[1] - y0)]
        target = draw_front if (i + 0.5) / steps < TWIST_AT else draw_back
        target.polygon(canvas.points(quad, placed=True), fill=255)

    # Swallowtail: a V cut into the free end, so it reads as cut silk rather than a tail.
    end, before = curve(1.0), curve(0.985)
    dx, dy = end[0] - before[0], end[1] - before[1]
    length = math.hypot(dx, dy)
    tx, ty, nx, ny = dx / length, dy / length, -dy / length, dx / length
    half = width(1.0)
    reach = half * 1.6
    notch = [
        (end[0] + nx * reach + tx * 0.02, end[1] + ny * reach + ty * 0.02),
        (end[0] - tx * half * 1.25, end[1] - ty * half * 1.25),
        (end[0] - nx * reach + tx * 0.02, end[1] - ny * reach + ty * 0.02),
    ]
    for target in (draw_front, draw_back):
        target.polygon(canvas.points(notch, placed=True), fill=0)
    return canvas.finish(front), canvas.finish(back)


def over(base, color, alpha):
    a = alpha[..., None]
    return base * (1 - a) + color * a


def render(size, layers=False):
    """The finished icon; with layers=True, the tile's gradient (full bleed) and the mark alone, for Android."""
    d = detail_for(size)
    canvas = Canvas(size, d["zoom"])
    axis = (np.arange(size, dtype=np.float32) + 0.5) / size
    u, v = np.meshgrid(axis, axis)

    tile_mask, draw = canvas.blank()
    draw.polygon([(x * canvas.full, y * canvas.full) for x, y in squircle(d["margin"])], fill=255)
    tile = canvas.finish(tile_mask)
    k = np.clip(u * 0.35 + v * 0.8 - 0.1, 0, 1)[..., None]
    color = rgb(TILE_TOP) * (1 - k) + rgb(TILE_BOTTOM) * k

    # Note head and stem, in placed (canvas) coordinates so small sizes can snap to pixels.
    head = note_head(*d["head"])
    head_placed = [canvas.place(x, y) for x, y in head]
    stem = d["stem"] * canvas.zoom
    stem_right = max(x for x, _ in head_placed) - 0.004 * canvas.zoom
    top = canvas.place(0, STEM_TOP)[1]
    stem_bottom = canvas.place(0, HEAD_CENTER[1] - 0.01)[1]
    if size <= LARGEST_SNAPPED_SIZE:
        pixels = max(1, round(stem * size))
        stem_right = round(stem_right * size) / size
        stem = pixels / size
        top = round(top * size) / size
    stem_left = stem_right - stem

    note, draw = canvas.blank()
    draw.polygon(canvas.points(head_placed, placed=True), fill=255)
    draw.rectangle([stem_left * canvas.full, top * canvas.full, stem_right * canvas.full - 1, stem_bottom * canvas.full], fill=255)
    note = canvas.finish(note)
    # The mark alone, premultiplied, for the Android foreground layer.
    mark = np.zeros((size, size, 3), np.float32)
    mark_alpha = np.zeros((size, size), np.float32)

    def paint(base, paint_color, alpha):
        nonlocal mark, mark_alpha
        a = alpha[..., None]
        mark = mark * (1 - a) + paint_color * a
        mark_alpha = mark_alpha * (1 - alpha) + alpha
        return over(base, paint_color, alpha)

    background = color
    color = paint(color, rgb(NOTE), note)

    half = d["ribbon"] * canvas.zoom
    curve = bezier(
        (stem_left, top + half),
        (stem_right + 0.2 * canvas.zoom, top + half + 0.01 * canvas.zoom),
        (stem_right + 0.25 * canvas.zoom, canvas.place(0, 0.34)[1]),
        (stem_right + 0.13 * canvas.zoom, canvas.place(0, 0.53)[1]),
    )
    front, back = draw_ribbon(canvas, curve, twist_width(half, d["narrow"]))
    back_shade = np.clip((v - 0.35) * 4, 0, 1)[..., None]
    color = paint(color, rgb(RIBBON_BACK_TOP) * (1 - back_shade) + rgb(RIBBON_BACK_BOTTOM) * back_shade, back)
    front_shade = np.clip((u - 0.5) * 3, 0, 1)[..., None]
    color = paint(color, rgb(NOTE) * (1 - front_shade) + rgb(RIBBON_FRONT_SHADE) * front_shade, front)
    if layers:
        straight = mark / np.maximum(mark_alpha, 1e-6)[..., None]
        mark_image = Image.fromarray(np.clip(np.dstack([straight, mark_alpha * 255]) + 0.5, 0, 255).astype(np.uint8), "RGBA")
        return mark_image

    result = np.dstack([color, tile * 255])
    return Image.fromarray(np.clip(result + 0.5, 0, 255).astype(np.uint8), "RGBA")


def bitmap_entry(image):
    """Classic 32-bit DIB entry, which every Windows icon API can read."""
    size = image.width
    pixels = np.asarray(image, dtype=np.uint8)[::-1]
    bgra = pixels[..., [2, 1, 0, 3]].tobytes()
    row = ((size + 31) // 32) * 4
    mask = bytearray()
    for line in pixels[..., 3]:
        bits = np.packbits(line == 0).tobytes()
        mask += bits + bytes(row - len(bits))
    header = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0, len(bgra) + len(mask), 0, 0, 0, 0)
    return header + bgra + bytes(mask)


def png_entry(image):
    buffer = BytesIO()
    image.save(buffer, "PNG", optimize=True)
    return buffer.getvalue()


def write_ico(path, images):
    entries = [bitmap_entry(i) if i.width <= LARGEST_BITMAP_ENTRY else png_entry(i) for i in images]
    offset = 6 + 16 * len(entries)
    output = bytearray(struct.pack("<HHH", 0, 1, len(entries)))
    for image, data in zip(images, entries):
        dimension = 0 if image.width >= 256 else image.width
        output += struct.pack("<BBBBHHII", dimension, dimension, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    for data in entries:
        output += data
    path.write_bytes(output)


def preview(path, images):
    by_size = {i.width: i for i in images}
    sheet = Image.new("RGB", (1200, 420), (236, 238, 240))
    ImageDraw.Draw(sheet).rectangle((600, 0, 1200, 420), fill=(24, 26, 30))
    for left in (0, 600):
        big = by_size[1024].resize((240, 240), Image.LANCZOS)
        sheet.paste(big, (left + 24, 24), big)
        x = left + 284
        for size in (128, 96):
            sheet.paste(by_size[size], (x, 24), by_size[size])
            x += size + 14
        x = left + 284
        for size in (64, 48, 40, 32, 24, 20, 16):
            sheet.paste(by_size[size], (x, 176), by_size[size])
            x += size + 6
        x = left + 24
        for size in (32, 24, 20, 16):
            zoom = 3 if size == 32 else 5
            enlarged = by_size[size].resize((size * zoom, size * zoom), Image.NEAREST)
            sheet.paste(enlarged, (x, 284), enlarged)
            x += size * zoom + 16
    sheet.save(path)


def make_android():
    """Adaptive icon layers: the tile's gradient fills the background layer edge to edge, and the mark sits
    alone on the foreground, sized to stay inside the safe zone whatever shape the launcher cuts."""
    size = ANDROID_LAYER
    # The gradient as the tile has it, laid out over the visible 72dp of the 108dp layer.
    axis = ((np.arange(size, dtype=np.float32) + 0.5) / size - 1 / 6) * 1.5
    u, v = np.meshgrid(axis, axis)
    k = np.clip(u * 0.35 + v * 0.8 - 0.1, 0, 1)[..., None]
    gradient = rgb(TILE_TOP) * (1 - k) + rgb(TILE_BOTTOM) * k
    Image.fromarray(np.clip(gradient + 0.5, 0, 255).astype(np.uint8), "RGB").save(ANDROID_RES / "ic_launcher_background.png", optimize=True)

    mark = render(1024, layers=True)
    box = mark.getchannel("A").point(lambda a: 255 if a > 8 else 0).getbbox()
    mark = mark.crop(box)
    scale = ANDROID_MARK_HEIGHT * size / mark.height
    mark = mark.resize((round(mark.width * scale), round(mark.height * scale)), Image.LANCZOS)
    foreground = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    foreground.paste(mark, ((size - mark.width) // 2, (size - mark.height) // 2), mark)
    foreground.save(ANDROID_RES / "ic_launcher_foreground.png", optimize=True)

    # Themed icons: the same mark in one colour, which the launcher tints.
    white = Image.new("RGBA", foreground.size, (255, 255, 255, 0))
    white.putalpha(foreground.getchannel("A"))
    white.save(ANDROID_RES / "ic_launcher_monochrome.png", optimize=True)


def main():
    if "--android" in sys.argv:
        make_android()
        return
    images = [render(size) for size in ICO_SIZES]
    master = render(1024)
    master.save(MASTER_PATH, optimize=True)
    write_ico(ICO_PATH, images)
    if len(sys.argv) > 1:
        preview(sys.argv[1], images + [master])


if __name__ == "__main__":
    main()
