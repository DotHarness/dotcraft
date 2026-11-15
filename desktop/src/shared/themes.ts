import type { DesktopPluginThemeSeed } from '@dotcraft/plugin'

export const THEME_MAX_SOURCE_BYTES = 1024 * 1024
export const THEME_MAX_POSTER_BYTES = 256 * 1024
export const THEME_MAX_IMAGE_BYTES = 8 * 1024 * 1024
export const THEME_MAX_THEMES = 100
export const THEME_MAX_LIBRARY_BYTES = 64 * 1024 * 1024
export const THEME_MAX_CHAT_CHOICES = 200
const THEME_MIN_INK_CONTRAST = 4.5
export const THEME_CHANGED_CHANNEL = 'themes:changed'
export const THEME_PREVIEW_CHANNEL = 'themes:preview'
export const THEME_CHOICES_CHANNEL = 'themes:choices-changed'

export type ThemeAppearance = 'light' | 'dark'
export type ThemeImageFit = 'cover' | 'contain' | 'tile'

export type ThemeBackdrop =
  | { readonly kind: 'scene'; readonly format: 'svg' | 'html'; readonly source: string; readonly poster: string }
  | { readonly kind: 'image'; readonly image: string; readonly fit: ThemeImageFit }
  | { readonly kind: 'gradient'; readonly stops: readonly string[]; readonly angle: number; readonly shape: 'linear' | 'radial' }
  | { readonly kind: 'color'; readonly color: string }

export interface ThemeInput {
  id: string
  name: string
  colorScheme: ThemeAppearance
  colors?: DesktopPluginThemeSeed
  backdrop?: ThemeBackdrop
  tint?: string
  pair?: string
  expectedRevision?: number
}

export interface Theme extends Omit<ThemeInput, 'expectedRevision'> {
  revision: number
  updatedAt: string
  authorThreadId?: string
  builtin?: boolean
}

export type ThemeChoice =
  | { readonly kind: 'none' }
  | { readonly kind: 'theme'; readonly id: string }

export type ThemeSlots = { light?: ThemeChoice; dark?: ThemeChoice }

export type ThemeScope =
  | { readonly kind: 'everywhere' }
  | { readonly kind: 'chat'; readonly threadId: string }

export interface ThemeChoices {
  readonly everywhere: ThemeSlots
  readonly chats: Readonly<Record<string, ThemeSlots & { readonly at: number }>>
}

export type ThemeSlotsPatch = Partial<Record<ThemeAppearance, ThemeChoice | null>>

export interface ThemeChoiceContext {
  readonly threadId: string | null
}

export interface ThemePreviewRequest {
  themeId: string
  threadId: string
}

export interface ThemesApi {
  list(): Promise<Theme[]>
  hasGpuCompositing(): Promise<boolean>
  save(theme: ThemeInput): Promise<Theme>
  remove(id: string): Promise<void>
  choices(): Promise<ThemeChoices>
  setChoice(scope: ThemeScope, patch: ThemeSlotsPatch): Promise<ThemeChoices>
  onChanged(listener: () => void): () => void
  onChoicesChanged(listener: (choices: ThemeChoices) => void): () => void
  onPreview(listener: (request: ThemePreviewRequest) => void): () => void
}

export const DEFAULT_THEME_CHOICES: ThemeChoices = {
  everywhere: { light: { kind: 'theme', id: 'builtin-honu-lagoon' }, dark: { kind: 'theme', id: 'builtin-braille-stars' } },
  chats: {}
}

export function slotsAt(choices: ThemeChoices, scope: ThemeScope): ThemeSlots {
  if (scope.kind === 'everywhere') return choices.everywhere
  return choices.chats[scope.threadId] ?? {}
}

export function resolveThemeChoice(
  choices: ThemeChoices,
  appearance: ThemeAppearance,
  context: ThemeChoiceContext,
  isAvailable: (choice: ThemeChoice, appearance: ThemeAppearance) => boolean = () => true
): { choice: ThemeChoice; scope: ThemeScope['kind'] } {
  const scopes: ThemeScope[] = [
    ...(context.threadId ? [{ kind: 'chat', threadId: context.threadId } as const] : []),
    { kind: 'everywhere' }
  ]
  for (const scope of scopes) {
    const choice = slotsAt(choices, scope)[appearance]
    if (choice && isAvailable(choice, appearance)) return { choice, scope: scope.kind }
  }
  return { choice: { kind: 'none' }, scope: 'everywhere' }
}

