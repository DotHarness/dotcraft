import { useEffect, useState, type JSX } from 'react'
import { CheckCircle2, Circle, GitBranch, XCircle } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { openEnvironmentSettings } from '../../stores/appNavigationStore'
import { addToast } from '../../stores/toastStore'
import {
  WORKTREE_CREATION_STEPS,
  useWorktreeCreationStore,
  type WorktreeCreation,
  type WorktreeCreationStep,
  type WorktreeCreationStepStatus
} from '../../stores/worktreeCreationStore'
import { startWorktreeSetupRepair } from '../../utils/worktreeSetupRepair'
import { Button, type ButtonVariant } from '../ui/Button'
import { DisclosureChevron } from '../ui/DisclosureChevron'
import { Spinner } from '../ui/Spinner'
import { ScriptOutput } from './ScriptOutput'
import styles from './WorktreeCreationCard.module.css'

interface CardAction {
  key: string
  label: string
  variant: ButtonVariant
  disabled?: boolean
  loading?: boolean
  onClick(): void
}

const ICON = { size: 16, strokeWidth: 1.8, 'aria-hidden': true } as const

function settle(promise: Promise<unknown>): void {
  promise.catch(() => {})
}

function StepIcon({ status }: { status: WorktreeCreationStepStatus }): JSX.Element {
  if (status === 'started') return <span className={styles.icon}><Spinner size={16} /></span>
  if (status === 'failed') return <span className={`${styles.icon} ${styles.failedIcon}`}><XCircle {...ICON} /></span>
  if (status === 'pending') return <span className={styles.icon}><Circle {...ICON} /></span>
  return <span className={styles.icon}><CheckCircle2 {...ICON} /></span>
}

export function WorktreeCreationCard({ creation }: { creation: WorktreeCreation }): JSX.Element {
  const t = useT()
  const [repairing, setRepairing] = useState(false)
  const store = useWorktreeCreationStore.getState
  const { anchorId, environment, phase, steps } = creation
  const failed = phase === 'failed'
  const [expanded, setExpanded] = useState(failed)
  useEffect(() => setExpanded(failed), [failed])
  const output = creation.output || creation.setup?.outputTail || ''

  const stepLabel = (step: WorktreeCreationStep): string => {
    if (step !== 'setupEnvironment') return t(`worktreeCreation.step.${step}`)
    return !environment || steps.setupEnvironment === 'skipped'
      ? t('worktreeCreation.step.setupSkipped')
      : t('worktreeCreation.step.setupEnvironment', { environment: environment.name })
  }
  const visibleSteps = WORKTREE_CREATION_STEPS.filter((step) => step !== 'copyFiles' || steps.copyFiles !== 'skipped')

  const failure = creation.error
    ?? (creation.setup?.exitCode === undefined
      ? t('worktreeCreation.failed')
      : t('worktreeCreation.exitCode', { code: creation.setup.exitCode }))

  const actions: CardAction[] = failed
    ? [
        ...(environment ? [{
          key: 'edit',
          label: t('worktreeCreation.editEnvironment'),
          variant: 'outline' as const,
          onClick: () => openEnvironmentSettings({ projectPath: creation.workspacePath, environmentId: environment.id })
        }, {
          key: 'repair',
          label: t('worktreeCreation.fixWithAgent'),
          variant: 'outline' as const,
          loading: repairing,
          onClick: () => {
            setRepairing(true)
            startWorktreeSetupRepair(creation, t('worktreeCreation.repairThreadTitle'))
              .catch((error: unknown) => addToast(
                t('worktreeCreation.repairFailed', { error: error instanceof Error ? error.message : String(error) }),
                'error'
              ))
              .finally(() => setRepairing(false))
          }
        }] : []),
        {
          key: 'retry',
          label: t('worktreeCreation.retry'),
          variant: 'secondary',
          onClick: () => settle(store().retry(anchorId))
        },
        {
          key: 'continue',
          label: t('worktreeCreation.continueAnyway'),
          variant: 'primary',
          onClick: () => store().continueAnyway(anchorId)
        }
      ]
    : phase === 'retrying'
      ? [{
          key: 'stop',
          label: t('worktreeCreation.stopSetup'),
          variant: 'ghost',
          onClick: () => settle(store().cancel(anchorId))
        }]
      : [
          {
            key: 'local',
            label: t('worktreeCreation.withoutWorktree'),
            variant: 'ghost',
            disabled: phase === 'cancelling',
            onClick: () => settle(store().createWithoutWorktree(anchorId))
          },
          {
            key: 'cancel',
            label: t('worktreeCreation.cancel'),
            variant: 'ghost',
            loading: phase === 'cancelling',
            onClick: () => settle(store().cancel(anchorId))
          }
        ]

  const title = failed ? t('worktreeCreation.failedTitle') : t('worktreeCreation.title')

  return (
    <section className={styles.card} aria-label={title} aria-busy={!failed}>
      <div className={styles.title}>
        <GitBranch size={14} strokeWidth={1.8} aria-hidden />
        <span>{title}</span>
      </div>
      <div className={styles.box}>
        <ul className={styles.steps}>
          {visibleSteps.map((step) => (
            <li key={step} className={styles.step} data-status={steps[step]}>
              <StepIcon status={steps[step]} />
              <span className={styles.stepLabel}>{stepLabel(step)}</span>
            </li>
          ))}
        </ul>
        {failed ? <p className={styles.failure} role="alert">{failure}</p> : null}
        {expanded && output ? <ScriptOutput text={output} /> : null}
        <div className={styles.footer}>
          {output ? (
            <Button
              variant="ghost"
              size="sm"
              aria-expanded={expanded}
              onClick={() => setExpanded((value) => !value)}
            >
              {expanded ? t('worktreeCreation.hideDetails') : t('worktreeCreation.showDetails')}
              <DisclosureChevron expanded={expanded} direction="reveal" />
            </Button>
          ) : <span />}
          <div className={styles.actions}>
            {actions.map((action) => (
              <Button
                key={action.key}
                variant={action.variant}
                size="sm"
                disabled={action.disabled}
                loading={action.loading}
                onClick={action.onClick}
              >
                {action.label}
              </Button>
            ))}
          </div>
        </div>
      </div>
    </section>
  )
}
