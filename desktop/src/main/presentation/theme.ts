import { child, numberAttr, type XmlNode } from './xml'

export interface Theme {
  colors: Record<string, string>
  majorFont?: string
  minorFont?: string
}

export interface ColorContext {
  theme: Theme
  map: Record<string, string>
  placeholder?: string
}

const DEFAULT_MAP: Record<string, string> = {
  bg1: 'lt1',
  tx1: 'dk1',
  bg2: 'lt2',
  tx2: 'dk2'
}

const PRESET_COLORS: Record<string, string> = {
  black: '000000',
  white: 'FFFFFF',
  red: 'FF0000',
  green: '008000',
  blue: '0000FF',
  yellow: 'FFFF00',
  gray: '808080',
  grey: '808080',
  orange: 'FFA500',
  purple: '800080',
  darkBlue: '00008B',
  darkGray: 'A9A9A9',
  lightGray: 'D3D3D3'
}

const COLOR_NODES = new Set(['srgbClr', 'schemeClr', 'sysClr', 'prstClr', 'scrgbClr', 'hslClr'])

export function parseTheme(root: XmlNode | undefined): Theme {
  const elements = child(root, 'themeElements')
  const scheme = child(elements, 'clrScheme')
  const colors: Record<string, string> = {}
  for (const entry of scheme?.children ?? []) {
    const value = entry.children[0]
    const hex = value?.name === 'srgbClr' ? value.attrs.val : value?.name === 'sysClr' ? value.attrs.lastClr : undefined
    if (hex && /^[0-9a-fA-F]{6}$/.test(hex)) colors[entry.name] = hex.toUpperCase()
  }
  const fonts = child(elements, 'fontScheme')
  return {
    colors,
    majorFont: child(child(fonts, 'majorFont'), 'latin')?.attrs.typeface || undefined,
    minorFont: child(child(fonts, 'minorFont'), 'latin')?.attrs.typeface || undefined
  }
}

export function parseColorMap(node: XmlNode | undefined): Record<string, string> {
  return node ? { ...DEFAULT_MAP, ...node.attrs } : { ...DEFAULT_MAP }
}

interface Rgb {
  r: number
  g: number
  b: number
  a: number
}

function fromHex(hex: string): Rgb {
  return { r: parseInt(hex.slice(0, 2), 16), g: parseInt(hex.slice(2, 4), 16), b: parseInt(hex.slice(4, 6), 16), a: 1 }
}

function toHsl({ r, g, b }: Rgb): [number, number, number] {
  const [red, green, blue] = [r / 255, g / 255, b / 255]
  const max = Math.max(red, green, blue)
  const min = Math.min(red, green, blue)
  const light = (max + min) / 2
  if (max === min) return [0, 0, light]
  const delta = max - min
  const sat = light > 0.5 ? delta / (2 - max - min) : delta / (max + min)
  const hue =
    max === red ? (green - blue) / delta + (green < blue ? 6 : 0) : max === green ? (blue - red) / delta + 2 : (red - green) / delta + 4
  return [hue / 6, sat, light]
}

function fromHsl(hue: number, sat: number, light: number, a: number): Rgb {
  if (sat === 0) {
    const value = Math.round(light * 255)
    return { r: value, g: value, b: value, a }
  }
  const q = light < 0.5 ? light * (1 + sat) : light + sat - light * sat
  const p = 2 * light - q
  const channel = (t: number) => {
    const x = t < 0 ? t + 1 : t > 1 ? t - 1 : t
    if (x < 1 / 6) return p + (q - p) * 6 * x
    if (x < 1 / 2) return q
    if (x < 2 / 3) return p + (q - p) * (2 / 3 - x) * 6
    return p
  }
  return { r: Math.round(channel(hue + 1 / 3) * 255), g: Math.round(channel(hue) * 255), b: Math.round(channel(hue - 1 / 3) * 255), a }
}

const clamp01 = (value: number) => Math.min(1, Math.max(0, value))

function applyModifiers(color: Rgb, node: XmlNode): Rgb {
  let current = color
  for (const modifier of node.children) {
    const value = (numberAttr(modifier, 'val') ?? 100000) / 100000
    if (modifier.name === 'alpha') current = { ...current, a: clamp01(value) }
    else if (modifier.name === 'lumMod' || modifier.name === 'lumOff') {
      const [hue, sat, light] = toHsl(current)
      const next = modifier.name === 'lumMod' ? light * value : light + value
      current = fromHsl(hue, sat, clamp01(next), current.a)
    } else if (modifier.name === 'satMod') {
      const [hue, sat, light] = toHsl(current)
      current = fromHsl(hue, clamp01(sat * value), light, current.a)
    } else if (modifier.name === 'tint') {
      const mix = (channel: number) => Math.round(channel + (255 - channel) * (1 - value))
      current = { r: mix(current.r), g: mix(current.g), b: mix(current.b), a: current.a }
    } else if (modifier.name === 'shade') {
      const mix = (channel: number) => Math.round(channel * value)
      current = { r: mix(current.r), g: mix(current.g), b: mix(current.b), a: current.a }
    }
  }
  return current
}

function css({ r, g, b, a }: Rgb): string {
  const hex = (value: number) => Math.min(255, Math.max(0, value)).toString(16).padStart(2, '0')
  return a >= 1 ? `#${hex(r)}${hex(g)}${hex(b)}` : `rgba(${r}, ${g}, ${b}, ${Number(a.toFixed(3))})`
}

function baseHex(node: XmlNode, context: ColorContext): string | undefined {
  switch (node.name) {
    case 'srgbClr':
      return node.attrs.val
    case 'sysClr':
      return node.attrs.lastClr ?? (node.attrs.val === 'window' ? 'FFFFFF' : '000000')
    case 'prstClr':
      return PRESET_COLORS[node.attrs.val ?? '']
    case 'scrgbClr': {
      const channel = (name: string) => Math.round(clamp01((numberAttr(node, name) ?? 0) / 100000) * 255).toString(16).padStart(2, '0')
      return `${channel('r')}${channel('g')}${channel('b')}`
    }
    case 'schemeClr': {
      let name = node.attrs.val ?? ''
      if (name === 'phClr') return context.placeholder?.replace('#', '')
      name = context.map[name] ?? name
      return context.theme.colors[name]
    }
    default:
      return undefined
  }
}

export function resolveColor(parent: XmlNode | undefined, context: ColorContext): string | undefined {
  const node = parent?.children.find((entry) => COLOR_NODES.has(entry.name))
  if (!node) return undefined
  const hex = baseHex(node, context)
  if (!hex || !/^[0-9a-fA-F]{6}$/.test(hex)) return undefined
  return css(applyModifiers(fromHex(hex), node))
}

export function schemeColor(scheme: string, context: ColorContext, tint?: number): string | undefined {
  const color: XmlNode = { name: 'schemeClr', attrs: { val: scheme }, children: [], text: '' }
  if (tint !== undefined) color.children.push({ name: 'tint', attrs: { val: String(tint) }, children: [], text: '' })
  return resolveColor({ name: 'solidFill', attrs: {}, children: [color], text: '' }, context)
}

export function themeFont(typeface: string, theme: Theme): string | undefined {
  if (typeface.startsWith('+mj')) return theme.majorFont
  if (typeface.startsWith('+mn')) return theme.minorFont
  return typeface
}
