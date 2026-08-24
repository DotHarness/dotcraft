import type { useT } from '../../contexts/LocaleContext'
import type { MarketplaceEntry, PluginEntry } from '../../stores/pluginStore'
import { pluginSubtitle, pluginTitle } from './PluginCatalogItem'

/**
 * The browse listing's grouping selector. `marketplaces` is not a publisher but a
 * delivery route: it narrows to marketplace-sourced entries and groups them by
 * their source, which is where refresh and remove live. See the Plugin creation
 * and marketplace sources section in specs/clients/desktop-client.md.
 */
export type PublisherFilter = 'dotcraft' | 'all' | 'marketplaces'
export type CategoryFilter = string

export interface PluginSection {
  key: string
  title: string
  plugins: PluginEntry[]
  /** Present when the section groups a marketplace, which owns refresh and remove. */
  marketplace?: MarketplaceEntry
}

const FIXED_PLUGIN_CATEGORIES = [
  'coding',
  'design',
  'engineering',
  'security',
  'lifestyle',
  'productivity',
  'research',
  'uncategorized'
]

export function marketplaceTitle(marketplace: MarketplaceEntry): string {
  return marketplace.displayName?.trim() || marketplace.name
}

export function filterPlugins(
  plugins: PluginEntry[],
  query: string,
  publisherFilter: PublisherFilter,
  categoryFilter: CategoryFilter
): PluginEntry[] {
  const q = query.trim().toLowerCase()
  return plugins.filter((plugin) => {
    if (publisherFilter === 'dotcraft' && !isDotHarnessPlugin(plugin)) return false
    if (publisherFilter === 'marketplaces' && !plugin.marketplaceName) return false
    if (categoryFilter === 'featured' && !isFeaturedPlugin(plugin)) return false
    if (categoryFilter !== 'all' && categoryFilter !== 'featured' && pluginCategoryKey(plugin) !== categoryFilter) return false
    if (!q) return true
    return (
      plugin.id.toLowerCase().includes(q) ||
      pluginTitle(plugin).toLowerCase().includes(q) ||
      pluginSubtitle(plugin).toLowerCase().includes(q)
    )
  })
}

export function buildCategoryOptions(
  plugins: PluginEntry[],
  t: ReturnType<typeof useT>
): Array<{ value: CategoryFilter; label: string }> {
  const categories = new Set<string>(FIXED_PLUGIN_CATEGORIES)
  for (const plugin of plugins) {
    const key = pluginCategoryKey(plugin)
    if (key) categories.add(key)
  }

  const orderedCategories = [
    ...FIXED_PLUGIN_CATEGORIES,
    ...[...categories]
      .filter((category) => !FIXED_PLUGIN_CATEGORIES.includes(category))
      .sort((a, b) => categoryLabel(a, t).localeCompare(categoryLabel(b, t)))
  ]

  return [
    { value: 'all', label: t('plugins.filter.category.all') },
    { value: 'featured', label: t('plugins.filter.category.featured') },
    ...orderedCategories.map((category) => ({ value: category, label: categoryLabel(category, t) }))
  ]
}

export function buildSections(
  plugins: PluginEntry[],
  categoryFilter: CategoryFilter,
  publisherFilter: PublisherFilter,
  t: ReturnType<typeof useT>,
  marketplaces: MarketplaceEntry[]
): PluginSection[] {
  // Grouping by marketplace is the one mode that asks "where did this come from",
  // so it answers only that: no installed-state group, and the category filter
  // still narrows what each group contains.
  if (publisherFilter === 'marketplaces') {
    const sections: PluginSection[] = []
    for (const marketplace of marketplaces) {
      const owned = plugins.filter((plugin) => plugin.marketplaceName === marketplace.name)
      if (owned.length === 0) continue
      sections.push({
        key: `marketplace:${marketplace.name}`,
        title: marketplaceTitle(marketplace),
        plugins: owned,
        marketplace
      })
    }
    return sections
  }

  if (categoryFilter === 'featured') {
    return plugins.length > 0 ? [{ key: 'featured', title: t('plugins.section.featured'), plugins }] : []
  }

  if (categoryFilter !== 'all') {
    return plugins.length > 0 ? [{ key: categoryFilter, title: categoryLabel(categoryFilter, t), plugins }] : []
  }

  const local = plugins.filter(isLocalInstalledPlugin)
  const seen = new Set(local.map((plugin) => plugin.id))
  const sections: PluginSection[] = []
  if (local.length > 0) {
    sections.push({ key: 'local', title: t('plugins.section.local'), plugins: local })
  }

  const featured = plugins.filter((plugin) => isFeaturedPlugin(plugin) && !seen.has(plugin.id))
  if (featured.length > 0) {
    sections.push({ key: 'featured', title: t('plugins.section.featured'), plugins: featured })
    for (const plugin of featured) seen.add(plugin.id)
  }

  const byCategory = new Map<string, PluginEntry[]>()
  for (const plugin of plugins) {
    if (seen.has(plugin.id)) continue
    const key = pluginCategoryKey(plugin) || 'uncategorized'
    const group = byCategory.get(key) ?? []
    group.push(plugin)
    byCategory.set(key, group)
  }

  const orderedKeys = [
    ...FIXED_PLUGIN_CATEGORIES,
    ...[...byCategory.keys()]
      .filter((key) => !FIXED_PLUGIN_CATEGORIES.includes(key))
      .sort((a, b) => categoryLabel(a, t).localeCompare(categoryLabel(b, t)))
  ]
  for (const key of orderedKeys) {
    const group = byCategory.get(key)
    if (group == null || group.length === 0) continue
    sections.push({ key, title: categoryLabel(key, t), plugins: group })
  }

  return sections
}

export function displayCategory(category: string | null | undefined, t: ReturnType<typeof useT>): string {
  return categoryLabel(normalizeCategory(category), t)
}

function isFeaturedPlugin(plugin: PluginEntry): boolean {
  return plugin.id === 'browser' || plugin.id === 'chrome'
}

function isLocalInstalledPlugin(plugin: PluginEntry): boolean {
  return plugin.installed && plugin.source.toLowerCase() !== 'builtin'
}

function isDotHarnessPlugin(plugin: PluginEntry): boolean {
  const developer = plugin.interface?.developerName?.trim().toLowerCase()
  return plugin.id === 'browser' || developer === 'dotharness' || plugin.source.toLowerCase().includes('builtin')
}

function pluginCategoryKey(plugin: PluginEntry): string {
  return normalizeCategory(plugin.interface?.category)
}

function normalizeCategory(category?: string | null): string {
  const normalized = (category || '').trim().toLowerCase()
  if (!normalized) return 'uncategorized'
  return normalized.replace(/\s+/g, '-')
}

function categoryLabel(category: string, t: ReturnType<typeof useT>): string {
  if (category === 'coding') return t('plugins.filter.category.coding')
  if (category === 'design') return t('plugins.filter.category.design')
  if (category === 'engineering') return t('plugins.filter.category.engineering')
  if (category === 'security') return t('plugins.filter.category.security')
  if (category === 'lifestyle') return t('plugins.filter.category.lifestyle')
  if (category === 'productivity') return t('plugins.filter.category.productivity')
  if (category === 'research') return t('plugins.filter.category.research')
  if (category === 'uncategorized') return t('plugins.filter.category.uncategorized')
  return category
    .split('-')
    .filter(Boolean)
    .map((part) => part.slice(0, 1).toUpperCase() + part.slice(1))
    .join(' ')
}
