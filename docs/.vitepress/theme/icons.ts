// Sidebar icons sourced from the official `lucide-static` package.
// We read each SVG at config-load time (Node), strip the wrapper, and re-emit
// the inner markup inside our own <svg> with consistent stroke / sizing tokens.

import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
const lucideDir = resolve(here, '..', '..', 'node_modules', 'lucide-static', 'icons')

const SVG_ATTRS =
  'viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"'

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
  return `<svg ${SVG_ATTRS}>${inner}</svg>`
}

// Map our semantic keys to canonical lucide icon names. Keep the keys stable
// so `withIcon('python', ...)` etc. still works in config.mts.
const SOURCES = {
  diamond: 'diamond',
  play: 'play',
  folder: 'folder',
  brain: 'brain',
  sparkles: 'sparkles',
  layers: 'layers',
  grid: 'layout-grid',
  monitor: 'monitor',
  terminal: 'terminal',
  code: 'code',
  bot: 'bot',
  globe: 'globe',
  puzzle: 'puzzle',
  users: 'users',
  workflow: 'workflow',
  activity: 'activity',
  shield: 'shield',
  branch: 'git-branch',
  cog: 'settings',
  server: 'server',
  network: 'network',
  fileCode: 'file-code',
  python: 'square-terminal',
  typescript: 'braces',
  package: 'package',
  plug: 'plug',
  book: 'book-open',
  tag: 'tag'
} as const

export const ICONS = Object.fromEntries(
  Object.entries(SOURCES).map(([key, lucideName]) => [key, loadLucide(lucideName)])
) as Record<keyof typeof SOURCES, string>

export type IconKey = keyof typeof SOURCES

export function withIcon(key: IconKey, label: string): string {
  const icon = ICONS[key] ?? ''
  return `<span class="dc-side-icon">${icon}</span><span class="dc-side-label">${label}</span>`
}
