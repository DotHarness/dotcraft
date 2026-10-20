import type { KeyboardEvent, MouseEvent } from 'react'
import { FileArchive, FolderClosed, GitBranch, type LucideIcon } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { usePluginStore, type MarketplaceEntry, type PluginEntry } from '../../stores/pluginStore'
import { ActionTooltip } from '../ui/ActionTooltip'
import styles from './PluginOriginBadge.module.css'

export const pluginOriginTitleClassName = styles.title

const ORIGINS: Record<MarketplaceEntry['sourceType'], { icon: LucideIcon; key: 'git' | 'local' | 'archive' }> = {
  git: { icon: GitBranch, key: 'git' },
  local: { icon: FolderClosed, key: 'local' },
  archive: { icon: FileArchive, key: 'archive' }
}

export function PluginOriginBadge({
  plugin,
  size = 'list'
}: {
  plugin: PluginEntry
  size?: 'list' | 'hero'
}): JSX.Element | null {
  const t = useT()
  const marketplace = usePluginStore((state) => originMarketplace(state.marketplaces, plugin))
  if (!marketplace) return null
  const origin = ORIGINS[marketplace.sourceType]
  if (!origin) return null
  const Icon = origin.icon
  const name = marketplace.displayName?.trim() || marketplace.name

  return (
    <ActionTooltip
      label={t(`plugins.origin.${origin.key}.tooltip`, { name })}
      detail={marketplace.sourceType === 'local' ? marketplaceDocumentPath(marketplace) : undefined}
    >
      <span
        role="img"
        tabIndex={0}
        aria-label={t(`plugins.origin.${origin.key}.label`)}
        className={styles.badge}
        data-size={size}
        onClick={stop}
        onKeyDown={stop}
      >
        <Icon size={size === 'hero' ? 18 : 14} aria-hidden />
      </span>
    </ActionTooltip>
  )
}

function originMarketplace(marketplaces: MarketplaceEntry[], plugin: PluginEntry): MarketplaceEntry | null {
  if (!plugin.marketplaceName) return null
  const marketplace = marketplaces.find((entry) => entry.name === plugin.marketplaceName)
  return marketplace?.removable ? marketplace : null
}

function marketplaceDocumentPath(marketplace: MarketplaceEntry): string {
  const root = (marketplace.root ?? marketplace.source).replace(/[\\/]+$/, '')
  if (!marketplace.marketplacePath) return root
  const separator = root.includes('\\') ? '\\' : '/'
  return `${root}${separator}${marketplace.marketplacePath.replace(/[\\/]/g, separator)}`
}

function stop(event: MouseEvent | KeyboardEvent): void {
  event.stopPropagation()
}
