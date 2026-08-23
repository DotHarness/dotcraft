import { useState } from 'react'
import type { CSSProperties, ReactNode } from 'react'
import { Ellipsis, Settings, Trash2 } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import type { MarketplaceEntry, PluginDiagnosticEntry, PluginEntry } from '../../stores/pluginStore'
import type { PluginCatalogSurface } from '../../stores/uiStore'
import {
  CatalogFilterMenu,
  CatalogScrollArea,
  CatalogSearchBox,
  CatalogTabs,
  CatalogToolbarIconButton,
  CatalogTopBar,
  styles as catalogStyles
} from '../catalog/CatalogSurface'
import { ActionTooltip } from '../ui/ActionTooltip'
import { RefreshIcon } from '../ui/AppIcons'
import { Button } from '../ui/Button'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { IconButton } from '../ui/IconButton'
import { SkeletonCatalogGrid } from '../ui/Skeleton'
import { SplitButton, type SplitButtonItem } from '../ui/SplitButton'
import { AddMarketplaceDialog } from './AddMarketplaceDialog'
import { PluginCatalogItem } from './PluginCatalogItem'
import { PluginDiagnosticsBanner } from './PluginDiagnosticsBanner'
import {
  marketplaceTitle,
  type CategoryFilter,
  type PluginSection,
  type PublisherFilter
} from './pluginCatalogModel'

type Surface = PluginCatalogSurface

export function PluginSurfaceTabs({
  value,
  onChange
}: {
  value: Surface
  onChange: (value: Surface) => void
}): JSX.Element {
  const t = useT()
  return (
    <CatalogTabs
      inTopBar
      value={value}
      onChange={onChange}
      items={[
        { value: 'plugins', label: t('plugins.tab.plugins') },
        { value: 'skills', label: t('plugins.tab.skills') }
      ]}
    />
  )
}

export function PluginBrowseSurface({
  surface,
  pluginManagement,
  pluginMarketplaces,
  remoteWorkspaceActive,
  loading,
  error,
  diagnostics,
  plugins,
  query,
  publisherFilter,
  categoryFilter,
  categoryOptions,
  marketplaces,
  sections,
  createActions,
  addMarketplaceOpen,
  installDialog,
  onSurfaceChange,
  onRefresh,
  onManage,
  onQueryChange,
  onPublisherFilterChange,
  onCategoryFilterChange,
  onOpenPlugin,
  onTryPlugin,
  onInstallPlugin,
  onRefreshMarketplace,
  onRemoveMarketplace,
  onCloseAddMarketplace,
  onMarketplaceAdded
}: {
  surface: Surface
  pluginManagement: boolean
  pluginMarketplaces: boolean
  remoteWorkspaceActive: boolean
  loading: boolean
  error: string | null
  diagnostics: PluginDiagnosticEntry[]
  plugins: PluginEntry[]
  query: string
  publisherFilter: PublisherFilter
  categoryFilter: CategoryFilter
  categoryOptions: Array<{ value: CategoryFilter; label: string }>
  marketplaces: MarketplaceEntry[]
  sections: PluginSection[]
  createActions: SplitButtonItem[]
  addMarketplaceOpen: boolean
  installDialog: ReactNode
  onSurfaceChange: (surface: Surface) => void
  onRefresh: () => void
  onManage: () => void
  onQueryChange: (query: string) => void
  onPublisherFilterChange: (publisher: PublisherFilter) => void
  onCategoryFilterChange: (category: CategoryFilter) => void
  onOpenPlugin: (plugin: PluginEntry) => void
  onTryPlugin: (plugin: PluginEntry) => void
  onInstallPlugin: (plugin: PluginEntry) => void
  onRefreshMarketplace: (marketplace: MarketplaceEntry) => void
  onRemoveMarketplace: (marketplace: MarketplaceEntry) => void
  onCloseAddMarketplace: () => void
  onMarketplaceAdded: (marketplace: MarketplaceEntry, alreadyAdded: boolean) => void
}): JSX.Element {
  const t = useT()
  return (
    <div style={page}>
      <CatalogTopBar
        navigation={<PluginSurfaceTabs value={surface} onChange={onSurfaceChange} />}
        actions={(
          <>
            <CatalogToolbarIconButton
              label={t('plugins.refresh')}
              onClick={onRefresh}
              icon={<RefreshIcon size={15} />}
            />
            <CatalogToolbarIconButton
              label={t('plugins.manage')}
              onClick={onManage}
              icon={<Settings size={15} aria-hidden />}
            />
            {createActions.length > 1 ? (
              <SplitButton
                label={t('plugins.create.button')}
                menuLabel={t('plugins.create.menuLabel')}
                onClick={createActions[0].onClick}
                items={createActions}
              />
            ) : (
              <Button variant="primary" size="toolbar" onClick={createActions[0].onClick}>
                {t('plugins.create.button')}
              </Button>
            )}
          </>
        )}
      />
      <header style={browseHeader}>
        <h1 style={heroTitle}>{t('plugins.heroTitle')}</h1>
        <div style={searchRow}>
          <CatalogSearchBox value={query} placeholder={t('plugins.searchPlaceholder')} onChange={onQueryChange} />
          <CatalogFilterMenu
            value={publisherFilter}
            ariaLabel={t('plugins.filter.publisher.label')}
            onChange={onPublisherFilterChange}
            options={[
              { value: 'dotcraft', label: t('plugins.filter.publisher.dotcraft') },
              { value: 'all', label: t('plugins.filter.publisher.all') },
              ...(pluginMarketplaces && marketplaces.length > 0
                ? [{ value: 'marketplaces' as const, label: t('plugins.filter.publisher.marketplaces') }]
                : [])
            ]}
          />
          <CatalogFilterMenu
            value={categoryFilter}
            ariaLabel={t('plugins.filter.category.label')}
            onChange={onCategoryFilterChange}
            options={categoryOptions}
          />
        </div>
      </header>
      <CatalogScrollArea>
        {!pluginManagement && <p style={emptyText}>{t('plugins.unavailable')}</p>}
        {loading && <SkeletonCatalogGrid ariaLabel={t('plugins.loading')} />}
        {error && <p style={{ ...emptyText, color: 'var(--error)' }} role="alert">{error}</p>}
        <PluginDiagnosticsBanner diagnostics={diagnostics} />
        {sections.map((section) => (
          <section key={section.key} style={{ marginBottom: '34px' }}>
            {section.marketplace ? (
              <MarketplaceSectionHeader
                marketplace={section.marketplace}
                onRefresh={() => onRefreshMarketplace(section.marketplace!)}
                onRemove={() => onRemoveMarketplace(section.marketplace!)}
              />
            ) : (
              <h2 style={sectionTitle}>{section.title}</h2>
            )}
            <div style={compactGrid}>
              {section.plugins.map((plugin) => (
                <PluginCatalogItem
                  key={plugin.id}
                  plugin={plugin}
                  tryLabel={t('plugins.tryInChat')}
                  installLabel={t('plugins.install')}
                  onOpen={() => onOpenPlugin(plugin)}
                  onTryInChat={() => onTryPlugin(plugin)}
                  onInstall={() => onInstallPlugin(plugin)}
                />
              ))}
            </div>
          </section>
        ))}
        {!loading && !error && plugins.length === 0 && <p style={emptyText}>{t('plugins.empty')}</p>}
      </CatalogScrollArea>
      {addMarketplaceOpen && (
        <AddMarketplaceDialog
          allowLocalFolder={!remoteWorkspaceActive}
          onClose={onCloseAddMarketplace}
          onAdded={onMarketplaceAdded}
        />
      )}
      {installDialog}
    </div>
  )
}

