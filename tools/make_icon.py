#!/usr/bin/env python3
"""Generate GSyncIndicator/appicon.ico (multi-size) used for the exe and shortcuts.

Matches the tray "active" look: a green disc with a soft highlight and ring on a
dark rounded-square background. Re-run after changing the design:

    python3 tools/make_icon.py
"""
import os
from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(__file__), "..", "GSyncIndicator", "appicon.ico")
SIZES = [16, 24, 32, 48, 64, 128, 256]

BG_TOP = (33, 38, 45)      # dark slate
BG_BOT = (22, 26, 31)
GREEN = (46, 204, 64)
GREEN_HI = (150, 240, 160)
GREEN_LO = (24, 130, 44)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def render(size):
    # Supersample for smooth edges, then downscale.
    S = max(size * 4, 64)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # Rounded-square background with a subtle vertical gradient.
    radius = int(S * 0.22)
    bg = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    for y in range(S):
        bg.paste(lerp(BG_TOP, BG_BOT, y / S) + (255,), (0, y, S, y + 1))
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=radius, fill=255)
    img.paste(bg, (0, 0), mask)

    # Outer glow.
    cx = cy = S / 2
    r = S * 0.30
    glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    gd.ellipse([cx - r * 1.35, cy - r * 1.35, cx + r * 1.35, cy + r * 1.35],
               fill=GREEN + (70,))
    img.alpha_composite(glow)

    # Main disc with a radial-ish highlight (draw shrinking lighter circles).
    steps = int(r)
    for i in range(steps, 0, -1):
        t = i / steps
        col = lerp(GREEN_HI, GREEN_LO, t)
        rr = r * t
        # offset the highlight toward upper-left
        ox = cx - r * 0.18 * (1 - t)
        oy = cy - r * 0.18 * (1 - t)
        d.ellipse([ox - rr, oy - rr, ox + rr, oy + rr], fill=col + (255,))

    # Border ring.
    d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=GREEN_LO + (255,),
              width=max(2, S // 64))

    return img.resize((size, size), Image.LANCZOS)


def main():
    import io
    import struct

    # Encode each size as PNG and assemble the ICO container by hand so every
    # size is embedded (Pillow's ICO writer keeps only one).
    pngs = []
    for s in SIZES:
        buf = io.BytesIO()
        render(s).save(buf, format="PNG")
        pngs.append((s, buf.getvalue()))

    header = struct.pack("<HHH", 0, 1, len(pngs))
    offset = 6 + 16 * len(pngs)
    entries, data = b"", b""
    for s, png in pngs:
        b = s if s < 256 else 0  # 0 means 256 in ICO
        entries += struct.pack("<BBBBHHII", b, b, 0, 0, 1, 32, len(png), offset)
        data += png
        offset += len(png)

    with open(OUT, "wb") as f:
        f.write(header + entries + data)
    print("wrote", os.path.normpath(OUT), os.path.getsize(OUT), "bytes",
          "sizes:", [s for s, _ in pngs])


if __name__ == "__main__":
    main()
