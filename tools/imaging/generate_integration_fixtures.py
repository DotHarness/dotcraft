"""Generate fixed integration fixtures and their provenance with Pillow."""

import hashlib
import json
from pathlib import Path

from PIL import Image, __version__


def main():
    destination = Path(__file__).resolve().parents[2] / "tests/DotCraft.Core.Tests/ImageFixtures"
    destination.mkdir(parents=True, exist_ok=True)
    for extension, image_format in [("png", "PNG"), ("jpg", "JPEG"), ("webp", "WEBP"), ("bmp", "BMP"), ("gif", "GIF")]:
        image = Image.new("RGB", (1, 1), (255, 0, 0))
        options = {"quality": 85} if extension == "jpg" else {"lossless": True} if extension == "webp" else {}
        image.save(destination / f"red-1.{extension}", image_format, **options)
    for size in range(1, 5):
        Image.new("RGBA", (size, size), (10, 20, 30, 255)).save(destination / f"color-{size}.png")
    manifest = {"generator": "Pillow", "version": __version__, "files": {}}
    for path in sorted(destination.iterdir()):
        if path.suffix not in {".png", ".jpg", ".webp", ".bmp", ".gif"}:
            continue
        with Image.open(path) as image:
            image.load()
            manifest["files"][path.name] = {
                "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                "size": list(image.size),
                "pixel": list(image.convert("RGBA").getpixel((0, 0))),
            }
    (destination / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
