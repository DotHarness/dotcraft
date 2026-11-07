import { Fragment, useState, type CSSProperties, type JSX, type KeyboardEvent } from 'react'
import { ArrowLeft, Search } from 'lucide-react'

import { useLocale, useT } from '../../contexts/LocaleContext'
import { useConnectionStore } from '../../stores/connectionStore'
import { useUIStore } from '../../stores/uiStore'
import { useWorkspaceProjectsStore } from '../../stores/workspaceProjectsStore'
import { isSessionImportAvailable } from '../../utils/sessionImport'
import {
  resolveDesktopPluginLabel,
  useDesktopPluginRegistry
} from '../../plugins/desktopPluginRegistry'
import { resolveDesktopPluginIcon } from '../desktopPlugins/DesktopPluginIcon'
import { IconButton } from '../ui/IconButton'
import { projectLabel, useWorkspaceProjectChoices } from '../conversation/workspaceProjectMenu'
import { SettingsSearchField } from '../settings/SettingsSearchField'
import {
  buildSettingsSearchEntries,
  filterSettingsSearchEntries,
  revealSettingsLabel,
  type SettingsSearchEntry
} from '../settings/settingsSearch'
import { SettingsWorkspacePicker } from '../settings/SettingsWorkspacePicker'
import { buildSettingsTabs, type SettingsTabGroup } from '../settings/settingsTabs'
import { prefetchSettingsPanel } from '../settings/coreSettingsPanels'
import {
  SIDEBAR_NAV_BORDER_INACTIVE,
  SIDEBAR_NAV_ICON_SLOT,
  SIDEBAR_NAV_ROW_OUTER
} from '../sidebar/sidebarNavRowStyles'

