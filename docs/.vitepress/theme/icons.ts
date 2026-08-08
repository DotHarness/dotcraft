// Sidebar icons are loaded at config time and inlined into VitePress labels.
// Abstract docs concepts use lucide; brand/platform entries use local SVG or
// Simple Icons, but all render as monochrome currentColor for a unified sidebar.

import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
const lucideDir = resolve(here, '..', '..', 'node_modules', 'lucide-static', 'icons')
const simpleIconsPath = resolve(here, '..', '..', 'node_modules', '@iconify-json', 'simple-icons', 'icons.json')
const localIconDir = resolve(here, 'sidebar-icons')

const STROKE_SVG_ATTRS =
  'viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"'
const SOLID_SVG_ATTRS = 'viewBox="0 0 24 24" width="18" height="18" fill="currentColor" aria-hidden="true"'

type IconSource =
  | { type: 'lucide'; name: string }
  | { type: 'simpleIcon'; name: string }
  | { type: 'localSvg'; name: string }
  | { type: 'localStrokeSvg'; name: string }

type SimpleIconSet = {
  icons: Record<string, { body: string; width?: number; height?: number }>
}

const simpleIcons = JSON.parse(readFileSync(simpleIconsPath, 'utf-8')) as SimpleIconSet

function loadLucide(name: string): string {
  const raw = readFileSync(resolve(lucideDir, `${name}.svg`), 'utf-8')
  // lucide-static files begin with an XML license comment and a multi-line
  // <svg ...> opening tag, so we strip both wrappers and keep only the inner
  // shape markup. That lets our outer wrapper own size and stroke-width.
  const inner = raw
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/^\s*<svg[\s\S]*?>/i, '')
    .replace(/<\/svg>\s*$/i, '')
    .trim()
  return `<svg ${STROKE_SVG_ATTRS}>${inner}</svg>`
}

function loadSimpleIcon(name: string): string {
  const icon = simpleIcons.icons[name]
  if (!icon) {
    throw new Error(`Missing Simple Icons entry: ${name}`)
  }

  const width = icon.width ?? 24
  const height = icon.height ?? 24
  const body = icon.body.trim()
  return `<svg ${SOLID_SVG_ATTRS.replace('viewBox="0 0 24 24"', `viewBox="0 0 ${width} ${height}"`)}>${body}</svg>`
}

function loadLocalSvg(name: string): string {
  const raw = readFileSync(resolve(localIconDir, `${name}.svg`), 'utf-8')
  const inner = raw
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/^\s*<svg[\s\S]*?>/i, '')
    .replace(/<\/svg>\s*$/i, '')
    .trim()
  return `<svg ${SOLID_SVG_ATTRS}>${inner}</svg>`
}

function loadLocalStrokeSvg(name: string): string {
  const raw = readFileSync(resolve(localIconDir, `${name}.svg`), 'utf-8')
  const inner = raw
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/^\s*<svg[\s\S]*?>/i, '')
    .replace(/<\/svg>\s*$/i, '')
    .trim()
  return `<svg ${STROKE_SVG_ATTRS}>${inner}</svg>`
}

function loadIcon(source: IconSource): string {
  if (source.type === 'simpleIcon') return loadSimpleIcon(source.name)
  if (source.type === 'localSvg') return loadLocalSvg(source.name)
  if (source.type === 'localStrokeSvg') return loadLocalStrokeSvg(source.name)
  return loadLucide(source.name)
}

