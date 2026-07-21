import { useState, type CSSProperties } from 'react'
import { Check, MessageCircle, Plus } from 'lucide-react'
import type { PluginEntry } from '../../stores/pluginStore'
import { styles as catalogStyles } from '../catalog/CatalogSurface'
import { ActionTooltip } from '../ui/ActionTooltip'
import { PluginInstallButton } from './PluginInstallButton'

export function PluginCatalogItem({
  plugin,
  onOpen,
  onTryInChat,
  onInstall,
  tryLabel,
  installLabel,
  style
}: {
  plugin: PluginEntry
  onOpen?: () => void
  onTryInChat?: () => void
  onInstall?: () => void
  tryLabel: string
  installLabel: string
  style?: CSSProperties
}): JSX.Element {
  const [active, setActive] = useState(false)
  const showTry = plugin.installed && plugin.enabled && active && onTryInChat
  const showInstall = !plugin.installed && onInstall

  return (
    <div
      role="button"
      tabIndex={0}
      onClick={onOpen}
      onKeyDown={(event) => {
        if (event.key !== 'Enter' && event.key !== ' ') return
        event.preventDefault()
        onOpen?.()
      }}
      onMouseEnter={() => setActive(true)}
      onMouseLeave={() => setActive(false)}
      onFocus={() => setActive(true)}
      onBlur={() => setActive(false)}
      style={{
        ...catalogStyles.compactItem,
        backgroundColor: active ? 'var(--bg-tertiary)' : 'transparent',
        color: active ? 'var(--text-primary)' : catalogStyles.compactItem.color,
        ...style
      }}
    >
      <PluginIcon plugin={plugin} size={40} />
      <span style={pluginText}>
        <strong style={catalogStyles.rowTitle}>{pluginTitle(plugin)}</strong>
        <span style={catalogStyles.rowDesc}>{pluginSubtitle(plugin)}</span>
      </span>
      <span style={catalogStyles.statusIcon}>
        {showTry ? (
          <ActionTooltip label={tryLabel}>
            <button
              type="button"
              aria-label={tryLabel}
              onClick={(event) => {
                event.stopPropagation()
                onTryInChat?.()
              }}
              style={tryAction(active)}
            >
              <MessageCircle size={14} aria-hidden />
              <span>{tryLabel}</span>
            </button>
          </ActionTooltip>
        ) : showInstall ? (
          <PluginInstallButton
            onClick={(event) => {
              event.stopPropagation()
              onInstall?.()
            }}
            style={{ backgroundColor: actionBackground(active) }}
          >
            {installLabel}
          </PluginInstallButton>
        ) : plugin.installed && plugin.enabled ? (
          <Check size={16} aria-hidden />
        ) : (
          <span style={iconAction(active)}>
            <Plus size={16} aria-hidden />
          </span>
        )}
      </span>
    </div>
  )
}

export function PluginIcon({ plugin, size }: { plugin: PluginEntry; size: number }): JSX.Element {
  const icon = plugin.interface?.composerIconDataUrl || plugin.interface?.logoDataUrl
  return (
    <span style={{ ...iconShell, width: size, height: size, backgroundColor: plugin.interface?.brandColor || '#0B63CE' }}>
      {icon ? <img src={icon} alt="" style={{ width: '100%', height: '100%', objectFit: 'contain' }} /> : pluginTitle(plugin).slice(0, 1)}
    </span>
  )
}

export function pluginTitle(plugin: PluginEntry): string {
  return plugin.interface?.displayName || plugin.displayName || plugin.id
}

export function pluginSubtitle(plugin: PluginEntry): string {
  return plugin.interface?.shortDescription || plugin.description || ''
}

export function pluginSourceLabel(plugin: PluginEntry): string {
  return plugin.interface?.developerName || plugin.source
}

const pluginText: CSSProperties = { display: 'flex', flexDirection: 'column', minWidth: 0, flex: 1 }
const iconShell: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  borderRadius: 8,
  overflow: 'hidden',
  color: '#fff',
  fontSize: 15,
  fontWeight: 700,
  flex: '0 0 auto'
}
function actionBackground(rowActive: boolean): string {
  return rowActive
    ? 'color-mix(in srgb, var(--text-primary) 9%, var(--bg-tertiary))'
    : 'var(--bg-tertiary)'
}

function tryAction(rowActive: boolean): CSSProperties {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    gap: 5,
    height: 28,
    padding: '0 9px',
    border: 'none',
    borderRadius: 8,
    backgroundColor: actionBackground(rowActive),
    color: 'var(--text-primary)',
    fontSize: 12,
    lineHeight: 1,
    whiteSpace: 'nowrap',
    cursor: 'pointer',
    transition: 'background-color 120ms ease'
  }
}

function iconAction(rowActive: boolean): CSSProperties {
  return {
    width: 30,
    height: 30,
    borderRadius: 999,
    backgroundColor: actionBackground(rowActive),
    color: 'var(--text-primary)',
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    transition: 'background-color 120ms ease'
  }
}
