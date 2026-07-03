import type { CSSProperties } from 'react'
import { ArrowLeft } from 'lucide-react'

import { useT } from '../../contexts/LocaleContext'
import { useConnectionStore } from '../../stores/connectionStore'
import { useUIStore } from '../../stores/uiStore'
import { ActionTooltip } from '../ui/ActionTooltip'
import { buildSettingsTabs, type SettingsTabGroup } from '../settings/settingsTabs'

export function SettingsSidebar(): JSX.Element {
  const t = useT()
  const capabilities = useConnectionStore((s) => s.capabilities)
  const sidebarCollapsed = useUIStore((s) => s.sidebarCollapsed)
  const activeSettingsTab = useUIStore((s) => s.activeSettingsTab)
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
            className="dc-settings-sidebar-button dotcraft-sidebar-control-radius"
            type="button"
            onClick={requestCloseSettings}
            aria-label={t('common.backToApp')}
            style={collapsedButtonStyle}
          >
            <ArrowLeft size={18} strokeWidth={2} aria-hidden="true" />
          </button>
        </ActionTooltip>

        {tabs.map((tab) => {
          const active = activeSettingsTab === tab.id
          const TabIcon = tab.icon
          return (
            <ActionTooltip key={tab.id} label={tab.label} placement="right">
              <button
                className="dc-settings-sidebar-button dotcraft-sidebar-control-radius"
                type="button"
                onClick={() => setActiveSettingsTab(tab.id)}
                aria-label={tab.label}
                data-active={active ? 'true' : undefined}
                style={collapsedButtonStyle}
              >
                <TabIcon size={17} strokeWidth={1.9} aria-hidden="true" />
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
        className="dc-settings-sidebar-button dotcraft-sidebar-control-radius"
        type="button"
        onClick={requestCloseSettings}
        style={backRowStyle}
        aria-label={t('common.backToApp')}
      >
        <ArrowLeft size={17} strokeWidth={2} aria-hidden="true" />
        <span style={labelStyle}>{t('common.backToApp')}</span>
      </button>

      <nav aria-label={t('settings.title')} style={navStyle}>
        {tabs.map((tab, index) => {
          const active = activeSettingsTab === tab.id
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
                className="dc-settings-sidebar-button dotcraft-sidebar-control-radius"
                type="button"
                onClick={() => setActiveSettingsTab(tab.id)}
                style={expandedTabStyle(active)}
                aria-current={active ? 'page' : undefined}
                data-active={active ? 'true' : undefined}
              >
                <span style={iconSlotStyle}>
                  <TabIcon size={17} strokeWidth={1.9} aria-hidden="true" />
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
  padding: '10px 8px',
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
  overflowY: 'auto',
  gap: '2px'
}

function tabGroupItemStyle(showGroupLabel: boolean): CSSProperties {
  return {
    display: 'flex',
    flexDirection: 'column',
    gap: '2px',
    marginTop: showGroupLabel ? '8px' : 0
  }
}

const groupLabelStyle: CSSProperties = {
  padding: '0 8px 2px',
  color: 'var(--text-dimmed)',
  fontSize: '11px',
  lineHeight: 1.35,
  fontWeight: 500
}

const backRowStyle: CSSProperties = {
  width: '100%',
  minHeight: 34,
  padding: '6px 8px',
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  border: 'none',
  borderRadius: 6,
  background: 'var(--settings-sidebar-row-bg, transparent)',
  color: 'var(--settings-sidebar-row-color, var(--text-secondary))',
  fontSize: 'var(--type-ui-size)',
  lineHeight: 'var(--type-ui-line-height)',
  cursor: 'pointer',
  textAlign: 'left',
  transition: 'background-color 120ms ease, color 120ms ease'
}

function expandedTabStyle(active: boolean): CSSProperties {
  return {
    width: '100%',
    minHeight: 34,
    padding: '7px 8px',
    display: 'flex',
    alignItems: 'center',
    gap: '8px',
    border: 'none',
    borderRadius: 6,
    background: 'var(--settings-sidebar-row-bg, transparent)',
    color: 'var(--settings-sidebar-row-color, var(--text-secondary))',
    fontSize: 'var(--type-ui-size)',
    lineHeight: 'var(--type-ui-line-height)',
    fontWeight: active ? 600 : 500,
    cursor: 'pointer',
    textAlign: 'left',
    transition: 'background-color 120ms ease, color 120ms ease'
  }
}

const collapsedButtonStyle: CSSProperties = {
  width: 32,
  height: 32,
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  border: 'none',
  borderRadius: 6,
  background: 'var(--settings-sidebar-row-bg, transparent)',
  color: 'var(--settings-sidebar-row-color, var(--text-secondary))',
  cursor: 'pointer',
  padding: 0,
  transition: 'background-color 120ms ease, color 120ms ease'
}

const iconSlotStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  width: 20,
  height: 20,
  flexShrink: 0
}

const labelStyle: CSSProperties = {
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}
