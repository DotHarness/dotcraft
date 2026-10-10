"""Generate fixed codec fixtures using Python stdlib and Pillow 12.2.0.

Expected RGBA/16-bit pixels come from sample values, never the production codecs.
Outputs are written to build/imaging-fixtures or the supplied directory. The
standards-based writers cover representations Pillow does not encode, and Pillow
checks the generated representations it supports.
"""
from pathlib import Path
import sys
import random
import struct
import zlib
from PIL import Image, ImageCms

ROOT = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[3] / 'build/imaging-fixtures'
ROOT.mkdir(parents=True, exist_ok=True)


def chunk(kind, data):
    return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))


def save(name, data, rgba):
    (ROOT / name).write_bytes(data)
    (ROOT / (name + '.rgba')).write_bytes(bytes(rgba))


def paeth(a, b, c):
    p = a + b - c
    aa, bb, cc = abs(p-a), abs(p-b), abs(p-c)
    return a if aa <= bb and aa <= cc else b if bb <= cc else c


def png_fixture(color, depth, interlace, small=False):
    width, height = (1, 1) if small else (9, 7)
    channels = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[color]
    maximum = (1 << depth) - 1
    palette = bytes([0, 0, 0, 255, 0, 10, 12, 255, 5, 44, 22, 255])[:3 * (1 << depth)]
    count = len(palette) // 3
    samples = []
    rgba, rgba16 = [], []
    transparent = [maximum // 2] * (1 if color == 0 else 3)
    for y in range(height):
        row = []
        for x in range(width):
            values = [((x * 71 + y * 131 + c * 59) % (maximum + 1)) for c in range(channels)]
            if color == 3:
                values = [(x+y) % count]
                r, g, b = palette[values[0]*3:values[0]*3+3]
                a = [0, 80, 170, 255][values[0]]
                out = [r, g, b, a]
            else:
                if x == 0 and y == 0 and color in (0, 2):
                    values = transparent.copy()
                r = values[0]
                g = values[1] if color in (2, 6) else r
                b = values[2] if color in (2, 6) else r
                a = values[-1] if color in (4, 6) else 0 if values == transparent else maximum
                raw = [r, g, b, a]
                out = [(v*255 + maximum//2)//maximum for v in raw]
                if depth == 16:
                    rgba16 += raw
            row.append(values)
            rgba += out
        samples.append(row)
    passes = [(0,0,1,1)] if not interlace else [(0,0,8,8),(4,0,8,8),(0,4,4,8),(2,0,4,4),(0,2,2,4),(1,0,2,2),(0,1,1,2)]
    encoded = bytearray()
    bpp = max(1, (channels*depth+7)//8)
    for x0, y0, dx, dy in passes:
        xs, ys = list(range(x0,width,dx)), list(range(y0,height,dy))
        if not xs or not ys:
            continue
        previous = bytearray((len(xs)*channels*depth+7)//8)
        for yi, y in enumerate(ys):
            values = [v for x in xs for v in samples[y][x]]
            if depth == 16:
                row = bytearray(struct.pack('>' + 'H'*len(values), *values))
            elif depth == 8:
                row = bytearray(values)
            else:
                row = bytearray((len(values)*depth+7)//8)
                for i, v in enumerate(values):
                    row[i*depth//8] |= v << (8-depth-i*depth%8)
            filter_type = yi % 5
            encoded.append(filter_type)
            for i, value in enumerate(row):
                left = row[i-bpp] if i >= bpp else 0
                up = previous[i]
                corner = previous[i-bpp] if i >= bpp else 0
                predictor = [0,left,up,(left+up)//2,paeth(left,up,corner)][filter_type]
                encoded.append((value-predictor)&255)
            previous = row
    output = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width,height,depth,color,0,0,int(interlace)))
    if color == 3:
        output += chunk(b'PLTE', palette) + chunk(b'tRNS', bytes([0,80,170,255][:count]))
    elif color in (0,2):
        output += chunk(b'tRNS', struct.pack('>'+'H'*len(transparent), *transparent))
    output += chunk(b'IDAT', zlib.compress(encoded)) + chunk(b'IEND', b'')
    name = f'png-c{color}-d{depth}-' + ('adam7' if interlace else 'plain') + ('-tiny' if small else '') + '.png'
    save(name, output, rgba)
    if depth == 16:
        (ROOT / (name + '.rgba16')).write_bytes(struct.pack('<'+'H'*len(rgba16), *rgba16))
    Image.open(ROOT/name).load()


def png_extra():
    image = Image.new('RGBA', (3,2))
    image.putdata([(255,0,0,255),(0,255,0,80),(0,0,255,0)]*2)
    exif = Image.Exif()
    exif[274] = 6
    profile = bytearray(ImageCms.ImageCmsProfile(ImageCms.createProfile('sRGB')).tobytes())
    struct.pack_into('>6H', profile, 24, 2026, 10, 10, 0, 0, 0)
    profile = bytes(profile)
    image.save(ROOT/'png-metadata.png', exif=exif, icc_profile=profile)
    (ROOT/'png-metadata.png.rgba').write_bytes(image.tobytes())
    (ROOT/'png-metadata.exif').write_bytes(exif.tobytes()[6:])
    (ROOT/'png-metadata.icc').write_bytes(profile)
    frame = Image.new('RGBA',(3,2),(4,6,8,255))
    image.save(ROOT/'png-default.apng', save_all=True, append_images=[frame], default_image=True, duration=100)
    (ROOT/'png-default.apng.rgba').write_bytes(image.tobytes())
    output = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR',struct.pack('>IIBBBBB',2,1,16,0,0,0,0))
    output += chunk(b'IDAT',zlib.compress(b'\x00'+struct.pack('>HH',26,131)))+chunk(b'IEND',b'')
    save('png-precision.png',output,[0,0,0,255,1,1,1,255])
    (ROOT/'png-precision.png.rgba16').write_bytes(struct.pack('<8H',26,26,26,65535,131,131,131,65535))


def gifs():
    palette = [0,0,0,255,0,0,0,255,0,0,0,255] + [0]*756
    for name, size in [('gif-small.gif',(3,2)), ('gif-interlace.gif',(24,18))]:
        image = Image.new('P',size)
        image.putpalette(palette)
        indices = [(x+y)%4 for y in range(size[1]) for x in range(size[0])]
        image.putdata(indices)
        image.save(ROOT/name, transparency=0, interlace=True, optimize=False)
        rgba = [v for i in indices for v in ([0,0,0,0] if i == 0 else palette[i*3:i*3+3]+[255])]
        (ROOT/(name+'.rgba')).write_bytes(bytes(rgba))
    data = bytearray((ROOT/'gif-small.gif').read_bytes())
    # The image descriptor follows the global table and graphic-control extension.
    descriptor = 13 + 768 + 8
    assert data[descriptor] == 44
    struct.pack_into('<HH',data,6,6,5)
    struct.pack_into('<HH',data,descriptor+1,2,1)
    expected = bytearray(6*5*4)
    source = (ROOT/'gif-small.gif.rgba').read_bytes()
    for y in range(2):
        expected[((y+1)*6+2)*4:((y+1)*6+5)*4] = source[y*12:(y+1)*12]
    save('gif-offset.gif',data,expected)
    randomizer = random.Random(2001)
    image = Image.new('P',(128,128))
    palette = [c for i in range(256) for c in (i,(i*7)%256,(i*11)%256)]
    image.putpalette(palette)
    indices = [randomizer.randrange(256) for _ in range(128*128)]
    image.putdata(indices)
    image.save(ROOT/'gif-dictionary.gif',interlace=False,optimize=False)
    (ROOT/'gif-dictionary.gif.rgba').write_bytes(bytes(v for i in indices for v in palette[i*3:i*3+3]+[255]))
    image = Image.new('P',(5,4),1)
    image.putpalette(palette)
    second = Image.new('P',(5,4),2)
    second.putpalette(palette)
    image.save(ROOT/'gif-animation.gif',save_all=True,append_images=[second],duration=100,optimize=False)
    (ROOT/'gif-animation.gif.rgba').write_bytes(bytes(palette[3:6]+[255])*20)


def bitmap(width,height,depth,raw,palette=b'',compression=0,masks=b'',topdown=False,core=False):
    if core:
        dib = struct.pack('<IHHHH',12,width,height,1,depth)
    else:
        dib = struct.pack('<IiiHHIIiiII',40,width,-height if topdown else height,1,depth,compression,len(raw),0,0,len(palette)//4,0)
    offset = 14+len(dib)+len(masks)+len(palette)
    return struct.pack('<2sIHHI',b'BM',offset+len(raw),0,0,offset)+dib+masks+palette+raw


def bmps():
    colors = [(0,0,0),(255,0,0),(0,255,0),(0,0,255),(255,255,255),(255,255,0),(0,255,255),(255,0,255)]
    width,height=5,3
    for depth in (1,4,8):
        count = min(1<<depth,8)
        indices = [(x+y)%count for y in range(height) for x in range(width)]
        palette = bytes(v for c in colors[:count] for v in (c[2],c[1],c[0],0))
        raw = bytearray()
        for y in reversed(range(height)):
            row = bytearray(((width*depth+31)//32)*4)
            for x in range(width):
                row[x*depth//8] |= indices[y*width+x] << (8-depth-x*depth%8)
            raw += row
        expected = [v for i in indices for v in colors[i]+(255,)]
        save(f'bmp-palette{depth}.bmp',bitmap(width,height,depth,raw,palette),expected)
        if depth == 4:
            core_palette = bytes(v for c in colors for v in (c[2],c[1],c[0])) + bytes(8*3)
            save('bmp-core.bmp',bitmap(width,height,depth,raw,core_palette,core=True),expected)
    for depth in (24,32):
        expected = [v for y in range(height) for x in range(width) for v in colors[(x+y)%8]+(255,)]
        for topdown in ((False,True) if depth == 24 else (False,)):
            raw = bytearray()
            for y in (range(height) if topdown else reversed(range(height))):
                row = bytes(v for x in range(width) for v in tuple(reversed(colors[(x+y)%8]))+((0,) if depth==32 else ()))
                raw += row+bytes((-len(row))%4)
            save(f'bmp-rgb{depth}-'+('top' if topdown else 'bottom')+'.bmp',bitmap(width,height,depth,raw,topdown=topdown),expected)
    for depth,masks in [(16,(0xf800,0x7e0,0x1f)),(32,(0x00ff0000,0x0000ff00,0x000000ff,0xff000000))]:
        expected=[]
        rows=[]
        for y in range(height):
            row=bytearray()
            for x in range(width):
                color = colors[(x+y)%8]
                alpha = (x*71+y*13)%256 if depth==32 else 255
                expected += list(color)+( [alpha] )
                value=0
                for v,mask in zip(color+(alpha,),masks):
                    shift=(mask&-mask).bit_length()-1
                    maximum=mask>>shift
                    value|=((v*maximum+127)//255)<<shift
                row+=struct.pack('<H' if depth==16 else '<I',value)
            rows.append(row+bytes((-len(row))%4))
        save(f'bmp-fields{depth}.bmp',bitmap(width,height,depth,b''.join(reversed(rows)),compression=3 if depth==16 else 6,masks=struct.pack('<'+'I'*len(masks),*masks)),expected)
    raw = b''.join(struct.pack('<H',(31 if x%2==0 else 0) << 10 | (31 if y%2 else 0) << 5 | (31 if x%2 else 0))
                   for y in reversed(range(height)) for x in range(width))
    raw = b''.join(raw[y*width*2:(y+1)*width*2]+bytes(2) for y in range(height))
    expected = [v for y in range(height) for x in range(width)
                for v in (255 if x%2==0 else 0,255 if y%2 else 0,255 if x%2 else 0,255)]
    save('bmp-rgb555.bmp',bitmap(width,height,16,raw),expected)
    fields = (ROOT/'bmp-fields32.bmp').read_bytes()
    expected = (ROOT/'bmp-fields32.bmp.rgba').read_bytes()
    old_offset = struct.unpack_from('<I',fields,10)[0]
    pixel_data = fields[old_offset:]
    profile = (ROOT/'png-metadata.icc').read_bytes()
    header = bytearray(124)
    header[:40] = fields[14:54]
    struct.pack_into('<I',header,0,len(header))
    struct.pack_into('<I',header,16,3)
    header[40:56] = fields[54:70]
    header[56:60] = b'DEBM'
    struct.pack_into('<II',header,112,len(header)+len(pixel_data),len(profile))
    offset = 14+len(header)
    data = struct.pack('<2sIHHI',b'BM',offset+len(pixel_data)+len(profile),0,0,offset)+header+pixel_data+profile
    save('bmp-header124.bmp',data,expected)
    for depth in (4,8):
        width,height=6,3
        palette=bytes(v for c in colors for v in (c[2],c[1],c[0],0))
        # Bottom row: encoded run. Middle: absolute run. Top: delta then run.
        raw=bytes([6,0x12 if depth==4 else 1,0,0])
        raw+=bytes([0,6,0x12,0x34,0x56,0] if depth==4 else [0,6,1,2,3,4,5,6])
        raw+=bytes([0,0,0,2,2,0,4,0x77 if depth==4 else 7,0,1])
        rows=[[0,0,7,7,7,7],[1,2,3,4,5,6],[1,2,1,2,1,2] if depth==4 else [1]*6]
        save(f'bmp-rle{depth}.bmp',bitmap(width,height,depth,raw,palette,compression=2 if depth==4 else 1),[v for row in rows for i in row for v in colors[i]+(255,)])


if __name__ == '__main__':
    for color, depth, interlace in (
        (0,1,False), (0,2,True), (0,4,False), (0,8,True), (0,16,False),
        (2,8,False), (2,16,True), (3,1,True), (3,4,False), (3,8,False),
        (4,8,False), (4,16,True), (6,8,False),
    ):
        png_fixture(color,depth,interlace)
    png_fixture(6,16,True,small=True)
    png_extra()
    gifs()
    bmps()
    for prefix in ('gif-', 'bmp-'):
        for file in ROOT.glob(prefix + '*'):
            if file.suffix not in ('.gif', '.bmp'):
                continue
            if file.name == 'bmp-fields32.bmp':
                # Pillow does not support BI_ALPHABITFIELDS; pixels are computed above.
                continue
            with Image.open(file) as image:
                assert image.convert('RGBA').tobytes() == Path(str(file)+'.rgba').read_bytes(), file.name
