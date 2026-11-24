import { useEffect, useState, type JSX } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import { useConfigSetting } from '../../../../stores/configStore'
import { useThreadStore } from '../../../../stores/threadStore'
import { requestWorkspaceResource, type WorkspaceResourceScope } from '../../../../stores/workspaceResourceScope'
import { SettingsEmptyRow, SettingsGroup, SettingsRow } from '../../SettingsGroup'
import { settingsDescriptionStyle } from '../../settingsTypography'
import { useConfirmDialog } from '../../../ui/ConfirmDialog'
import { Button } from '../../../ui/Button'
import { Input } from '../../../ui/Input'
import { ScriptOutput } from '../../../worktreeCreation/ScriptOutput'
import { WorktreeLoadingRow } from './WorktreeLoadingRow'
import { removeAnyway, removeWorktree, type WorktreeRemoval, type WorktreeRemovalOutcome } from './worktreeRemoval'
import { refreshWorktreeList, useWorktreeList, type ListedWorktree } from './worktreeSettingsResource'
import styles from './WorktreeSettingsGroup.module.css'

const PREFIX_INPUT_ID = 'settings-worktree-branch-prefix'

function prefixError(value: string): boolean {
  return value.length === 0 || /\s/.test(value) || value.endsWith('/') || value.startsWith('/')
}

export function WorktreeSettingsGroup({ scope }: { scope: WorkspaceResourceScope }): JSX.Element {
  const t = useT()
  const confirm = useConfirmDialog()
  const prefix = useConfigSetting('Worktrees.BranchPrefix', (error) => t('settings.worktrees.saveFailed', { error }))
  const savedPrefix = typeof prefix.value === 'string' ? prefix.value : 'dotcraft'
  const [prefixDraft, setPrefixDraft] = useState(savedPrefix)
  const list = useWorktreeList(scope)
  const worktrees = list?.data ?? null
  const [removalError, setRemovalError] = useState<string | null>(null)
  const error = removalError ?? list?.error ?? null
  const [removingPath, setRemovingPath] = useState<string | null>(null)
  const [cleanupFailure, setCleanupFailure] = useState<Extract<WorktreeRemovalOutcome, { kind: 'cleanupFailed' }> | null>(null)
  const threads = useThreadStore((state) => state.threadList)

  useEffect(() => setPrefixDraft(savedPrefix), [savedPrefix])

  function request(method: 'worktree/remove', params: WorktreeRemoval): Promise<unknown> {
    return requestWorkspaceResource(scope, method, { ...params })
  }

  function commitPrefix(): void {
    const next = prefixDraft.trim()
    if (prefixError(next)) {
      setPrefixDraft(savedPrefix)
      return
    }
    if (next !== savedPrefix) void prefix.set(next)
  }

  async function remove(worktree: ListedWorktree): Promise<void> {
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
    setRemovalError(null)
    await window.api.devServers.stopFolder(path).catch(() => undefined)
    setCleanupFailure(null)
    const outcome = await run()
    setRemovingPath(null)
    if (outcome.kind === 'cleanupFailed') {
      setCleanupFailure(outcome)
    } else if (outcome.kind === 'failed') {
      setRemovalError(outcome.message)
    } else {
      await refreshWorktreeList(scope, path)
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
      {error && <SettingsEmptyRow tone="error">{error}</SettingsEmptyRow>}
      {worktrees === null && error === null && <WorktreeLoadingRow />}
      {worktrees?.length === 0 && error === null ? (
        <SettingsEmptyRow>{t('settings.worktrees.empty')}</SettingsEmptyRow>
      ) : worktrees?.map((worktree) => {
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

