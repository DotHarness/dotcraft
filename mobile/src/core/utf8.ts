export function decodeBase64(data: string): Uint8Array {
  const binary = atob(data.replace(/\s+/g, ''))
  const bytes = new Uint8Array(binary.length)
  for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index)
  return bytes
}

function continuation(bytes: ArrayLike<number>, index: number): number {
  const byte = bytes[index]
  if (byte === undefined || (byte & 0xc0) !== 0x80) throw new RangeError('Invalid UTF-8')
  return byte & 0x3f
}

export function decodeUtf8(bytes: ArrayLike<number>): string {
  const parts: string[] = []
  let chunk: number[] = []
  for (let index = 0; index < bytes.length; ) {
    const byte = bytes[index]
    let point: number
    if (byte < 0x80) {
      point = byte
      index += 1
    } else if (byte >= 0xc2 && byte < 0xe0) {
      point = ((byte & 0x1f) << 6) | continuation(bytes, index + 1)
      index += 2
    } else if (byte >= 0xe0 && byte < 0xf0) {
      point = ((byte & 0x0f) << 12) | (continuation(bytes, index + 1) << 6) | continuation(bytes, index + 2)
      if (point < 0x800 || (point >= 0xd800 && point < 0xe000)) throw new RangeError('Invalid UTF-8')
      index += 3
    } else if (byte >= 0xf0 && byte < 0xf5) {
      point = ((byte & 0x07) << 18) | (continuation(bytes, index + 1) << 12) | (continuation(bytes, index + 2) << 6) | continuation(bytes, index + 3)
      if (point < 0x10000 || point > 0x10ffff) throw new RangeError('Invalid UTF-8')
      index += 4
    } else {
      throw new RangeError('Invalid UTF-8')
    }
    chunk.push(point)
    if (chunk.length >= 8192) {
      parts.push(String.fromCodePoint(...chunk))
      chunk = []
    }
  }
  parts.push(String.fromCodePoint(...chunk))
  return parts.join('')
}

export function encodeUtf8(text: string): number[] {
  const bytes: number[] = []
  for (const char of text) {
    const point = char.codePointAt(0)!
    if (point < 0x80) bytes.push(point)
    else if (point < 0x800) bytes.push(0xc0 | (point >> 6), 0x80 | (point & 0x3f))
    else if (point < 0x10000) bytes.push(0xe0 | (point >> 12), 0x80 | ((point >> 6) & 0x3f), 0x80 | (point & 0x3f))
    else bytes.push(0xf0 | (point >> 18), 0x80 | ((point >> 12) & 0x3f), 0x80 | ((point >> 6) & 0x3f), 0x80 | (point & 0x3f))
  }
  return bytes
}
