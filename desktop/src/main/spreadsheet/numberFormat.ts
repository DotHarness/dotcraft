const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December']
const DAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']
const DAY_MS = 86_400_000
const EPOCH_1900_OFFSET = 25569
const EPOCH_1904_OFFSET = 24107

export function generalNumber(value: number): string {
  if (!Number.isFinite(value)) return String(value)
  if (Number.isInteger(value) && Math.abs(value) < 1e11) return String(value)
  const magnitude = Math.abs(value)
  if (magnitude !== 0 && (magnitude >= 1e11 || magnitude < 1e-9)) {
    return value.toExponential(5).replace(/\.?0+e/, 'e').replace('e', 'E').replace(/E([+-])(\d)$/, 'E$10$2')
  }
  return String(Number(value.toPrecision(10)))
}

function splitSections(format: string): string[] {
  const sections: string[] = []
  let current = ''
  let quoted = false
  for (let index = 0; index < format.length; index++) {
    const char = format[index]!
    if (char === '"') quoted = !quoted
    if (char === '\\' && !quoted) {
      current += char + (format[index + 1] ?? '')
      index++
      continue
    }
    if (char === ';' && !quoted) {
      sections.push(current)
      current = ''
      continue
    }
    current += char
  }
  sections.push(current)
  return sections
}

type Token = { kind: 'literal'; text: string } | { kind: 'code'; text: string }

function tokenize(section: string): Token[] {
  const tokens: Token[] = []
  const push = (kind: Token['kind'], text: string) => {
    const last = tokens[tokens.length - 1]
    if (last && last.kind === kind) last.text += text
    else tokens.push({ kind, text })
  }
  for (let index = 0; index < section.length; index++) {
    const char = section[index]!
    if (char === '"') {
      const end = section.indexOf('"', index + 1)
      push('literal', section.slice(index + 1, end < 0 ? undefined : end))
      index = end < 0 ? section.length : end
    } else if (char === '\\') {
      push('literal', section[index + 1] ?? '')
      index++
    } else if (char === '_') {
      push('literal', ' ')
      index++
    } else if (char === '*') {
      index++
    } else if (char === '[') {
      const end = section.indexOf(']', index)
      const inner = section.slice(index + 1, end < 0 ? undefined : end)
      const currency = /^\$([^-\]]*)/.exec(inner)
      if (currency) push('literal', currency[1]!)
      else if (/^h+$|^m+$|^s+$/i.test(inner)) push('code', `[${inner}]`)
      index = end < 0 ? section.length : end
    } else {
      push('code', char)
    }
  }
  return tokens
}

const DATE_CODE = /[ymdhs]/i

function isDateFormat(tokens: Token[]): boolean {
  return tokens.some((token) => token.kind === 'code' && DATE_CODE.test(token.text.replace(/General/gi, '')))
}

function pad(value: number, width: number): string {
  return String(value).padStart(width, '0')
}

function formatDate(serial: number, tokens: Token[], date1904: boolean): string {
  const milliseconds = Math.round((serial - (date1904 ? EPOCH_1904_OFFSET : EPOCH_1900_OFFSET)) * DAY_MS)
  const date = new Date(milliseconds)
  const parts = {
    year: date.getUTCFullYear(),
    month: date.getUTCMonth(),
    day: date.getUTCDate(),
    weekday: date.getUTCDay(),
    hours: date.getUTCHours(),
    minutes: date.getUTCMinutes(),
    seconds: date.getUTCSeconds()
  }
  const twelveHour = tokens.some((token) => token.kind === 'code' && /AM\/PM|A\/P/i.test(token.text))
  const pattern = /\[h+\]|\[m+\]|\[s+\]|AM\/PM|A\/P|y+|m+|d+|h+|s+|\.0+|./gi
  const pieces: Array<{ code: string; literal?: string }> = []
  for (const token of tokens) {
    if (token.kind === 'literal') pieces.push({ code: '', literal: token.text })
    else for (const match of token.text.matchAll(pattern)) pieces.push({ code: match[0] })
  }
  const isMinute = (index: number) => {
    for (let back = index - 1; back >= 0; back--) {
      const prior = pieces[back]!.code.toLowerCase()
      if (/^h+$|^\[h+\]$/.test(prior)) return true
      if (/^[yd]+$|^m+$/.test(prior)) break
    }
    for (let next = index + 1; next < pieces.length; next++) {
      const after = pieces[next]!.code.toLowerCase()
      if (/^s+$|^\[s+\]$/.test(after)) return true
      if (/^[ymdh]+$/.test(after)) break
    }
    return false
  }
  return pieces
    .map((piece, index) => {
      if (piece.literal !== undefined) return piece.literal
      const token = piece.code
      const lower = token.toLowerCase()
      if (lower === 'am/pm') return parts.hours < 12 ? 'AM' : 'PM'
      if (lower === 'a/p') return parts.hours < 12 ? 'A' : 'P'
      if (lower.startsWith('[h')) return String(Math.floor(serial * 24))
      if (lower.startsWith('[m')) return String(Math.floor(serial * 1440))
      if (lower.startsWith('[s')) return String(Math.floor(serial * 86400))
      if (lower.startsWith('.0')) return `.${pad(Math.floor((date.getUTCMilliseconds() / 1000) * 10 ** (lower.length - 1)), lower.length - 1)}`
      if (/^y+$/.test(lower)) return lower.length <= 2 ? pad(parts.year % 100, 2) : String(parts.year)
      if (/^m+$/.test(lower)) {
        if (lower.length <= 2 && isMinute(index)) return lower.length === 2 ? pad(parts.minutes, 2) : String(parts.minutes)
        if (lower.length === 1) return String(parts.month + 1)
        if (lower.length === 2) return pad(parts.month + 1, 2)
        if (lower.length === 3) return MONTHS[parts.month]!.slice(0, 3)
        if (lower.length === 5) return MONTHS[parts.month]![0]!
        return MONTHS[parts.month]!
      }
      if (/^d+$/.test(lower)) {
        if (lower.length === 1) return String(parts.day)
        if (lower.length === 2) return pad(parts.day, 2)
        if (lower.length === 3) return DAYS[parts.weekday]!.slice(0, 3)
        return DAYS[parts.weekday]!
      }
      if (/^h+$/.test(lower)) {
        const hours = twelveHour ? parts.hours % 12 || 12 : parts.hours
        return lower.length >= 2 ? pad(hours, 2) : String(hours)
      }
      if (/^s+$/.test(lower)) return lower.length >= 2 ? pad(parts.seconds, 2) : String(parts.seconds)
      return token
    })
    .join('')
}

