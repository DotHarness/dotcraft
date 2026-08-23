import { useState } from 'react'
import type { CSSProperties } from 'react'
import { useT } from '../../contexts/LocaleContext'
import type { PluginDiagnosticEntry, PluginEntry } from '../../stores/pluginStore'
import type { SkillEntry } from '../../stores/skillsStore'
import type { PluginCatalogSurface } from '../../stores/uiStore'
import {
  CatalogBreadcrumb,
  CatalogHoverButton,
  CatalogScrollArea,
  CatalogSearchBox,
  CatalogTopBar,
  styles as catalogStyles
} from '../catalog/CatalogSurface'
import { SkillsManageList } from '../skills/SkillsView'
import { PillSwitch } from '../ui/PillSwitch'
import { SkeletonList } from '../ui/Skeleton'
import { PluginIcon, pluginSourceLabel, pluginSubtitle, pluginTitle } from './PluginCatalogItem'
import { PluginDiagnosticsBanner } from './PluginDiagnosticsBanner'
import { PluginInstallButton } from './PluginInstallButton'

type Surface = PluginCatalogSurface

export function PluginManageSurface({
  surface,
  pluginManagement,
  loading,
  error,
  diagnostics,
  plugins,
  pluginCount,
  pluginQuery,
  skills,
  skillsLoading,
  skillsError,
  skillsCount,
  skillQuery,
  onBack,
  onSurfaceChange,
  onPluginQueryChange,
  onSkillQueryChange,
  onOpenPlugin,
  onInstallPlugin,
  onTogglePlugin,
  onToggleSkill
}: {
  surface: Surface
  pluginManagement: boolean
  loading: boolean
  error: string | null
  diagnostics: PluginDiagnosticEntry[]
  plugins: PluginEntry[]
  pluginCount: number
  pluginQuery: string
  skills: SkillEntry[]
  skillsLoading: boolean
  skillsError: string | null
  skillsCount: number
  skillQuery: string
  onBack: () => void
  onSurfaceChange: (surface: Surface) => void
  onPluginQueryChange: (query: string) => void
  onSkillQueryChange: (query: string) => void
  onOpenPlugin: (plugin: PluginEntry) => void
  onInstallPlugin: (plugin: PluginEntry) => void
  onTogglePlugin: (plugin: PluginEntry, enabled: boolean) => void
  onToggleSkill: (skill: SkillEntry, enabled: boolean) => void
}): JSX.Element {
  const t = useT()
  return (
    <div style={page}>
      <CatalogTopBar
        navigation={(
          <CatalogBreadcrumb
            parentLabel={surface === 'plugins' ? t('plugins.pageTitle') : t('skills.pageTitle')}
            currentLabel={t('plugins.manage')}
            onBack={onBack}
          />
        )}
      />
      <header style={manageHeader}>
        <ManageToolbar
          key={surface}
          surface={surface}
          pluginCount={pluginCount}
          skillsCount={skillsCount}
          query={surface === 'plugins' ? pluginQuery : skillQuery}
          onSurfaceChange={onSurfaceChange}
          onQueryChange={surface === 'plugins' ? onPluginQueryChange : onSkillQueryChange}
        />
      </header>
      {surface === 'plugins' ? (
        <PluginsManageList
          pluginManagement={pluginManagement}
          loading={loading}
          error={error}
          diagnostics={diagnostics}
          plugins={plugins}
          onOpen={onOpenPlugin}
          onInstall={onInstallPlugin}
          onToggle={onTogglePlugin}
        />
      ) : (
        <SkillsManageList
          skills={skills}
          loading={skillsLoading}
          error={skillsError}
          onToggleEnabled={onToggleSkill}
        />
      )}
    </div>
  )
}

function ManageToolbar({
  surface,
  pluginCount,
  skillsCount,
  query,
  onSurfaceChange,
  onQueryChange
}: {
  surface: Surface
  pluginCount: number
  skillsCount: number
  query: string
  onSurfaceChange: (surface: Surface) => void
  onQueryChange: (query: string) => void
}): JSX.Element {
  const t = useT()
  return (
    <div style={manageToolbar}>
      <ManageSurfaceTabs
        value={surface}
        pluginsCount={pluginCount}
        skillsCount={skillsCount}
        onChange={onSurfaceChange}
      />
      <div style={{ flex: 1 }} />
      <CatalogSearchBox
        value={query}
        placeholder={surface === 'plugins'
          ? t('plugins.manage.searchPlaceholder')
          : t('skills.manage.searchPlaceholder')}
        onChange={onQueryChange}
        style={{ maxWidth: '280px', flex: '0 1 280px' }}
      />
    </div>
  )
}

function ManageSurfaceTabs({
  value,
  pluginsCount,
  skillsCount,
  onChange
}: {
  value: Surface
  pluginsCount: number
  skillsCount: number
  onChange: (surface: Surface) => void
}): JSX.Element {
  const t = useT()
  const items: Array<{ value: Surface; label: string }> = [
    { value: 'plugins', label: t('plugins.manage.count.plugins', { count: String(pluginsCount) }) },
    { value: 'skills', label: t('plugins.manage.count.skills', { count: String(skillsCount) }) }
  ]

  return (
    <div style={manageSurfaceTabs}>
      {items.map((item) => (
        <ManageSurfaceTab
          key={item.value}
          label={item.label}
          active={value === item.value}
          onClick={() => onChange(item.value)}
        />
      ))}
    </div>
  )
}

