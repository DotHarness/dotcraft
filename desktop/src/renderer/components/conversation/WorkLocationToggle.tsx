import type { JSX } from 'react'
import { ArrowRightLeft, Laptop, Split } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { ActionTooltip } from '../ui/ActionTooltip'
import type { ComposerWorkspaceMode } from './ComposerWorkspaceFooter'
import styles from './WorkLocationToggle.module.css'

interface WorkLocationToggleProps {
  variant: 'welcome' | 'thread'
  mode: ComposerWorkspaceMode
  branchName: string | null
  canUseWorktrees: boolean
  loadingDisabled: boolean
  handoffDisabledReason: string | null
  onToggle: (targetMode: ComposerWorkspaceMode) => void
}

export function WorkLocationToggle({
  variant,
  mode,
  branchName,
  canUseWorktrees,
  loadingDisabled,
  handoffDisabledReason,
  onToggle
}: WorkLocationToggleProps): JSX.Element {
  const t = useT()
  const locationLabel = variant === 'welcome'
    ? (mode === 'worktree' ? t('workspaceFooter.newWorktree') : t('workspaceFooter.workLocally'))
    : (mode === 'worktree' ? t('workspaceFooter.worktree') : t('workspaceFooter.local'))
  const targetMode: ComposerWorkspaceMode = mode === 'local' ? 'worktree' : 'local'

  const blockedReason = loadingDisabled
    ? undefined
    : variant === 'thread' && handoffDisabledReason
      ? handoffDisabledReason
      : mode === 'local' && !canUseWorktrees
        ? t('workspaceFooter.worktreesUnavailable')
        : undefined
  const blocked = Boolean(blockedReason)
  const interactive = !loadingDisabled && !blocked

  const tooltipLabel = mode === 'local'
    ? (branchName ? t('workspaceFooter.workLocally.tooltip', { branch: branchName }) : locationLabel)
    : variant === 'welcome'
      ? t('workspaceFooter.newWorktree.tooltip')
      : (branchName ? t('workspaceFooter.worktree.tooltip', { branch: branchName }) : locationLabel)

  const tooltipDetail = targetMode === 'worktree'
    ? (variant === 'welcome' ? t('workspaceFooter.switchToWorktree') : t('workspaceFooter.clickToHandoffWorktree'))
    : (variant === 'welcome' ? t('workspaceFooter.switchToLocal') : t('workspaceFooter.clickToHandoffLocal'))

  return (
    <ActionTooltip
      label={loadingDisabled ? '' : tooltipLabel}
      detail={tooltipDetail}
      disabledReason={blockedReason}
      placement="top"
    >
      <button
        type="button"
        className={`dc-composer-pill ${styles.pill}`}
        aria-label={locationLabel}
        disabled={loadingDisabled}
        aria-disabled={blocked || undefined}
        data-blocked={blocked ? 'true' : undefined}
        onClick={() => {
          if (!blocked) onToggle(targetMode)
        }}
      >
        <span className={styles.iconSlot} aria-hidden>
          <span className={styles.iconRest}>
            {mode === 'worktree'
              ? <Split size={15} strokeWidth={1.8} />
              : <Laptop size={15} strokeWidth={1.8} />}
          </span>
          {interactive && (
            <span className={styles.iconHover}>
              <ArrowRightLeft size={14} strokeWidth={1.8} />
            </span>
          )}
        </span>
        <span className={styles.compactLabel}>{locationLabel}</span>
      </button>
    </ActionTooltip>
  )
}
