/**
 * Empty-state launcher for the detail panel — shown when no system tab and no
 * viewer tab is open. A neutral 2×2 card grid that opens the panel's tab types,
 * dispatching through the same action handler as the "+" add-tab menu so there
 * is no duplicated open logic.
 *
 * Chrome stays neutral per the desktop visual-design spec (§7); the lucide glyph
 * is the only accent. Browser / Terminal cards are disabled without an active
 * thread + workspace, mirroring the add-tab menu's `canOpenWorkspaceTab` guard.
 */
import type { CSSProperties } from 'react'
import { FilePlus2, FolderOpen, Globe, SquareTerminal } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { ACTION_SHORTCUTS, formatShortcutParts } from '../ui/shortcutKeys'
import type { AddTabMenuAction } from '../../../shared/addTabMenu'

interface DetailPanelLauncherProps {
  /** Dispatches the same actions as the "+" add-tab menu. */
  onAction: (action: AddTabMenuAction) => void
  /** Whether browser/terminal tabs can be created (needs an active thread + workspace). */
  canOpenWorkspaceTab: boolean
}

interface LauncherCardSpec {
  action: AddTabMenuAction
  title: string
  description: string
  icon: JSX.Element
  shortcut?: string
  enabled: boolean
}

export function DetailPanelLauncher({ onAction, canOpenWorkspaceTab }: DetailPanelLauncherProps): JSX.Element {
  const t = useT()
  const iconStyle: CSSProperties = { display: 'block' }
  const fmt = (spec: typeof ACTION_SHORTCUTS[keyof typeof ACTION_SHORTCUTS]): string =>
    formatShortcutParts(spec).join('+')

  const cards: LauncherCardSpec[] = [
    {
      action: 'openFile',
      title: t('detailPanel.launcherFilesTitle'),
      description: t('detailPanel.launcherFilesDesc'),
      icon: <FolderOpen size={22} strokeWidth={1.75} aria-hidden style={iconStyle} />,
      shortcut: fmt(ACTION_SHORTCUTS.quickOpen),
      enabled: true
    },
    {
      action: 'newBrowser',
      title: t('detailPanel.launcherBrowserTitle'),
      description: t('detailPanel.launcherBrowserDesc'),
      icon: <Globe size={22} strokeWidth={1.75} aria-hidden style={iconStyle} />,
      shortcut: fmt(ACTION_SHORTCUTS.newBrowserTab),
      enabled: canOpenWorkspaceTab
    },
    {
      action: 'newChanges',
      title: t('detailPanel.launcherReviewTitle'),
      description: t('detailPanel.launcherReviewDesc'),
      icon: <FilePlus2 size={22} strokeWidth={1.75} aria-hidden style={iconStyle} />,
      shortcut: fmt(ACTION_SHORTCUTS.viewChanges),
      enabled: true
    },
    {
      action: 'newTerminal',
      title: t('detailPanel.launcherTerminalTitle'),
      description: t('detailPanel.launcherTerminalDesc'),
      icon: <SquareTerminal size={22} strokeWidth={1.75} aria-hidden style={iconStyle} />,
      shortcut: fmt(ACTION_SHORTCUTS.newTerminalTab),
      enabled: canOpenWorkspaceTab
    }
  ]

  return (
    <div style={containerStyle}>
      <div style={gridStyle}>
        {cards.map((card) => (
          <LauncherCardButton key={card.action} card={card} onAction={onAction} />
        ))}
      </div>
    </div>
  )
}

function LauncherCardButton({
  card,
  onAction
}: {
  card: LauncherCardSpec
  onAction: (action: AddTabMenuAction) => void
}): JSX.Element {
  return (
    <button
      type="button"
      disabled={!card.enabled}
      aria-label={card.title}
      onClick={() => {
        if (card.enabled) onAction(card.action)
      }}
      style={{
        ...cardStyle,
        cursor: card.enabled ? 'pointer' : 'default',
        opacity: card.enabled ? 1 : 0.45
      }}
      onMouseEnter={(e) => {
        if (!card.enabled) return
        ;(e.currentTarget as HTMLButtonElement).style.background = 'var(--bg-tertiary)'
        ;(e.currentTarget as HTMLButtonElement).style.borderColor = 'var(--border-active)'
      }}
      onMouseLeave={(e) => {
        ;(e.currentTarget as HTMLButtonElement).style.background = 'var(--bg-secondary)'
        ;(e.currentTarget as HTMLButtonElement).style.borderColor = 'var(--border-default)'
      }}
    >
      <span style={cardIconStyle}>{card.icon}</span>
      <span style={cardTitleStyle}>{card.title}</span>
      <span style={cardDescStyle}>{card.description}</span>
      {card.shortcut && <span style={shortcutChipStyle}>{card.shortcut}</span>}
    </button>
  )
}

const containerStyle: CSSProperties = {
  height: '100%',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: '24px',
  boxSizing: 'border-box',
  overflow: 'auto'
}

const gridStyle: CSSProperties = {
  display: 'grid',
  gridTemplateColumns: 'repeat(2, minmax(0, 1fr))',
  gap: '12px',
  width: '100%',
  maxWidth: '460px'
}

const cardStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  alignItems: 'center',
  justifyContent: 'center',
  gap: '6px',
  minHeight: '128px',
  padding: '20px 16px',
  border: '1px solid var(--border-default)',
  borderRadius: '8px',
  background: 'var(--bg-secondary)',
  color: 'var(--text-primary)',
  textAlign: 'center',
  font: 'inherit',
  transition: 'background-color 100ms ease, border-color 100ms ease'
}

const cardIconStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  color: 'var(--text-secondary)',
  marginBottom: '2px'
}

const cardTitleStyle: CSSProperties = {
  fontSize: '13px',
  fontWeight: 600,
  color: 'var(--text-primary)'
}

const cardDescStyle: CSSProperties = {
  fontSize: '12px',
  color: 'var(--text-secondary)'
}

const shortcutChipStyle: CSSProperties = {
  marginTop: '4px',
  padding: '1px 6px',
  borderRadius: '5px',
  background: 'var(--bg-tertiary)',
  color: 'var(--text-secondary)',
  fontSize: '11px',
  lineHeight: '16px'
}
