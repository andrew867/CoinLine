"""Rebuild the starter's terminal pixels from its operator-provided PNGs.

The monochrome display uses nine bottom-first bands of 112 columns; each
byte encodes eight vertical pixels with the top pixel in bit zero.
Requires Pillow. The 36-byte record-body filler is written by the table codec.
"""

from pathlib import Path
from PIL import Image


def pack_bitmap(image: Image.Image) -> bytes:
    if image.size != (112, 72):
        raise ValueError("Starter artwork must be 112 by 72 pixels.")
    pixels = image.convert("L").load()
    return bytes(
        sum((pixels[x, (8 - band) * 8 + bit] > 127) << bit for bit in range(8))
        for band in range(9)
        for x in range(112)
    )


if __name__ == "__main__":
    root = Path(__file__).resolve().parents[1]
    source = root / "web" / "src" / "assets" / "graphical-starter"
    destination = root / "backend" / "src" / "HostPlatform.Protocols.Tables" / "Assets" / "GraphicalStarter"
    destination.mkdir(parents=True, exist_ok=True)
    for frame in range(1, 21):
        name = f"frame-{frame:02d}"
        with Image.open(source / f"{name}.png") as image:
            (destination / f"{name}.bin").write_bytes(pack_bitmap(image))
    print("Packed 16 globe frames and 4 static advertisements.")