export function SettingsSidebar(): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const capabilities = useConnectionStore((s) => s.capabilities)
  const sidebarCollapsed = useUIStore((s) => s.sidebarCollapsed)
  const setSidebarCollapsed = useUIStore((s) => s.setSidebarCollapsed)
  const activeSettingsTab = useUIStore((s) => s.activeSettingsTab)
  const activeSidebarTab = activeSettingsTab === 'dreams' || activeSettingsTab === 'subAgents'
    ? 'personalization'
    : activeSettingsTab
  const setActiveSettingsTab = useUIStore((s) => s.setActiveSettingsTab)
  const requestCloseSettings = useUIStore((s) => s.requestCloseSettings)
  const [query, setQuery] = useState('')
  const [highlight, setHighlight] = useState(-1)
  const [focusSearch, setFocusSearch] = useState(false)

  const tabs = buildSettingsTabs(t, {
    sourceControlEnabled: capabilities?.sourceControlManagement === true,
    mcpEnabled: capabilities?.mcpManagement === true,
    hooksEnabled: capabilities?.hooksManagement === true,
    sessionImportEnabled: isSessionImportAvailable(capabilities)
  })
  const pluginTabs = useDesktopPluginRegistry((state) => state.settingsPages).map((entry) => ({
    id: entry.settingsKey,
    label: resolveDesktopPluginLabel(entry.label, locale),
    icon: resolveDesktopPluginIcon(entry.icon),
    group: 'integrations' as const
  }))
  const lastIntegration = tabs.findLastIndex((tab) => tab.group === 'integrations')
  tabs.splice(lastIntegration + 1, 0, ...pluginTabs)

  const searching = query.trim().length > 0
  const results = filterSettingsSearchEntries(
    buildSettingsSearchEntries(tabs, t, { subAgentEnabled: capabilities?.subAgentManagement === true }),
    query
  )
  const workspaceName = useForegroundWorkspaceName()

  const selectResult = (entry: SettingsSearchEntry): void => {
    setActiveSettingsTab(entry.tab)
    if (entry.revealLabel) revealSettingsLabel(entry.revealLabel)
  }

  const onSearchKeyDown = (event: KeyboardEvent<HTMLInputElement>): void => {
    if (event.key === 'Escape' && query) {
      event.preventDefault()
      setQuery('')
      setHighlight(-1)
    } else if (searching && event.key === 'ArrowDown') {
      event.preventDefault()
      setHighlight((index) => Math.min(index + 1, results.length - 1))
    } else if (searching && event.key === 'ArrowUp') {
      event.preventDefault()
      setHighlight((index) => Math.max(index - 1, 0))
    } else if (searching && event.key === 'Enter') {
      const entry = results[Math.max(highlight, 0)]
      if (entry) selectResult(entry)
    }
  }

  if (sidebarCollapsed) {
    return (
      <div style={collapsedContainerStyle}>
        <IconButton
          size={32}
          radius={8}
          className="dc-sidebar-icon-button"
          label={t('common.backToApp')}
          tooltipLabel={t('common.backToApp')}
          tooltipPlacement="right"
          onClick={requestCloseSettings}
          icon={<ArrowLeft size={16} aria-hidden="true" />}
        />
        <IconButton
          size={32}
          radius={8}
          className="dc-sidebar-icon-button"
          label={t('settings.search.label')}
          tooltipLabel={t('settings.search.label')}
          tooltipPlacement="right"
          onClick={() => {
            setFocusSearch(true)
            setSidebarCollapsed(false)
          }}
          icon={<Search size={16} aria-hidden="true" />}
        />

        {tabs.map((tab, index) => {
          const active = activeSidebarTab === tab.id
          const TabIcon = tab.icon
          const firstWorkspaceTab = tab.group === 'workspace' && tabs[index - 1]?.group !== 'workspace'
          return (
            <Fragment key={tab.id}>
              {firstWorkspaceTab && <SettingsWorkspacePicker collapsed />}
              <IconButton
                size={32}
                radius={8}
                className="dc-sidebar-icon-button"
                label={tab.label}
                tooltipLabel={tab.label}
                tooltipPlacement="right"
                active={active}
                onClick={() => setActiveSettingsTab(tab.id)}
                onPointerEnter={active ? undefined : () => prefetchSettingsPanel(tab.id)}
                onFocus={active ? undefined : () => prefetchSettingsPanel(tab.id)}
                icon={<TabIcon size={16} aria-hidden="true" />}
              />
            </Fragment>
          )
        })}
      </div>
    )
  }

  return (
    <div style={expandedContainerStyle}>
      <button
        className="dotcraft-sidebar-nav-button dotcraft-sidebar-row-radius"
        type="button"
        onClick={requestCloseSettings}
        style={backRowStyle}
        aria-label={t('common.backToApp')}
      >
        <span style={iconSlotStyle}>
          <ArrowLeft size={16} strokeWidth={2} aria-hidden="true" />
        </span>
        <span style={labelStyle}>{t('common.backToApp')}</span>
      </button>

      <SettingsSearchField
        value={query}
        autoFocus={focusSearch}
        onChange={(next) => {
          setQuery(next)
          setHighlight(-1)
        }}
        onKeyDown={onSearchKeyDown}
      />

      <nav className="dc-scrollbar-stable" aria-label={t('settings.title')} style={navStyle}>
        {searching ? (
          results.length === 0 ? (
            <div style={noResultsStyle}>{t('settings.search.noResults')}</div>
          ) : results.map((entry, index) => {
            const ResultIcon = entry.icon
            const description = [
              entry.label === entry.panelLabel ? null : entry.panelLabel,
              entry.group === 'workspace' ? workspaceName : null
            ].filter(Boolean).join(' · ')
            return (
              <button
                key={entry.id}
                className="dotcraft-sidebar-nav-button dotcraft-sidebar-row-radius"
                type="button"
                onClick={() => selectResult(entry)}
                onPointerEnter={() => prefetchSettingsPanel(entry.tab)}
                style={expandedTabStyle}
                data-active={index === highlight ? 'true' : undefined}
              >
                <span style={iconSlotStyle}>
                  <ResultIcon size={16} strokeWidth={2} aria-hidden="true" />
                </span>
                <span style={resultTextStyle}>
                  <span style={labelStyle}>{entry.label}</span>
                  {description && <span style={resultDescriptionStyle}>{description}</span>}
                </span>
              </button>
            )
          })
        ) : tabs.map((tab, index) => {
          const active = activeSidebarTab === tab.id
          const TabIcon = tab.icon
          const showGroupLabel = index === 0 || tabs[index - 1].group !== tab.group
          return (
            <div key={tab.id} style={tabGroupItemStyle(showGroupLabel)}>
              {showGroupLabel && (
                <div style={groupLabelStyle}>
                  {settingsGroupLabel(tab.group, t)}
                </div>
              )}
              {showGroupLabel && tab.group === 'workspace' && <SettingsWorkspacePicker />}
              <button
                className="dotcraft-sidebar-nav-button dotcraft-sidebar-row-radius"
                type="button"
                onClick={() => setActiveSettingsTab(tab.id)}
                onPointerEnter={active ? undefined : () => prefetchSettingsPanel(tab.id)}
                onFocus={active ? undefined : () => prefetchSettingsPanel(tab.id)}
                style={expandedTabStyle}
                aria-current={active ? 'page' : undefined}
                data-active={active ? 'true' : undefined}
              >
                <span style={iconSlotStyle}>
                  <TabIcon size={16} strokeWidth={2} aria-hidden="true" />
                </span>
                <span style={labelStyle}>{tab.label}</span>
              </button>
            </div>
          )
        })}
      </nav>
    </div>
  )
}

