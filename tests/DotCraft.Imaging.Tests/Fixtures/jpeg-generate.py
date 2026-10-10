"""Fixture provenance: Pillow 12.2.0/libjpeg; constant blocks use JPEG Annex F."""

from pathlib import Path
import sys
import struct
from PIL import Image, ImageCms

ROOT = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[3] / 'build/imaging-fixtures'
ROOT.mkdir(parents=True, exist_ok=True)


def save(name, image, **options):
    path = ROOT / f"jpeg-{name}.jpg"
    image.save(path, "JPEG", **options)
    with Image.open(path) as decoded:
        decoded.load()
        (ROOT / f"jpeg-{name}.rgba").write_bytes(decoded.convert("RGBA").tobytes())


def constant(name, ids, values, adobe=None):
    def segment(code, data):
        return bytes([255, code]) + struct.pack(">H", len(data) + 2) + data

    encoded = b"\xff\xd8"
    if adobe is not None:
        encoded += segment(0xEE, b"Adobe" + struct.pack(">HHHB", 100, 0, 0, adobe))
    encoded += segment(0xDB, b"\0" + bytes([1]) * 64)
    frame = bytes([8]) + struct.pack(">HHB", 1, 1, len(ids))
    encoded += segment(0xC0, frame + b"".join(bytes([i, 17, 0]) for i in ids))
    dc_table = bytes([0, 0, 0, 0, 12]) + bytes(12) + bytes(range(12))
    ac_table = bytes([16, 1]) + bytes(15) + bytes([0])
    encoded += segment(0xC4, dc_table) + segment(0xC4, ac_table)
    scan = bytes([len(ids)]) + b"".join(bytes([i, 0]) for i in ids) + bytes([0, 63, 0])
    encoded += segment(0xDA, scan)
    bits = ""
    for value in values:
        dc = (value - 128) * 8
        size = abs(dc).bit_length()
        amplitude = dc if dc >= 0 else dc + (1 << size) - 1
        bits += f"{size:04b}" + (f"{amplitude:0{size}b}" if size else "") + "0"
    bits += "1" * (-len(bits) % 8)
    entropy = bytes(int(bits[i:i + 8], 2) for i in range(0, len(bits), 8))
    encoded += entropy.replace(b"\xff", b"\xff\0") + b"\xff\xd9"
    path = ROOT / f"jpeg-{name}.jpg"
    path.write_bytes(encoded)
    with Image.open(path) as decoded:
        decoded.load()
        (ROOT / f"jpeg-{name}.rgba").write_bytes(decoded.convert("RGBA").tobytes())


def generate():
    width, height = 19, 13
    rgb = Image.new("RGB", (width, height))
    rgb.putdata([((x * 11 + y * 3) % 256, (x * 4 + y * 17) % 256,
                  (x * 9 + y * 7) % 256) for y in range(height) for x in range(width)])
    for name, subsampling, progressive, restart in (
        ("baseline-0", 0, False, False),
        ("baseline-1", 1, False, False),
        ("baseline-2", 2, False, False),
        ("progressive-2", 2, True, False),
        ("baseline-restart-0", 0, False, True),
        ("progressive-restart-2", 2, True, True),
    ):
        options = dict(quality=91, subsampling=subsampling, progressive=progressive)
        if restart:
            options["restart_marker_blocks"] = 1
        save(name, rgb, **options)
    save("gray", rgb.convert("L"), quality=91)
    save("gray-progressive", rgb.convert("L"), quality=91, progressive=True, restart_marker_blocks=1)
    save("cmyk", rgb.convert("CMYK"), quality=91)
    save("cmyk-progressive", rgb.convert("CMYK"), quality=91, progressive=True, restart_marker_blocks=1)
    exif = Image.Exif()
    exif[274] = 6
    icc = bytearray(ImageCms.ImageCmsProfile(ImageCms.createProfile("sRGB")).tobytes())
    struct.pack_into(">6H", icc, 24, 2026, 10, 10, 9, 34, 42)
    icc = bytes(icc)
    save("metadata", rgb, quality=91, subsampling=0, exif=exif, icc_profile=icc)
    (ROOT / "jpeg-metadata.exif").write_bytes(exif.tobytes()[6:])
    (ROOT / "jpeg-metadata.icc").write_bytes(icc)
    constant("rgb", list(b"RGB"), [230, 40, 100], 0)
    constant("ycck", [1, 2, 3, 4], [80, 128, 128, 200], 2)
    constant("cmyk-plain", [1, 2, 3, 4], [20, 40, 80, 60])
    # Raw CMYK without an Adobe inversion convention uses multiplicative inks.
    (ROOT / "jpeg-cmyk-plain.rgba").write_bytes(bytes([180, 164, 134, 255]))


if __name__ == "__main__":
    generate()
