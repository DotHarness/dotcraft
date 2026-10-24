import { useCallback, useEffect, useState, type CSSProperties, type JSX } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import { useConfigSetting } from '../../../../stores/configStore'
import { useThreadStore } from '../../../../stores/threadStore'
import { SettingsGroup, SettingsRow } from '../../SettingsGroup'
import { settingsDescriptionStyle } from '../../settingsTypography'
import { useConfirmDialog } from '../../../ui/ConfirmDialog'
import { Button } from '../../../ui/Button'
import { Input } from '../../../ui/Input'
import { ScriptOutput } from '../../../worktreeCreation/ScriptOutput'
import { removeAnyway, removeWorktree, type WorktreeRemovalOutcome } from './worktreeRemoval'
import styles from './WorktreeSettingsGroup.module.css'

interface WorktreeStatusWire {
  threadId?: string | null
  path: string
  branchName: string
  hasUncommittedChanges?: boolean
  aheadCount?: number
}

const PREFIX_INPUT_ID = 'settings-worktree-branch-prefix'

function prefixError(value: string): boolean {
  return value.length === 0 || /\s/.test(value) || value.endsWith('/') || value.startsWith('/')
}

export function WorktreeSettingsGroup({ workspacePath }: { workspacePath?: string }): JSX.Element {
  const t = useT()
  const confirm = useConfirmDialog()
  const prefix = useConfigSetting('Worktrees.BranchPrefix', (error) => t('settings.worktrees.saveFailed', { error }))
  const savedPrefix = typeof prefix.value === 'string' ? prefix.value : 'dotcraft'
  const [prefixDraft, setPrefixDraft] = useState(savedPrefix)
  const [worktrees, setWorktrees] = useState<WorktreeStatusWire[]>([])
  const [loadError, setLoadError] = useState<string | null>(null)
  const [removingPath, setRemovingPath] = useState<string | null>(null)
  const [cleanupFailure, setCleanupFailure] = useState<Extract<WorktreeRemovalOutcome, { kind: 'cleanupFailed' }> | null>(null)
  const threads = useThreadStore((state) => state.threadList)

  useEffect(() => setPrefixDraft(savedPrefix), [savedPrefix])

  const reload = useCallback(async () => {
    try {
      const result = await window.api.appServer.sendRequest('worktree/list', {}) as { data?: WorktreeStatusWire[] }
      setWorktrees(result.data ?? [])
      setLoadError(null)
    } catch (err) {
      setLoadError(err instanceof Error ? err.message : String(err))
    }
    // workspacePath only re-triggers a reload when the foreground workspace changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [workspacePath])

  useEffect(() => { void reload() }, [reload])

  function commitPrefix(): void {
    const next = prefixDraft.trim()
    if (prefixError(next)) {
      setPrefixDraft(savedPrefix)
      return
    }
    if (next !== savedPrefix) void prefix.set(next)
  }

  async function remove(worktree: WorktreeStatusWire): Promise<void> {
    const hasWork = worktree.hasUncommittedChanges === true || (worktree.aheadCount ?? 0) > 0
    if (hasWork) {
      const ok = await confirm({
        title: t('settings.worktrees.removeConfirmTitle'),
        message: t('settings.worktrees.removeConfirmMessage', { branch: worktree.branchName }),
        confirmLabel: t('settings.worktrees.remove'),
        danger: true
      })
      if (!ok) return
    }

    await settleRemoval(worktree.path, () => removeWorktree(request, { path: worktree.path, discardChanges: hasWork }))
  }

  async function settleRemoval(path: string, run: () => Promise<WorktreeRemovalOutcome>): Promise<void> {
    setRemovingPath(path)
    setCleanupFailure(null)
    const outcome = await run()
    setRemovingPath(null)
    if (outcome.kind === 'cleanupFailed') {
      setCleanupFailure(outcome)
    } else if (outcome.kind === 'failed') {
      setLoadError(outcome.message)
    } else {
      await reload()
    }
  }

  return (
    <SettingsGroup>
      <SettingsRow
        label={t('settings.worktrees.branchPrefix.label')}
        description={t('settings.worktrees.branchPrefix.description')}
        htmlFor={PREFIX_INPUT_ID}
        control={
          <Input
            id={PREFIX_INPUT_ID}
            value={prefixDraft}
            disabled={prefix.pending}
            aria-invalid={prefixError(prefixDraft.trim())}
            style={{ width: '200px' }}
            onChange={(event) => setPrefixDraft(event.target.value)}
            onBlur={commitPrefix}
            onKeyDown={(event) => {
              if (event.key === 'Enter') commitPrefix()
            }}
          />
        }
      />
      {loadError && <SettingsRow><span style={errorStyle}>{loadError}</span></SettingsRow>}
      {worktrees.length === 0 && !loadError ? (
        <SettingsRow><span style={settingsDescriptionStyle(false)}>{t('settings.worktrees.empty')}</span></SettingsRow>
      ) : worktrees.map((worktree) => {
        const owner = worktree.threadId ? threads.find((thread) => thread.id === worktree.threadId) : undefined
        const inUse = owner != null && owner.status !== 'archived'
        const details = [
          owner?.displayName ?? (worktree.threadId ? t('settings.worktrees.archivedConversation') : t('settings.worktrees.unused')),
          worktree.hasUncommittedChanges ? t('settings.worktrees.uncommitted') : null,
          (worktree.aheadCount ?? 0) > 0 ? t('settings.worktrees.newCommits', { count: worktree.aheadCount ?? 0 }) : null
        ].filter(Boolean).join(' · ')
        const failure = cleanupFailure?.removal.path === worktree.path ? cleanupFailure : null
        return [
          <SettingsRow
            key={worktree.path}
            label={worktree.branchName}
            description={details}
            control={inUse ? (
              <span style={settingsDescriptionStyle(false)}>{t('settings.worktrees.inUse')}</span>
            ) : (
              <Button
                disabled={removingPath === worktree.path}
                onClick={() => { void remove(worktree) }}
              >
                {t('settings.worktrees.remove')}
              </Button>
            )}
          />,
          failure ? (
            <SettingsRow
              key={`${worktree.path}:cleanup`}
              orientation="block"
              label={t('settings.worktrees.cleanupFailed')}
              description={failure.failure.exitCode === undefined
                ? t('settings.worktrees.cleanupFailedDescription')
                : t('settings.worktrees.cleanupExitCode', { code: failure.failure.exitCode })}
              control={failure.failure.outputTail ? <ScriptOutput text={failure.failure.outputTail} /> : undefined}
            >
              <div className={styles.cleanupActions}>
                <Button
                  variant="danger"
                  disabled={removingPath === worktree.path}
                  onClick={() => { void settleRemoval(worktree.path, () => removeAnyway(request, failure)) }}
                >
                  {t('settings.worktrees.removeAnyway')}
                </Button>
              </div>
            </SettingsRow>
          ) : null
        ]
      })}
    </SettingsGroup>
  )
}

function request(method: 'worktree/remove', params: { path: string; discardChanges: boolean; skipCleanup?: boolean }): Promise<unknown> {
  return window.api.appServer.sendRequest(method, params)
}

const errorStyle: CSSProperties = {
  color: 'var(--error)',
  fontSize: '13px'
}