function ManageSurfaceTab({
  label,
  active,
  onClick
}: {
  label: string
  active: boolean
  onClick: () => void
}): JSX.Element {
  return (
    <CatalogHoverButton
      type="button"
      onClick={onClick}
      baseStyle={active ? manageSurfaceTabActive : manageSurfaceTab}
      hoverStyle={{ borderColor: 'transparent' }}
    >
      {label}
    </CatalogHoverButton>
  )
}

function PluginsManageList({
  pluginManagement,
  loading,
  error,
  diagnostics,
  plugins,
  onOpen,
  onInstall,
  onToggle
}: {
  pluginManagement: boolean
  loading: boolean
  error: string | null
  diagnostics: PluginDiagnosticEntry[]
  plugins: PluginEntry[]
  onOpen: (plugin: PluginEntry) => void
  onInstall: (plugin: PluginEntry) => void
  onToggle: (plugin: PluginEntry, enabled: boolean) => void
}): JSX.Element {
  const t = useT()

  return (
    <CatalogScrollArea variant="manage">
      {!pluginManagement && <p style={emptyText}>{t('plugins.unavailable')}</p>}
      {loading && (
        <SkeletonList
          count={5}
          ariaLabel={t('plugins.loading')}
          rowProps={{ media: 38, mediaRadius: 8, lines: ['46%', '30%'] }}
          rowStyle={{ maxWidth: '730px', margin: '0 auto', minHeight: '74px' }}
        />
      )}
      {error && <p style={{ ...emptyText, color: 'var(--error)' }} role="alert">{error}</p>}
      <PluginDiagnosticsBanner diagnostics={diagnostics} />
      {plugins.map((plugin) => (
        <PluginManageItem
          key={plugin.id}
          plugin={plugin}
          onOpen={() => onOpen(plugin)}
          onInstall={() => onInstall(plugin)}
          onToggle={(enabled) => onToggle(plugin, enabled)}
        />
      ))}
    </CatalogScrollArea>
  )
}

function PluginManageItem({
  plugin,
  onOpen,
  onInstall,
  onToggle
}: {
  plugin: PluginEntry
  onOpen: () => void
  onInstall: () => void
  onToggle: (enabled: boolean) => void
}): JSX.Element {
  const t = useT()
  const [active, setActive] = useState(false)
  return (
    <div
      style={interactiveManageRow(active)}
      onMouseEnter={() => setActive(true)}
      onMouseLeave={() => setActive(false)}
      onFocus={() => setActive(true)}
      onBlur={() => setActive(false)}
    >
      <button type="button" onClick={onOpen} style={manageItemMain}>
        <PluginIcon plugin={plugin} size={38} />
        <span style={pluginText}>
          <strong style={rowTitle}>{pluginTitle(plugin)}</strong>
          <span style={rowDesc}>{pluginSubtitle(plugin)}</span>
        </span>
      </button>
      <span style={manageSource} title={plugin.marketplaceName ?? undefined}>
        {plugin.marketplaceName || pluginSourceLabel(plugin)}
      </span>
      <span style={manageActionSlot}>
        {plugin.installed ? (
          <PillSwitch checked={plugin.enabled} onChange={onToggle} size="sm" aria-label={`${pluginTitle(plugin)} enabled`} />
        ) : (
          <PluginInstallButton onClick={onInstall}>{t('plugins.install')}</PluginInstallButton>
        )}
      </span>
    </div>
  )
}

const page: CSSProperties = catalogStyles.page
const compactItem: CSSProperties = catalogStyles.compactItem
const rowTitle: CSSProperties = catalogStyles.rowTitle
const rowDesc: CSSProperties = catalogStyles.rowDesc
const manageHeader: CSSProperties = catalogStyles.manageHeader
const manageToolbar: CSSProperties = catalogStyles.manageToolbar
const manageSurfaceTabs: CSSProperties = { display: 'flex', alignItems: 'center', gap: '8px' }
const manageSurfaceTab: CSSProperties = {
  ...catalogStyles.chip,
  border: 'none',
  cursor: 'pointer'
}
const manageSurfaceTabActive: CSSProperties = {
  ...catalogStyles.chipActive,
  border: 'none',
  cursor: 'pointer'
}
const manageRow: CSSProperties = catalogStyles.manageRow
const emptyText: CSSProperties = catalogStyles.emptyText

function interactiveManageRow(active: boolean): CSSProperties {
  return {
    ...manageRow,
    borderRadius: '8px',
    padding: '0 8px',
    boxSizing: 'border-box',
    backgroundColor: active ? 'var(--bg-tertiary)' : 'transparent',
    transition: 'background-color 120ms ease, color 120ms ease'
  }
}

const manageItemMain: CSSProperties = { ...compactItem, flex: 1, padding: 0, height: 'auto' }
const manageSource: CSSProperties = {
  width: '86px',
  flexShrink: 0,
  color: 'var(--text-secondary)',
  fontSize: '13px',
  textAlign: 'left',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}
// Fixed-width, centered slot for the trailing control so the Install button and the
// PillSwitch share one column: this keeps the developer column at a constant x across
// installed/uninstalled rows and centers both controls on the same vertical line.
const manageActionSlot: CSSProperties = {
  width: '84px',
  flexShrink: 0,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center'
}
const pluginText: CSSProperties = { display: 'flex', flexDirection: 'column', minWidth: 0, flex: 1 }
