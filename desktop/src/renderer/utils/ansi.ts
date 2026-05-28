export interface AnsiSpan {
  text: string
  fg?: string
  bg?: string
  bold?: boolean
  dim?: boolean
  italic?: boolean
  underline?: boolean
  strike?: boolean
  inverse?: boolean
}

interface AnsiAttrs {
  fg?: string
  bg?: string
  bold?: boolean
  dim?: boolean
  italic?: boolean
  underline?: boolean
  strike?: boolean
  inverse?: boolean
}

const ESC = '\u001b'
const BEL = '\u0007'

const ANSI_COLOR_VARS = [
  'var(--ansi-black)',
  'var(--ansi-red)',
  'var(--ansi-green)',
  'var(--ansi-yellow)',
  'var(--ansi-blue)',
  'var(--ansi-magenta)',
  'var(--ansi-cyan)',
  'var(--ansi-white)'
] as const

const ANSI_BRIGHT_COLOR_VARS = [
  'var(--ansi-bright-black)',
  'var(--ansi-bright-red)',
  'var(--ansi-bright-green)',
  'var(--ansi-bright-yellow)',
  'var(--ansi-bright-blue)',
  'var(--ansi-bright-magenta)',
  'var(--ansi-bright-cyan)',
  'var(--ansi-bright-white)'
] as const

export function stripAnsi(input: string): string {
  if (!input) return ''
  return parseAnsi(input).map((span) => span.text).join('')
}

export function parseAnsi(input: string): AnsiSpan[] {
  if (!input) return []

  const normalized = coalesceCarriageRewrites(input)
  const spans: AnsiSpan[] = []
  let attrs: AnsiAttrs = {}
  let buffer = ''
  let i = 0

  function flush(): void {
    if (buffer.length === 0) return
    spans.push({
      text: buffer,
      ...attrs
    })
    buffer = ''
  }

  while (i < normalized.length) {
    const ch = normalized[i]
    if (ch !== ESC) {
      buffer += ch
      i++
      continue
    }

    const next = normalized[i + 1]
    if (!next) {
      i++
      continue
    }

    if (next === '[') {
      const seqStart = i + 2
      let j = seqStart
      while (j < normalized.length) {
        const code = normalized.charCodeAt(j)
        if (code >= 0x40 && code <= 0x7e) break
        j++
      }
      if (j >= normalized.length) {
        i = normalized.length
        continue
      }
      const finalChar = normalized[j]
      const payload = normalized.slice(seqStart, j)
      if (finalChar === 'm') {
        flush()
        attrs = applySgr(attrs, payload)
      }
      i = j + 1
      continue
    }

    if (next === ']') {
      // OSC ... BEL or ESC \
      let j = i + 2
      let closed = false
      while (j < normalized.length) {
        const curr = normalized[j]
        if (curr === BEL) {
          j++
          closed = true
          break
        }
        if (curr === ESC && normalized[j + 1] === '\\') {
          j += 2
          closed = true
          break
        }
        j++
      }
      i = closed ? j : normalized.length
      continue
    }

    // Single char control sequence: ESC X
    i += 2
  }

  flush()
  return spans
}

function coalesceCarriageRewrites(input: string): string {
  if (!input.includes('\r')) return input
  const lines = input.split('\n')
  const normalized = lines.map((line) => {
    const noTrailingCr = line.replace(/\r+$/, '')
    if (!noTrailingCr.includes('\r')) return noTrailingCr
    return noTrailingCr.slice(noTrailingCr.lastIndexOf('\r') + 1)
  })
  return normalized.join('\n')
}

function applySgr(current: AnsiAttrs, payload: string): AnsiAttrs {
  const params = parseSgrParams(payload)
  let next = { ...current }
  let idx = 0

  while (idx < params.length) {
    const p = params[idx]
    switch (p) {
      case 0:
        next = {}
        break
      case 1:
        next.bold = true
        break
      case 2:
        next.dim = true
        break
      case 3:
        next.italic = true
        break
      case 4:
        next.underline = true
        break
      case 7:
        next.inverse = true
        break
      case 9:
        next.strike = true
        break
      case 22:
        next.bold = undefined
        next.dim = undefined
        break
      case 23:
        next.italic = undefined
        break
      case 24:
        next.underline = undefined
        break
      case 27:
        next.inverse = undefined
        break
      case 29:
        next.strike = undefined
        break
      case 39:
        next.fg = undefined
        break
      case 49:
        next.bg = undefined
        break
      default:
        if (p >= 30 && p <= 37) {
          next.fg = ANSI_COLOR_VARS[p - 30]
        } else if (p >= 90 && p <= 97) {
          next.fg = ANSI_BRIGHT_COLOR_VARS[p - 90]
        } else if (p >= 40 && p <= 47) {
          next.bg = ANSI_COLOR_VARS[p - 40]
        } else if (p >= 100 && p <= 107) {
          next.bg = ANSI_BRIGHT_COLOR_VARS[p - 100]
        } else if (p === 38 || p === 48) {
          const color = parseExtendedColor(params, idx + 1)
          if (color.consumed > 0 && color.css) {
            if (p === 38) {
              next.fg = color.css
            } else {
              next.bg = color.css
            }
            idx += color.consumed
          }
        }
        break
    }
    idx++
  }

  return next
}

function parseSgrParams(payload: string): number[] {
  if (payload.trim() === '') return [0]
  return payload.split(';').map((part) => {
    if (part === '') return 0
    const value = Number.parseInt(part, 10)
    return Number.isNaN(value) ? 0 : value
  })
}

function parseExtendedColor(
  params: number[],
  start: number
): { css?: string; consumed: number } {
  const mode = params[start]
  if (mode === 5) {
    const index = params[start + 1]
    if (index == null || Number.isNaN(index)) return { consumed: 0 }
    return { css: ansi256ToCss(index), consumed: 2 }
  }
  if (mode === 2) {
    const r = clampByte(params[start + 1])
    const g = clampByte(params[start + 2])
    const b = clampByte(params[start + 3])
    if (r == null || g == null || b == null) return { consumed: 0 }
    return { css: `rgb(${r}, ${g}, ${b})`, consumed: 4 }
  }
  return { consumed: 0 }
}

function ansi256ToCss(index: number): string {
  const n = clampByte(index) ?? 0
  if (n < 8) return ANSI_COLOR_VARS[n]
  if (n < 16) return ANSI_BRIGHT_COLOR_VARS[n - 8]
  if (n >= 232) {
    const gray = 8 + (n - 232) * 10
    return `rgb(${gray}, ${gray}, ${gray})`
  }

  const value = n - 16
  const r = Math.floor(value / 36)
  const g = Math.floor((value % 36) / 6)
  const b = value % 6
  const scale = [0, 95, 135, 175, 215, 255]
  return `rgb(${scale[r]}, ${scale[g]}, ${scale[b]})`
}

function clampByte(value: number | undefined): number | null {
  if (value == null || Number.isNaN(value)) return null
  return Math.max(0, Math.min(255, Math.round(value)))
}