export function patchThemeChoices(
  choices: ThemeChoices,
  scope: ThemeScope,
  patch: ThemeSlotsPatch,
  at: number
): ThemeChoices {
  const slots: ThemeSlots = { ...slotsAt(choices, scope) }
  delete (slots as { at?: number }).at
  for (const appearance of ['light', 'dark'] as const) {
    const next = patch[appearance]
    if (next === undefined) continue
    if (next === null) delete slots[appearance]
    else slots[appearance] = next
  }
  const empty = !slots.light && !slots.dark
  if (scope.kind === 'everywhere') return { ...choices, everywhere: slots }
  const chats: Record<string, ThemeSlots & { at: number }> = { ...choices.chats }
  if (empty) delete chats[scope.threadId]
  else chats[scope.threadId] = { ...slots, at }
  return { ...choices, chats }
}

export function validateThemeChoice(value: unknown): ThemeChoice {
  if (!isRecord(value)) throw new Error('A theme choice object is required.')
  if (value.kind === 'none') return { kind: 'none' }
  if (value.kind !== 'theme' || typeof value.id !== 'string' || !value.id || value.id.length > 128) {
    throw new Error('A theme choice is none or names a theme id.')
  }
  return { kind: 'theme', id: value.id }
}

export function validateThemeScope(value: unknown): ThemeScope {
  const v = isRecord(value) ? value : {}
  if (v.kind === 'everywhere') return { kind: 'everywhere' }
  if (v.kind === 'chat' && typeof v.threadId === 'string' && v.threadId) return { kind: 'chat', threadId: v.threadId }
  throw new Error('A theme scope is everywhere or a chat.')
}

export function validateTheme(value: unknown): ThemeInput {
  if (!isRecord(value)) throw new Error('A theme object is required.')
  const v = value
  if (typeof v.id !== 'string' || !/^[a-z0-9][a-z0-9-]{0,63}$/.test(v.id) || v.id.startsWith('builtin-')) {
    throw new Error('Theme id must be 1–64 lowercase letters, digits or hyphens, and must not start with builtin-.')
  }
  if (typeof v.name !== 'string' || !v.name.trim() || v.name.length > 100) throw new Error('Theme name must be 1–100 characters.')
  if (v.colorScheme !== 'light' && v.colorScheme !== 'dark') throw new Error('Theme colorScheme must be light or dark.')
  if (v.colors === undefined && v.backdrop === undefined) throw new Error('A theme needs colors, a backdrop, or both.')
  const result: ThemeInput = { id: v.id, name: v.name.trim(), colorScheme: v.colorScheme }
  if (v.colors !== undefined) result.colors = validateColors(v.colors)
  if (v.backdrop !== undefined) result.backdrop = validateBackdrop(v.backdrop)
  if (v.tint !== undefined) {
    if (!result.backdrop) throw new Error('Theme tint needs a backdrop.')
    result.tint = rawColor(v.tint, 'tint')
  }
  if (v.pair !== undefined) {
    if (typeof v.pair !== 'string' || !/^[a-z0-9][a-z0-9-]{0,63}$/.test(v.pair) || v.pair === v.id) throw new Error('Theme pair must be the id of another theme.')
    result.pair = v.pair
  }
  if (v.expectedRevision !== undefined) {
    if (!Number.isSafeInteger(v.expectedRevision) || (v.expectedRevision as number) < 1) throw new Error('expectedRevision must be a positive integer.')
    result.expectedRevision = v.expectedRevision as number
  }
  return result
}

