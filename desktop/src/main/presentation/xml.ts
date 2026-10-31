export interface XmlNode {
  name: string
  attrs: Record<string, string>
  children: XmlNode[]
  text: string
}

const ENTITIES: Record<string, string> = { lt: '<', gt: '>', amp: '&', quot: '"', apos: "'" }

function decode(value: string): string {
  if (!value.includes('&')) return value
  return value.replace(/&(#x[0-9a-fA-F]+|#\d+|[a-zA-Z]+);/g, (match, entity: string) => {
    if (entity[0] === '#') {
      const code = entity[1] === 'x' ? parseInt(entity.slice(2), 16) : parseInt(entity.slice(1), 10)
      return Number.isFinite(code) && code >= 0 && code <= 0x10ffff ? String.fromCodePoint(code) : match
    }
    return ENTITIES[entity] ?? match
  })
}

function localName(name: string): string {
  const colon = name.indexOf(':')
  return colon >= 0 ? name.slice(colon + 1) : name
}

const isSpace = (code: number) => code === 32 || code === 9 || code === 10 || code === 13

/** Element names drop their namespace prefix; attribute names keep it (`r:id` differs from `id`). */
export function parseXml(source: string): XmlNode {
  const root: XmlNode = { name: '#document', attrs: {}, children: [], text: '' }
  const stack: XmlNode[] = [root]
  const length = source.length
  let index = source.charCodeAt(0) === 0xfeff ? 1 : 0
  while (index < length) {
    const open = source.indexOf('<', index)
    const top = stack[stack.length - 1]!
    if (open < 0) {
      top.text += decode(source.slice(index))
      break
    }
    if (open > index) top.text += decode(source.slice(index, open))
    if (source.startsWith('<!--', open)) {
      const end = source.indexOf('-->', open + 4)
      index = end < 0 ? length : end + 3
      continue
    }
    if (source.startsWith('<![CDATA[', open)) {
      const end = source.indexOf(']]>', open + 9)
      top.text += source.slice(open + 9, end < 0 ? length : end)
      index = end < 0 ? length : end + 3
      continue
    }
    const next = source[open + 1]
    if (next === '?' || next === '!') {
      const end = source.indexOf('>', open)
      index = end < 0 ? length : end + 1
      continue
    }
    if (next === '/') {
      const end = source.indexOf('>', open)
      if (stack.length > 1) stack.pop()
      index = end < 0 ? length : end + 1
      continue
    }
    let cursor = open + 1
    while (cursor < length && !isSpace(source.charCodeAt(cursor)) && source[cursor] !== '>' && source[cursor] !== '/') cursor++
    const node: XmlNode = { name: localName(source.slice(open + 1, cursor)), attrs: {}, children: [], text: '' }
    let selfClosing = false
    while (cursor < length) {
      const code = source.charCodeAt(cursor)
      if (isSpace(code)) {
        cursor++
        continue
      }
      if (source[cursor] === '>') {
        cursor++
        break
      }
      if (source[cursor] === '/') {
        selfClosing = true
        cursor++
        continue
      }
      const nameStart = cursor
      while (cursor < length && source[cursor] !== '=' && !isSpace(source.charCodeAt(cursor)) && source[cursor] !== '>' && source[cursor] !== '/') cursor++
      const name = source.slice(nameStart, cursor)
      while (cursor < length && isSpace(source.charCodeAt(cursor))) cursor++
      if (source[cursor] !== '=') {
        if (name) node.attrs[name] = ''
        continue
      }
      cursor++
      while (cursor < length && isSpace(source.charCodeAt(cursor))) cursor++
      const quote = source[cursor]
      if (quote !== '"' && quote !== "'") continue
      const end = source.indexOf(quote, cursor + 1)
      const valueEnd = end < 0 ? length : end
      node.attrs[name] = decode(source.slice(cursor + 1, valueEnd))
      cursor = valueEnd + 1
    }
    top.children.push(node)
    if (!selfClosing) stack.push(node)
    index = cursor
  }
  return root
}

export function child(node: XmlNode | undefined, name: string): XmlNode | undefined {
  return node?.children.find((entry) => entry.name === name)
}

export function children(node: XmlNode | undefined, name: string): XmlNode[] {
  return node ? node.children.filter((entry) => entry.name === name) : []
}

export function path(node: XmlNode | undefined, ...names: string[]): XmlNode | undefined {
  let current = node
  for (const name of names) current = child(current, name)
  return current
}

export function numberAttr(node: XmlNode | undefined, name: string): number | undefined {
  const raw = node?.attrs[name]
  if (raw === undefined || raw === '') return undefined
  const value = Number(raw)
  return Number.isFinite(value) ? value : undefined
}

export function boolAttr(node: XmlNode | undefined, name: string): boolean | undefined {
  const raw = node?.attrs[name]
  if (raw === undefined) return undefined
  return raw === '1' || raw === 'true'
}

/** Alternate content prefers the fallback, which uses only the base vocabulary. */
export function unwrapAlternate(node: XmlNode): XmlNode[] {
  if (node.name !== 'AlternateContent') return [node]
  const branch = child(node, 'Fallback') ?? child(node, 'Choice')
  return branch ? branch.children.flatMap(unwrapAlternate) : []
}
