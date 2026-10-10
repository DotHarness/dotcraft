"""Fixed fixtures generated independently with Pillow 12.2.0 and its WebP codec."""
from pathlib import Path
import sys
from PIL import Image, ImageCms
import random
import struct

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[3] / 'build/imaging-fixtures'
root.mkdir(parents=True, exist_ok=True)
random_source = random.Random(12026)
cases = {
    'palette': (37, 19, lambda x, y: [(230, 10, 80, 255), (12, 99, 211, 70), (8, 200, 18, 0)][(x+y)%3]),
    'binary': (71, 13, lambda x, y: (255, 120, 40, 255) if (x^y)&1 else (0, 0, 0, 0)),
    'gradient': (63, 29, lambda x, y: ((x*7+y*3)%256, (x*3+y*5)%256, (x*11+y*13)%256, (x*17+y*11)%256)),
    'repeated': (103, 57, lambda x, y: ((x%7)*30, (y%5)*50, ((x+y)%6)*40, 255)),
    'random': (43, 31, lambda x, y: tuple(random_source.randrange(256) for _ in range(4))),
    'solid': (9, 5, lambda x, y: (19, 108, 241, 37)),
    'tiles': (513, 129, lambda x, y: ((x*3)%256, y%256, 47, 255)
              if x//64%2 == 0 else tuple(random_source.randrange(256) for _ in range(4))),
}
for name, (width, height, pixel) in cases.items():
    image = Image.new('RGBA', (width, height))
    image.putdata([pixel(x, y) for y in range(height) for x in range(width)])
    for method in (0, 6):
        path = root / f'webp-{name}-{method}.webp'
        image.save(path, lossless=True, exact=True, method=method)
        path.with_suffix('.rgba').write_bytes(image.tobytes())

for name, dimensions in (('lossy-small', (7, 9)), ('lossy-multi', (67, 45)), ('lossy-alpha', (33, 21))):
    width, height = dimensions
    image = Image.new('RGBA', dimensions)
    image.putdata([((x*7+y*3)%256, (x*3+y*5)%256, (x*11+y*13)%256,
                    (x*17+y*11)%256 if name == 'lossy-alpha' else 255)
                   for y in range(height) for x in range(width)])
    path = root / f'webp-{name}.webp'
    image.save(path, quality=83, method=6)
    path.with_suffix('.rgba').write_bytes(Image.open(path).convert('RGBA').tobytes())

random_source = random.Random(310)
for i in range(12):
    dimensions = [(1, 1), (1, 23), (31, 1), (17, 17), (49, 97), (101, 123)][i % 6]
    image = Image.new('RGB', dimensions)
    image.putdata([tuple(random_source.randrange(256) for _ in range(3))
                   for _ in range(dimensions[0] * dimensions[1])])
    path = root / f'webp-lossy-random-{i}.webp'
    image.save(path, quality=[0, 5, 25, 50, 75, 100][i % 6], method=[0, 6][i // 6])
    path.with_suffix('.rgba').write_bytes(Image.open(path).convert('RGBA').tobytes())

exif = Image.Exif()
exif[274] = 6
profile = bytearray(ImageCms.ImageCmsProfile(ImageCms.createProfile('sRGB')).tobytes())
profile[24:36] = struct.pack('>6H', 2000, 1, 1, 0, 0, 0)
profile = bytes(profile)
image = Image.open(root / 'webp-gradient-6.webp')
image.save(root / 'webp-metadata.webp', lossless=True, exact=True, exif=exif, icc_profile=profile)
(root / 'webp-metadata.rgba').write_bytes(image.convert('RGBA').tobytes())
(root / 'webp-metadata.exif').write_bytes(Image.open(root / 'webp-metadata.webp').info['exif'])
(root / 'webp-metadata.icc').write_bytes(profile)

def chunk(tag, payload):
    return tag + struct.pack('<I', len(payload)) + payload + b'\0' * (len(payload)&1)
def number(value):
    return value.to_bytes(3, 'little')

original = (root / 'webp-metadata.webp').read_bytes()
position = 12
body = bytearray(b'WEBP')
while position < len(original):
    length = struct.unpack_from('<I', original, position + 4)[0]
    tag = original[position:position + 4]
    payload = original[position + 8:position + 8 + length]
    body.extend(chunk(tag, b'Exif\0\0' + payload if tag == b'EXIF' else payload))
    position += 8 + length + (length & 1)
(root / 'webp-metadata-prefix.webp').write_bytes(b'RIFF' + struct.pack('<I', len(body)) + body)
(root / 'webp-metadata-prefix.rgba').write_bytes((root / 'webp-metadata.rgba').read_bytes())

base = (root / 'webp-lossy-small.webp').read_bytes()
base_pixels = Image.open(root / 'webp-lossy-small.webp').convert('RGBA').tobytes()
width, height = 7, 9
alphas = bytes((x * 31 + y * 17) % 256 for y in range(height) for x in range(width))
for filtering in range(4):
    filtered = bytearray()
    for y in range(height):
        for x in range(width):
            i = y * width + x
            left = 0 if x == 0 else alphas[i - 1]
            top = 0 if y == 0 else alphas[i - width]
            top_left = 0 if x == 0 or y == 0 else alphas[i - width - 1]
            if filtering == 0:
                predictor = 0
            elif y == 0:
                predictor = left
            elif x == 0:
                predictor = top
            else:
                predictor = left if filtering == 1 else top if filtering == 2 else max(0, min(255, left + top - top_left))
            filtered.append((alphas[i] - predictor) & 255)
    for compression in (0, 1):
        if compression == 0:
            alpha_payload = bytes(filtered)
        else:
            image = Image.new('RGBA', (width, height))
            image.putdata([(0, value, 0, 255) for value in filtered])
            import io
            temporary = io.BytesIO()
            image.save(temporary, format='WEBP', lossless=True, exact=True, method=6)
            alpha_payload = temporary.getvalue()[25:]
            vp8l_size = struct.unpack_from('<I', temporary.getvalue(), 16)[0]
            alpha_payload = alpha_payload[:vp8l_size - 5]
        vp8x = bytes([16, 0, 0, 0]) + number(width - 1) + number(height - 1)
        body = b'WEBP' + chunk(b'VP8X', vp8x) + chunk(b'ALPH', bytes([compression | filtering << 2]) + alpha_payload) + base[12:]
        path = root / f'webp-lossy-alpha-filter-{filtering}-{compression}.webp'
        path.write_bytes(b'RIFF' + struct.pack('<I', len(body)) + body)
        expected = bytearray(base_pixels)
        expected[3::4] = alphas
        assert Image.open(path).convert('RGBA').tobytes() == expected
        path.with_suffix('.rgba').write_bytes(expected)

frame = (root/'webp-solid-6.webp').read_bytes()[12:]
vp8x = bytes([18, 0, 0, 0]) + number(15) + number(11)
anmf = number(2) + number(1) + number(8) + number(4) + number(50) + bytes([2]) + frame
body = b'WEBP' + chunk(b'VP8X', vp8x) + chunk(b'ANIM', bytes(6)) + chunk(b'ANMF', anmf) + chunk(b'ANMF', anmf)
(root/'webp-offset-animation.webp').write_bytes(b'RIFF'+struct.pack('<I', len(body))+body)
expected = Image.new('RGBA', (16, 12))
expected.paste(Image.open(root/'webp-solid-6.webp'), (4, 2))
(root/'webp-offset-animation.rgba').write_bytes(expected.tobytes())

for blend in (0, 2):
    frame = (root / 'webp-palette-6.webp').read_bytes()[12:]
    vp8x = bytes([18, 0, 0, 0]) + number(42) + number(24)
    anmf = number(1) + number(2) + number(36) + number(18) + number(50) + bytes([blend]) + frame
    body = b'WEBP' + chunk(b'VP8X', vp8x) + chunk(b'ANIM', bytes(6)) + chunk(b'ANMF', anmf)
    path = root / f'webp-animation-blend-{blend}.webp'
    path.write_bytes(b'RIFF' + struct.pack('<I', len(body)) + body)
    expected = Image.new('RGBA', (43, 25))
    source = Image.open(root / 'webp-palette-6.webp').convert('RGBA')
    if blend == 0:
        expected.alpha_composite(source, (2, 4))
    else:
        expected.paste(source, (2, 4))
    path.with_suffix('.rgba').write_bytes(expected.tobytes())
