export const DEV_NULL = '/dev/null'

const C_ESCAPES: Record<string, number> = { a: 7, b: 8, t: 9, n: 10, v: 11, f: 12, r: 13, '"': 34, '\\': 92 }
const ESCAPE_BY_BYTE = new Map(Object.entries(C_ESCAPES).map(([char, byte]) => [byte, char]))

function quotedNameEnd(value: string): number {
  for (let i = 1; i < value.length; i++) {
    if (value[i] === '\\') i++
    else if (value[i] === '"') return i + 1
  }
  return -1
}

/** Decodes a git C-style quoted name, whose octal escapes are UTF-8 bytes; unquoted names pass through. */
export function decodePatchName(token: string): string {
  if (!token.startsWith('"') || quotedNameEnd(token) !== token.length) return token
  const inner = token.slice(1, -1)
  const encoder = new TextEncoder()
  const bytes: number[] = []
  for (let i = 0; i < inner.length;) {
    if (inner[i] === '\\') {
      const octal = /^[0-7]{3}/.exec(inner.slice(i + 1, i + 4))
      if (octal) {
        bytes.push(parseInt(octal[0], 8))
        i += 4
        continue
      }
      const escaped = C_ESCAPES[inner[i + 1]]
      if (escaped !== undefined) {
        bytes.push(escaped)
        i += 2
        continue
      }
    }
    const char = String.fromCodePoint(inner.codePointAt(i)!)
    bytes.push(...encoder.encode(char))
    i += char.length
  }
  return new TextDecoder().decode(new Uint8Array(bytes))
}

export function encodePatchName(name: string): string {
  const bytes = new TextEncoder().encode(name)
  const printable = (byte: number): boolean => byte >= 0x20 && byte < 0x7f && !ESCAPE_BY_BYTE.has(byte)
  if (bytes.every(printable)) return name
  let quoted = '"'
  for (const byte of bytes) {
    const escape = ESCAPE_BY_BYTE.get(byte)
    if (escape !== undefined) quoted += `\\${escape}`
    else if (printable(byte)) quoted += String.fromCharCode(byte)
    else quoted += `\\${byte.toString(8).padStart(3, '0')}`
  }
  return `${quoted}"`
}

export function stripSidePrefix(name: string): string {
  return /^[ab]\//.test(name) ? name.slice(2) : name
}

export function splitFileHeaderName(value: string): [token: string, trailer: string] {
  const quotedEnd = value.startsWith('"') ? quotedNameEnd(value) : -1
  const end = quotedEnd !== -1 ? quotedEnd : value.includes('\t') ? value.indexOf('\t') : value.length
  return [value.slice(0, end), value.slice(end)]
}

export function splitGitHeaderNames(value: string): [oldToken: string, newToken: string] | null {
  if (value.startsWith('"')) {
    const end = quotedNameEnd(value)
    return end === -1 || value[end] !== ' ' ? null : [value.slice(0, end), value.slice(end + 1)]
  }
  // Unquoted names may contain spaces, so prefer the split where both sides name the same file.
  const middle = (value.length - 1) / 2
  if (value[middle] === ' ' && stripSidePrefix(value.slice(0, middle)) === stripSidePrefix(value.slice(middle + 1))) {
    return [value.slice(0, middle), value.slice(middle + 1)]
  }
  const split = value.indexOf(' b/')
  return split === -1 ? null : [value.slice(0, split), value.slice(split + 1)]
}
