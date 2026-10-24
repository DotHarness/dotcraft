import { useRef, type JSX } from 'react'
import { ChevronDown, CircleOff, Layers, Plus, Settings2 } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { openEnvironmentSettings } from '../../stores/appNavigationStore'
import { useWorktreeEnvironments } from '../projectActions/worktreeEnvironments'
import { FooterMenuButton, FooterMenuDivider, WorkspaceFooterPill, menuStyle } from './composerFooterPrimitives'
import { ComposerOverlapBand, useComposerOverlapBandHeight } from './useComposerOverlapBand'
import css from './ComposerWorkspaceFooter.module.css'
import styles from './WorktreeEnvironmentPicker.module.css'

const GLYPH = { size: 14, strokeWidth: 1.8, 'aria-hidden': true } as const

export function WorktreeEnvironmentPicker({
  workspacePath,
  busy,
  open,
  onOpenChange
}: {
  workspacePath: string
  busy: boolean
  open: boolean
  onOpenChange: (open: boolean) => void
}): JSX.Element {
  const t = useT()
  const result = useWorktreeEnvironments(workspacePath || null)
  const menuRef = useRef<HTMLDivElement>(null)
  const menuOpen = open && !busy && result != null
  const bandHeight = useComposerOverlapBandHeight(menuRef, menuOpen)
  const environments = result?.environments ?? []
  const selected = environments.find((environment) => environment.id === result?.selectedEnvironmentId) ?? null
  const label = result?.unavailable
    ? t('worktreeEnvironment.unavailable')
    : selected?.name ?? t('worktreeEnvironment.noneShort')

  const select = (environmentId: string | null): void => {
    onOpenChange(false)
    void window.api.projectActions.selectWorktreeEnvironment({ workspacePath, environmentId }).catch(() => {})
  }

  const openSettings = (): void => {
    onOpenChange(false)
    openEnvironmentSettings(environments.length > 0
      ? { projectPath: workspacePath, ...(selected ? { environmentId: selected.id } : {}) }
      : { projectPath: workspacePath, create: true })
  }

  return (
    <div style={{ position: 'relative' }}>
      <WorkspaceFooterPill
        aria-label={`${t('worktreeEnvironment.label')}: ${label}`}
        disabled={busy || result == null}
        open={menuOpen}
        onClick={() => onOpenChange(!open)}
      >
        <Layers size={15} strokeWidth={1.8} aria-hidden />
        <span className={css.compactLabel} style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{label}</span>
        <ChevronDown size={14} strokeWidth={1.8} aria-hidden />
      </WorkspaceFooterPill>
      {menuOpen && (
        <div ref={menuRef} style={menuStyle}>
          <ComposerOverlapBand height={bandHeight} radius={10} />
          <FooterMenuButton
            icon={<CircleOff {...GLYPH} />}
            checked={selected === null && !result.unavailable}
            onClick={() => select(null)}
          >
            <span className={styles.name}>{t('worktreeEnvironment.none')}</span>
          </FooterMenuButton>
          {environments.map((environment) => (
            <FooterMenuButton
              key={environment.id}
              icon={<Layers {...GLYPH} />}
              checked={environment.id === selected?.id}
              disabled={Boolean(environment.error) && environment.id !== selected?.id}
              onClick={() => select(environment.id)}
            >
              <span className={styles.name}>{environment.name}</span>
              {environment.error ? (
                <span className={styles.note}>{t('settings.environments.needsAttention')}</span>
              ) : environment.isDefault ? (
                <span className={styles.note}>{t('worktreeEnvironment.default')}</span>
              ) : null}
            </FooterMenuButton>
          ))}
          <FooterMenuDivider />
          <FooterMenuButton
            icon={environments.length > 0 ? <Settings2 {...GLYPH} /> : <Plus {...GLYPH} />}
            onClick={openSettings}
          >
            <span className={styles.name}>
              {environments.length > 0 ? t('worktreeEnvironment.settings') : t('worktreeEnvironment.setUp')}
            </span>
          </FooterMenuButton>
        </div>
      )}
    </div>
  )
}