function validateColors(value: unknown): DesktopPluginThemeSeed {
  if (!isRecord(value)) throw new Error('Theme colors must be an object.')
  for (const key of ['surface', 'ink', 'accent']) {
    if (typeof value[key] !== 'string' || !/^#[0-9a-f]{6}$/i.test(value[key] as string)) throw new Error(`Theme colors.${key} must be a six-digit hex color.`)
  }
  const colors = {
    surface: value.surface as string, ink: value.ink as string, accent: value.accent as string,
    contrast: numberWithin(value.contrast, 0, 100, 'colors.contrast')
  }
  const ratio = contrastRatio(colors.ink, colors.surface)
  if (ratio < THEME_MIN_INK_CONTRAST) {
    throw new Error(`Theme colors.ink must reach ${THEME_MIN_INK_CONTRAST}:1 contrast against colors.surface, and this pair reaches ${ratio.toFixed(2)}:1.`)
  }
  return colors
}

function validateBackdrop(value: unknown): ThemeBackdrop {
  if (!isRecord(value)) throw new Error('Theme backdrop must be an object.')
  if (value.kind === 'scene') {
    if (value.format !== 'svg' && value.format !== 'html') throw new Error('Scene format must be svg or html.')
    const source = textWithin(value.source, THEME_MAX_SOURCE_BYTES, 'source')
    const poster = textWithin(value.poster, THEME_MAX_POSTER_BYTES, 'poster')
    if (!/^\s*<svg(?:\s|>)/i.test(poster)) throw new Error('Scene poster must be a static SVG document.')
    if (/<\s*(?:script|foreignObject|animate\w*|set)\b|\bon\w+\s*=|@(?:-\w+-)?keyframes\b|\b(?:animation|transition)(?:-[\w-]+)?\s*:/i.test(poster)) {
      throw new Error('Scene poster must be static SVG without scripts, event handlers, foreign objects or animation.')
    }
    if (value.format === 'svg' && !/^\s*<svg(?:\s|>)/i.test(source)) throw new Error('SVG scene source must start with <svg>.')
    return { kind: 'scene', format: value.format, source, poster }
  }
  if (value.kind === 'image') {
    if (typeof value.image !== 'string' || !/^data:image\/(?:jpeg|png|webp);base64,[A-Za-z0-9+/]+=*$/.test(value.image)) {
      throw new Error('Backdrop image must be a base64 data URL of a JPEG, PNG or WebP.')
    }
    if (Math.floor((value.image.length - value.image.indexOf(',') - 1) * 0.75) > THEME_MAX_IMAGE_BYTES) throw new Error(`Backdrop image exceeds ${THEME_MAX_IMAGE_BYTES} bytes.`)
    const fit = value.fit ?? 'cover'
    if (fit !== 'cover' && fit !== 'contain' && fit !== 'tile') throw new Error('Backdrop image fit must be cover, contain or tile.')
    return { kind: 'image', image: value.image, fit }
  }
  if (value.kind === 'gradient') {
    if (!Array.isArray(value.stops) || value.stops.length < 2 || value.stops.length > 16) throw new Error('Backdrop gradient needs 2 to 16 stops.')
    const shape = value.shape ?? 'linear'
    if (shape !== 'linear' && shape !== 'radial') throw new Error('Backdrop gradient shape must be linear or radial.')
    return {
      kind: 'gradient',
      stops: value.stops.map((stop, index) => rawColor(stop, `gradient.stops[${index}]`)),
      angle: value.angle === undefined ? 180 : numberWithin(value.angle, -360, 360, 'gradient.angle'),
      shape
    }
  }
  if (value.kind === 'color') return { kind: 'color', color: rawColor(value.color, 'backdrop.color') }
  throw new Error('Theme backdrop kind must be scene, image, gradient or color.')
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function numberWithin(value: unknown, min: number, max: number, name: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value) || value < min || value > max) throw new Error(`Theme ${name} must be between ${min} and ${max}.`)
  return value
}

function rawColor(value: unknown, name: string): string {
  if (typeof value !== 'string' || value.length > 64 ||
    !/^(?:#(?:[0-9a-f]{3,4}|[0-9a-f]{6}|[0-9a-f]{8})|(?:rgb|hsl)a?\([0-9.,%/ deg]{1,59}\))$/i.test(value)) {
    throw new Error(`Theme ${name} must be a hex, rgb() or hsl() color.`)
  }
  return value
}

function textWithin(value: unknown, maxBytes: number, name: string): string {
  if (typeof value !== 'string' || !value.trim()) throw new Error(`Scene ${name} is required.`)
  if (new TextEncoder().encode(value).byteLength > maxBytes) throw new Error(`Scene ${name} exceeds ${maxBytes} bytes.`)
  return value
}

function luminance(hex: string): number {
  const channel = (offset: number): number => {
    const value = parseInt(hex.slice(offset, offset + 2), 16) / 255
    return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4
  }
  return 0.2126 * channel(1) + 0.7152 * channel(3) + 0.0722 * channel(5)
}

function contrastRatio(a: string, b: string): number {
  const [light, dark] = [luminance(a), luminance(b)].sort((x, y) => y - x)
  return (light! + 0.05) / (dark! + 0.05)
}