function MarketplaceSectionHeader({
  marketplace,
  onRefresh,
  onRemove
}: {
  marketplace: MarketplaceEntry
  onRefresh: () => void
  onRemove: () => void
}): JSX.Element {
  const t = useT()
  const [position, setPosition] = useState<ContextMenuPosition | null>(null)
  const [hovered, setHovered] = useState(false)
  const [actionFocused, setActionFocused] = useState(false)

  // Revealed by opacity rather than by mounting, so the row does not shift as the
  // pointer crosses it, and on keyboard focus too, or the actions are pointer-only.
  // See Selection Rows in specs/architecture/DESIGN.md.
  const actionsVisible = hovered || actionFocused || position != null

  return (
    <div
      style={marketplaceHeaderRow}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
    >
      {/* The source identifies the group without earning a place in the layout. */}
      <ActionTooltip label={marketplace.source} placement="bottom" multiline>
        <h2 style={marketplaceHeaderTitle}>{marketplaceTitle(marketplace)}</h2>
      </ActionTooltip>
      <div style={{ flex: 1 }} />
      {marketplace.removable && (
        <span style={revealedActionStyle(actionsVisible)}>
          <IconButton
            label={t('plugins.marketplace.actions')}
            tooltipLabel={t('plugins.marketplace.actions')}
            tooltipPlacement="bottom"
            aria-haspopup="menu"
            aria-expanded={position != null}
            size={28}
            onFocus={() => setActionFocused(true)}
            onBlur={() => setActionFocused(false)}
            onClick={(event) => {
              const rect = event.currentTarget.getBoundingClientRect()
              setPosition({ x: rect.right - 200, y: rect.bottom + 4 })
            }}
            icon={<Ellipsis size={15} aria-hidden />}
          />
        </span>
      )}
      {position && (
        <ContextMenu
          position={position}
          onClose={() => setPosition(null)}
          items={[
            {
              label: t('plugins.marketplace.refresh'),
              icon: <RefreshIcon size={14} />,
              onClick: onRefresh
            },
            {
              label: t('plugins.marketplace.remove'),
              icon: <Trash2 size={14} />,
              danger: true,
              onClick: onRemove
            }
          ]}
        />
      )}
    </div>
  )
}

const page: CSSProperties = catalogStyles.page
const browseHeader: CSSProperties = catalogStyles.browseHeader
const heroTitle: CSSProperties = catalogStyles.heroTitle
const searchRow: CSSProperties = catalogStyles.searchRow
const sectionTitle: CSSProperties = catalogStyles.sectionTitle
const compactGrid: CSSProperties = catalogStyles.compactGrid
const emptyText: CSSProperties = catalogStyles.emptyText
// A section title carries the section's column geometry, not just its type: the
// 760px width and `0 auto` centering are what line a heading up with the grid
// beneath it. Splitting the two lets the header row take the column while the
// heading keeps the type, instead of the row spanning the full main padding.
const { maxWidth: sectionColumnWidth, margin: sectionColumnMargin,
  ...sectionTitleType } = sectionTitle

const marketplaceHeaderRow: CSSProperties = {
  maxWidth: sectionColumnWidth,
  margin: sectionColumnMargin,
  display: 'flex',
  alignItems: 'center',
  gap: '10px'
}
// `minWidth: 0` lets a long marketplace name truncate instead of pushing the row
// wider than its column; a flex item defaults to its content's minimum width.
const marketplaceHeaderTitle: CSSProperties = {
  ...sectionTitleType,
  margin: 0,
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}
function revealedActionStyle(visible: boolean): CSSProperties {
  return {
    display: 'inline-flex',
    opacity: visible ? 1 : 0,
    pointerEvents: visible ? 'auto' : 'none',
    transition: 'opacity 120ms ease'
  }
}