function useForegroundWorkspaceName(): string {
  const t = useT()
  const workspacePath = useWorkspaceProjectsStore((s) => s.foregroundWorkspacePath)
  const choices = useWorkspaceProjectChoices(workspacePath)
  if (choices.foregroundIsChat) return t('chatsRail.title')
  return choices.selectedProject ? projectLabel(choices.selectedProject) : ''
}

function settingsGroupLabel(group: SettingsTabGroup, t: ReturnType<typeof useT>): string {
  switch (group) {
    case 'personal':
      return t('settings.sidebar.group.personal')
    case 'integrations':
      return t('settings.sidebar.group.integrations')
    case 'workspace':
      return t('settings.sidebar.group.workspace')
  }
}

const expandedContainerStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  height: '100%',
  minHeight: 0,
  padding: '4px 0 10px',
  gap: '4px',
  overflow: 'hidden'
}

const collapsedContainerStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  alignItems: 'center',
  height: '100%',
  padding: '8px 0',
  gap: '6px',
  overflow: 'hidden'
}

const navStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  minHeight: 0,
  overflowY: 'auto'
}

function tabGroupItemStyle(showGroupLabel: boolean): CSSProperties {
  return {
    display: 'flex',
    flexDirection: 'column',
    marginTop: showGroupLabel ? '8px' : 0
  }
}

const groupLabelStyle: CSSProperties = {
  padding: '0 16px 2px',
  color: 'var(--text-dimmed)',
  fontSize: '11px',
  lineHeight: 1.35,
  fontWeight: 500
}

const backRowStyle: CSSProperties = {
  ...SIDEBAR_NAV_ROW_OUTER,
  ...SIDEBAR_NAV_BORDER_INACTIVE,
  color: 'var(--text-secondary)'
}

// Match the main sidebar nav rows: active state changes background + text
// colour only (via the shared CSS class), never weight.
const expandedTabStyle: CSSProperties = {
  ...SIDEBAR_NAV_ROW_OUTER,
  ...SIDEBAR_NAV_BORDER_INACTIVE
}

const iconSlotStyle: CSSProperties = SIDEBAR_NAV_ICON_SLOT

const labelStyle: CSSProperties = {
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const resultTextStyle: CSSProperties = {
  minWidth: 0,
  display: 'flex',
  flexDirection: 'column',
  padding: '4px 0'
}

const resultDescriptionStyle: CSSProperties = {
  ...labelStyle,
  color: 'var(--text-tertiary)',
  fontSize: '12px',
  lineHeight: 1.35
}

const noResultsStyle: CSSProperties = {
  padding: '8px 16px 0',
  color: 'var(--text-secondary)',
  fontSize: 'var(--type-ui-size)'
}