// Map semantic keys to icon sources. Keep the keys stable so config.mts can
// switch icon artwork without changing the sidebar authoring style.
const SOURCES = {
  play: { type: 'lucide', name: 'play' },
  brain: { type: 'lucide', name: 'brain' },
  sparkles: { type: 'lucide', name: 'sparkles' },
  layers: { type: 'lucide', name: 'layers' },
  grid: { type: 'lucide', name: 'layout-grid' },
  monitor: { type: 'lucide', name: 'monitor' },
  terminal: { type: 'lucide', name: 'terminal' },
  code: { type: 'lucide', name: 'code' },
  bot: { type: 'lucide', name: 'bot' },
  globe: { type: 'lucide', name: 'globe' },
  puzzle: { type: 'lucide', name: 'puzzle' },
  users: { type: 'lucide', name: 'users' },
  workflow: { type: 'lucide', name: 'workflow' },
  anchor: { type: 'lucide', name: 'anchor' },
  activity: { type: 'lucide', name: 'activity' },
  shield: { type: 'lucide', name: 'shield' },
  branch: { type: 'lucide', name: 'git-branch' },
  cog: { type: 'lucide', name: 'settings' },
  server: { type: 'lucide', name: 'server' },
  network: { type: 'lucide', name: 'network' },
  fileCode: { type: 'lucide', name: 'file-code' },
  dotnet: { type: 'simpleIcon', name: 'dotnet' },
  python: { type: 'simpleIcon', name: 'python' },
  typescript: { type: 'simpleIcon', name: 'typescript' },
  github: { type: 'simpleIcon', name: 'github' },
  gitlab: { type: 'simpleIcon', name: 'gitlab' },
  mcp: { type: 'localSvg', name: 'mcp' },
  oratorio: { type: 'localStrokeSvg', name: 'oratorio-baton' },
  package: { type: 'lucide', name: 'package' },
  plug: { type: 'lucide', name: 'plug' },
  route: { type: 'lucide', name: 'route' },
  database: { type: 'lucide', name: 'database' },
  radio: { type: 'lucide', name: 'radio' },
  antenna: { type: 'lucide', name: 'antenna' },
  webhook: { type: 'lucide', name: 'webhook' },
  box: { type: 'lucide', name: 'box' },
  boxes: { type: 'lucide', name: 'boxes' },
  waypoints: { type: 'lucide', name: 'waypoints' },
  share: { type: 'lucide', name: 'share-2' },
  sliders: { type: 'lucide', name: 'sliders-horizontal' },
  fileJson: { type: 'lucide', name: 'file-json' },
  scrollText: { type: 'lucide', name: 'scroll-text' },
  satelliteDish: { type: 'lucide', name: 'satellite-dish' },
  messageSquare: { type: 'lucide', name: 'message-square' },
  messagesSquare: { type: 'lucide', name: 'messages-square' },
  building: { type: 'lucide', name: 'building-2' },
  feather: { type: 'lucide', name: 'feather' },
  send: { type: 'lucide', name: 'send-horizontal' },
  smartphone: { type: 'lucide', name: 'smartphone' },
  botMessage: { type: 'lucide', name: 'bot-message-square' },
  plugZap: { type: 'lucide', name: 'plug-zap' },
  blocks: { type: 'lucide', name: 'blocks' },
  dashboard: { type: 'lucide', name: 'layout-dashboard' },
  layout: { type: 'lucide', name: 'layout' },
  cloud: { type: 'lucide', name: 'cloud' },
  rocket: { type: 'lucide', name: 'rocket' },
  repeat: { type: 'lucide', name: 'repeat' },
  history: { type: 'lucide', name: 'history' },
  cpu: { type: 'lucide', name: 'cpu' },
  fileCog: { type: 'lucide', name: 'file-cog' },
  book: { type: 'lucide', name: 'book-open' },
  tag: { type: 'lucide', name: 'tag' },
  qq: { type: 'localSvg', name: 'qq' },
  wecom: { type: 'localSvg', name: 'wecom' },
  feishu: { type: 'localSvg', name: 'feishu' },
  telegram: { type: 'localSvg', name: 'telegram' },
  weixin: { type: 'localSvg', name: 'weixin' }
} as const

export const ICONS = Object.fromEntries(
  Object.entries(SOURCES).map(([key, source]) => [key, loadIcon(source)])
) as Record<keyof typeof SOURCES, string>

export type IconKey = keyof typeof SOURCES

export function withIcon(key: IconKey, label: string): string {
  const icon = ICONS[key] ?? ''
  return `<span class="dc-side-icon">${icon}</span><span class="dc-side-label">${label}</span>`
}
