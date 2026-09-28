import type { ImportCandidate, ImportSettings } from '@dotcraft/sdk/contracts'

type Translate = (key: string, vars?: Record<string, string | number>) => string
type LocatedItem = Pick<ImportCandidate, 'category' | 'sourcePath' | 'targetPath'>

export const SETUP_CATEGORIES = ['skills', 'instructions', 'commands', 'hooks', 'mcp', 'plugins'] as const
export const importable = (item: Pick<ImportCandidate, 'state'>): boolean => item.state === 'new' || item.state === 'changed'

const MAX_LISTED_NAMES = 6
/** Per-item categories share a folder, which may carry a suffix such as `skills-*`. */
const CATEGORY_FOLDERS: Record<string, string> = {
  skills: 'skills',
  commands: 'commands',
  plugins: 'plugins'
}

export interface ImportLocation {
  source: string | null
  target: string | null
}

export function categoryLocation(items: LocatedItem[], workspacePath?: string): ImportLocation {
  const category = items[0]?.category ?? ''
  const side = (paths: string[]): string | null => {
    const common = commonPath(paths.filter(Boolean).map(path => anchorPath(normalizePath(path), category)))
    return common ? compactPath(common, workspacePath) : null
  }
  return {
    source: side(items.map(item => item.sourcePath)),
    target: side(items.map(item => item.targetPath))
  }
}

export function describeLocation(location: ImportLocation, t: Translate): string | null {
  if (location.source && location.target) return t('settings.import.setup.location', { source: location.source, target: location.target })
  if (location.target) return t('settings.import.setup.locationTarget', { target: location.target })
  return location.source
}

export function nameList(names: string[], locale: string, t: Translate): string {
  const shown = names.slice(0, MAX_LISTED_NAMES)
  const rest = names.length - shown.length
  const parts = rest > 0 ? [...shown, t('settings.import.setup.more', { count: rest })] : shown
  return new Intl.ListFormat(locale, { type: 'conjunction' }).format(parts)
}

export function categoryInfo(category: string, items: ImportCandidate[], locale: string, t: Translate): string {
  const names = (list: ImportCandidate[]): string => nameList(list.map(item => item.title), locale, t)
  const ready = items.filter(importable)
  const existing = items.filter(item => item.state === 'current')
  const unsupported = items.filter(item => !importable(item) && item.state !== 'current')
  return [
    ready.length > 0 ? t('settings.import.info.includes', { items: names(ready) }) : null,
    ...conversionNotes(category, items, t),
    existing.length > 0 ? t('settings.import.info.existing', { items: names(existing) }) : null,
    unsupported.length > 0 ? t('settings.import.info.unsupported', { items: names(unsupported) }) : null
  ].filter((sentence): sentence is string => sentence !== null).join(' ')
}

export function sessionsInfo(items: ImportCandidate[], locale: string, t: Translate): string {
  const ready = items.filter(importable)
  const imported = items.filter(item => item.state === 'current').length
  const deferred = items.filter(item => item.state === 'deferred').length
  return [
    ready.length > 0 ? t('settings.import.info.includes', { items: nameList(ready.map(item => item.title), locale, t) }) : null,
    imported > 0 ? t('settings.import.info.chatsImported', { count: imported }) : null,
    deferred > 0 ? t('settings.import.info.chatsDeferred', { count: deferred }) : null
  ].filter((sentence): sentence is string => sentence !== null).join(' ')
}

export function unavailableStatus(items: ImportCandidate[], t: Translate): string {
  if (items.every(item => item.state === 'current')) return t('settings.import.status.existing')
  if (items.every(item => !importable(item) && item.state !== 'current')) return t('settings.import.status.unsupported')
  return t('settings.import.source.none')
}

export function syncSelectionSummary(settings: ImportSettings, locale: string, t: Translate): string {
  if (!settings.hasImported) return t('settings.import.syncSummary.unavailable')
  const { selection } = settings
  if (selection.all) return t('settings.import.syncSummary.all')
  const chosen = new Set([...selection.user, ...selection.workspace])
  const labels = SETUP_CATEGORIES.filter(category => chosen.has(category)).map(category => t(`settings.import.category.${category}`))
  if (labels.length === 0) return t(selection.sessions ? 'settings.import.syncSummary.chatsOnly' : 'settings.import.syncSummary.none')
  if (selection.sessions) labels.push(t('settings.import.category.sessions'))
  return labels.length <= 3
    ? new Intl.ListFormat(locale, { type: 'conjunction' }).format(labels)
    : t('settings.import.syncSummary.count', { count: labels.length })
}

function conversionNotes(category: string, items: ImportCandidate[], t: Translate): string[] {
  switch (category) {
    case 'instructions': {
      const files = [...new Set(items.map(item => fileName(item.sourcePath)))].filter(file => file !== 'AGENTS.md')
      return [...files.map(file => t('settings.import.note.instructions', { file })), t('settings.import.note.rewrite')]
    }
    case 'skills':
    case 'commands':
      return [t('settings.import.note.rewrite')]
    case 'hooks':
      return [t('settings.import.note.hooks')]
    case 'mcp':
      return [t('settings.import.note.mcp')]
    case 'plugins':
      return [t('settings.import.note.plugins')]
    default:
      return []
  }
}

function normalizePath(path: string): string {
  return path.replace(/\\/g, '/').replace(/\/+$/, '')
}

function fileName(path: string): string {
  return normalizePath(path).split('/').at(-1) ?? path
}

function anchorPath(path: string, category: string): string {
  const folder = CATEGORY_FOLDERS[category]
  if (!folder) return path
  const segments = path.split('/')
  const index = segments.findIndex((segment, position) => position > 0 && (segment === folder || segment.startsWith(`${folder}-`)))
  return index > 0 ? segments.slice(0, index + 1).join('/') : path
}

/** Longest shared directory; null when the paths only meet at a home or drive root. */
function commonPath(paths: string[]): string | null {
  if (paths.length === 0) return null
  const fold = caseFold(paths[0])
  if (paths.every(path => fold(path) === fold(paths[0]))) return paths[0]
  const split = paths.map(path => path.split('/'))
  const shared: string[] = []
  for (let index = 0; index < split[0].length; index++) {
    const segment = fold(split[0][index])
    if (!split.every(parts => parts[index] !== undefined && fold(parts[index]) === segment)) break
    shared.push(split[0][index])
  }
  return shared.some(segment => segment.startsWith('.')) ? shared.join('/') : null
}

function compactPath(path: string, workspacePath?: string): string {
  if (workspacePath) {
    const root = normalizePath(workspacePath)
    const fold = caseFold(root)
    if (path.length > root.length && path[root.length] === '/' && fold(path.slice(0, root.length)) === fold(root)) {
      return path.slice(root.length + 1)
    }
  }
  const segments = path.split('/')
  const hidden = segments.findIndex((segment, index) => index > 1 && segment.startsWith('.'))
  return hidden > 0 ? ['~', ...segments.slice(hidden)].join('/') : path
}

function caseFold(sample: string): (value: string) => string {
  return /^[a-z]:\//i.test(sample) ? value => value.toLowerCase() : value => value
}
