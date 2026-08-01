import type { CSSProperties } from 'react'
import { ArrowLeft } from 'lucide-react'

import { useT } from '../../contexts/LocaleContext'
import { useConnectionStore } from '../../stores/connectionStore'
import { useUIStore } from '../../stores/uiStore'
import { ActionTooltip } from '../ui/ActionTooltip'
import { buildSettingsTabs, type SettingsTabGroup } from '../settings/settingsTabs'
import {
  SIDEBAR_NAV_BORDER_INACTIVE,
  SIDEBAR_NAV_ICON_SLOT,
  SIDEBAR_NAV_ROW_OUTER
} from '../sidebar/sidebarNavRowStyles'

export function SettingsSidebar(): JSX.Element {
  const t = useT()
  const capabilities = useConnectionStore((s) => s.capabilities)
  const sidebarCollapsed = useUIStore((s) => s.sidebarCollapsed)
  const activeSettingsTab = useUIStore((s) => s.activeSettingsTab)
  const activeSidebarTab = activeSettingsTab === 'dreams' ? 'personalization' : activeSettingsTab
  const setActiveSettingsTab = useUIStore((s) => s.setActiveSettingsTab)
  const requestCloseSettings = useUIStore((s) => s.requestCloseSettings)

  const workspaceCoreApiAvailable = typeof window.api.workspaceConfig?.getCore === 'function'
  const memoryManagementEnabled = capabilities?.memoryManagement === true
  const dreamsCapabilityEnabled = capabilities?.dreams === true
  const personalizationAvailable = workspaceCoreApiAvailable || memoryManagementEnabled || dreamsCapabilityEnabled
  const tabs = buildSettingsTabs(t, {
    personalizationAvailable,
    sourceControlEnabled: capabilities?.sourceControlManagement === true,
    mcpEnabled: capabilities?.mcpManagement === true,
    hooksEnabled: capabilities?.hooksManagement === true,
    subAgentEnabled: capabilities?.subAgentManagement === true
  })

  if (sidebarCollapsed) {
    return (
      <div style={collapsedContainerStyle}>
        <ActionTooltip label={t('common.backToApp')} placement="right">
          <button
            className="dotcraft-sidebar-nav-button dotcraft-sidebar-icon-control-radius"
            type="button"
            onClick={requestCloseSettings}
            aria-label={t('common.backToApp')}
            style={collapsedButtonStyle}
          >
            <ArrowLeft size={16} strokeWidth={2} aria-hidden="true" />
          </button>
        </ActionTooltip>

        {tabs.map((tab) => {
          const active = activeSidebarTab === tab.id
          const TabIcon = tab.icon
          return (
            <ActionTooltip key={tab.id} label={tab.label} placement="right">
              <button
                className="dotcraft-sidebar-nav-button dotcraft-sidebar-icon-control-radius"
                type="button"
                onClick={() => setActiveSettingsTab(tab.id)}
                aria-label={tab.label}
                data-active={active ? 'true' : undefined}
                style={collapsedButtonStyle}
              >
                <TabIcon size={16} strokeWidth={2} aria-hidden="true" />
              </button>
            </ActionTooltip>
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

      <nav aria-label={t('settings.title')} style={navStyle}>
        {tabs.map((tab, index) => {
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
              <button
                className="dotcraft-sidebar-nav-button dotcraft-sidebar-row-radius"
                type="button"
                onClick={() => setActiveSettingsTab(tab.id)}
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

function settingsGroupLabel(group: SettingsTabGroup, t: ReturnType<typeof useT>): string {
  switch (group) {
    case 'personal':
      return t('settings.sidebar.group.personal')
    case 'integrations':
      return t('settings.sidebar.group.integrations')
    case 'coding':
      return t('settings.sidebar.group.coding')
    case 'archived':
      return t('settings.sidebar.group.archived')
  }
}

const expandedContainerStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  height: '100%',
  minHeight: 0,
  padding: '10px 0',
  gap: '8px',
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
  ...SIDEBAR_NAV_BORDER_INACTIVE
}

// Match the main sidebar nav rows: active state changes background + text
// colour only (via the shared CSS class), never weight.
const expandedTabStyle: CSSProperties = {
  ...SIDEBAR_NAV_ROW_OUTER,
  ...SIDEBAR_NAV_BORDER_INACTIVE
}

const collapsedButtonStyle: CSSProperties = {
  width: 32,
  height: 32,
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  border: 'none',
  borderRadius: 'var(--sidebar-icon-control-radius)',
  padding: 0
}

const iconSlotStyle: CSSProperties = SIDEBAR_NAV_ICON_SLOT

const labelStyle: CSSProperties = {
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}