function groupThousands(digits: string): string {
  return digits.replace(/\B(?=(\d{3})+(?!\d))/g, ',')
}

function formatNumber(value: number, tokens: Token[]): string | null {
  const code = tokens.filter((token) => token.kind === 'code').map((token) => token.text).join('')
  if (/\?\/|#\/|\/\?|\/#/.test(code)) return null
  const placeholders = /[0#?][0#?,.]*(?:[eE][+-][0#]+)?|\.[0#?]+/.exec(code)
  if (!placeholders) {
    return tokens.map((token) => token.text.replace(/General/gi, generalNumber(value))).join('')
  }
  const pattern = placeholders[0]
  let scaled = value
  if (code.includes('%')) scaled *= 100 ** (code.match(/%/g)?.length ?? 0)
  const trailingCommas = /,+$/.exec(pattern.replace(/[eE].*$/, ''))?.[0].length ?? 0
  scaled /= 1000 ** trailingCommas
  const body = pattern.replace(/,+$/, '')
  const exponent = /[eE]([+-])([0#]+)$/.exec(body)
  const mantissa = body.replace(/[eE].*$/, '')
  const [integerPart = '', fractionPart = ''] = mantissa.split('.')
  const decimals = fractionPart.replace(/[^0#?]/g, '').length
  const minimumDecimals = fractionPart.replace(/[^0]/g, '').length
  const minimumIntegers = integerPart.replace(/[^0]/g, '').length
  const grouping = integerPart.includes(',')
  let rendered: string
  if (exponent) {
    const [digits = '', power = '0'] = Math.abs(scaled).toExponential(decimals).split('e')
    const sign = Number(power) < 0 ? '-' : exponent[1] === '+' ? '+' : ''
    rendered = `${digits}E${sign}${pad(Math.abs(Number(power)), exponent[2]!.length)}`
  } else {
    const fixed = Math.abs(scaled).toFixed(decimals)
    let [whole = '', fraction = ''] = fixed.split('.')
    while (fraction.length > minimumDecimals && fraction.endsWith('0')) fraction = fraction.slice(0, -1)
    if (whole === '0' && minimumIntegers === 0) whole = ''
    whole = whole.padStart(minimumIntegers, '0')
    if (grouping) whole = groupThousands(whole)
    rendered = fraction ? `${whole}.${fraction}` : whole
  }
  let placed = false
  return tokens
    .map((token) => {
      if (token.kind === 'literal') return token.text
      const index = token.text.indexOf(pattern)
      if (placed || index < 0) return token.text.replace(/[0#?,.%]/g, (char) => (char === '%' ? '%' : ''))
      placed = true
      const before = token.text.slice(0, index).replace(/[0#?,.]/g, '')
      const after = token.text.slice(index + pattern.length).replace(/[0#?,.]/g, '')
      return `${before}${rendered}${after}`
    })
    .join('')
}

export function formatCellNumber(value: number, format: string | undefined, date1904 = false): string {
  if (!format || /^general$/i.test(format.trim()) || format.trim() === '@') return generalNumber(value)
  const sections = splitSections(format)
  const sectionIndex = value < 0 && sections.length > 1 ? 1 : value === 0 && sections.length > 2 ? 2 : 0
  const tokens = tokenize(sections[sectionIndex] ?? sections[0]!)
  if (isDateFormat(tokens)) return formatDate(value, tokens, date1904)
  const negativeSign = value < 0 && sectionIndex === 0 ? '-' : ''
  const formatted = formatNumber(sectionIndex === 1 ? Math.abs(value) : value, tokens)
  if (formatted === null) return generalNumber(value)
  return negativeSign && !formatted.startsWith('-') ? `${negativeSign}${formatted}` : formatted
}

export function dateToSerial(date: Date, date1904: boolean): number {
  return date.getTime() / DAY_MS + (date1904 ? EPOCH_1904_OFFSET : EPOCH_1900_OFFSET)
}
