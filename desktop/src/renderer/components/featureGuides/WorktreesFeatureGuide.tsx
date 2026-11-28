import { useEffect, type CSSProperties } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { normalizeGitPathKey } from '../../stores/gitStore'
import { useFeatureGuideStore } from '../../stores/featureGuideStore'
import { useThreadStore } from '../../stores/threadStore'
import { useWorkspaceProjectsStore } from '../../stores/workspaceProjectsStore'
import { isThreadRuntimeActive } from '../../utils/threadRuntime'
import { FeatureGuideCapture, FeatureGuideCoachmark, FeatureGuideMedia } from './FeatureGuide'
import rowsDark from '../../assets/featureGuides/worktrees-rows-dark.webp'
import rowsLight from '../../assets/featureGuides/worktrees-rows-light.webp'
import composerDark from '../../assets/featureGuides/worktrees-composer-dark.webp'
import composerLight from '../../assets/featureGuides/worktrees-composer-light.webp'

const ROWS_MEDIA = { light: rowsLight, dark: rowsDark }
const COMPOSER_MEDIA = { light: composerLight, dark: composerDark }
const GUIDE_STYLE: CSSProperties = { left: 0, bottom: 'calc(100% + 10px)', zIndex: 20 }
let eligibleMounts = 0

function useAnotherChatRunning(): boolean {
  return useThreadStore((state) => state.threadList.some((thread) =>
    !state.hiddenThreadIds.has(thread.id)
    && (state.runningTurnThreadIds.has(thread.id) || isThreadRuntimeActive(state.runtimeSnapshots.get(thread.id) ?? thread.runtime))))
}

export function WorktreesGuideCard({ onUse, onClose, style }: { onUse: () => void; onClose: () => void; style?: CSSProperties }): JSX.Element {
  const t = useT()
  return (
    <FeatureGuideCoachmark
      style={style}
      media={(
        <FeatureGuideMedia focus="20% 60%" height={168}>
          <FeatureGuideCapture source={ROWS_MEDIA} style={{ left: 16, top: 14, width: 240 }} />
          <FeatureGuideCapture source={COMPOSER_MEDIA} style={{ left: 16, top: 80, width: 400 }} />
        </FeatureGuideMedia>
      )}
      title={t('featureGuides.worktrees.title')}
      body={t('featureGuides.worktrees.body')}
      primaryLabel={t('featureGuides.worktrees.action')}
      onPrimary={onUse}
      onClose={onClose}
    />
  )
}

export function WorktreesFeatureGuide({ workspacePath, available, worktreeMode, onUseWorktree }: {
  workspacePath: string
  available: boolean
  worktreeMode: boolean
  onUseWorktree: () => void
}): JSX.Element | null {
  const foreground = useWorkspaceProjectsStore((state) => state.foregroundWorkspacePath)
  const sameProject = Boolean(workspacePath) && normalizeGitPathKey(workspacePath) === normalizeGitPathKey(foreground)
  const usedWorktree = useThreadStore((state) => state.threadList.some((thread) => thread.worktree != null))
  const anotherRunning = useAnotherChatRunning()
  const active = useFeatureGuideStore((state) => state.active === 'worktrees')
  const eligible = available && sameProject && !usedWorktree && anotherRunning

  useEffect(() => {
    if (worktreeMode || (sameProject && usedWorktree)) useFeatureGuideStore.getState().settle('worktrees')
  }, [sameProject, usedWorktree, worktreeMode])

  useEffect(() => {
    if (!eligible) return
    eligibleMounts += 1
    useFeatureGuideStore.getState().request('worktrees')
    return () => {
      eligibleMounts -= 1
      queueMicrotask(() => {
        const state = useFeatureGuideStore.getState()
        if (eligibleMounts === 0 && state.active === 'worktrees') state.close('worktrees', false)
      })
    }
  }, [eligible])

  if (!active || !eligible) return null
  const close = (acted: boolean): void => useFeatureGuideStore.getState().close('worktrees', acted)
  return (
    <WorktreesGuideCard
      style={GUIDE_STYLE}
      onUse={() => {
        close(true)
        onUseWorktree()
      }}
      onClose={() => close(false)}
    />
  )
}
