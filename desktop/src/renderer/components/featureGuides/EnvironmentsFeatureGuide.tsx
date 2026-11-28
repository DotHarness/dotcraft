import { useEffect, type CSSProperties } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { normalizeGitPathKey } from '../../stores/gitStore'
import { useFeatureGuideStore } from '../../stores/featureGuideStore'
import { useThreadStore } from '../../stores/threadStore'
import { useWorkspaceProjectsStore } from '../../stores/workspaceProjectsStore'
import { FeatureGuideCapture, FeatureGuideCoachmark, FeatureGuideMedia } from './FeatureGuide'
import environmentsDark from '../../assets/featureGuides/environments-dark.webp'
import environmentsLight from '../../assets/featureGuides/environments-light.webp'

const MEDIA = { light: environmentsLight, dark: environmentsDark }
const GUIDE_STYLE: CSSProperties = { left: 0, bottom: 'calc(100% + 10px)', zIndex: 20 }
let eligibleMounts = 0

export function EnvironmentsGuideCard({ onSetUp, onClose, style }: { onSetUp: () => void; onClose: () => void; style?: CSSProperties }): JSX.Element {
  const t = useT()
  return (
    <FeatureGuideCoachmark
      style={style}
      media={(
        <FeatureGuideMedia focus="30% 60%" height={150}>
          <FeatureGuideCapture source={MEDIA} style={{ left: 16, top: 22, width: 330 }} />
        </FeatureGuideMedia>
      )}
      title={t('featureGuides.environments.title')}
      body={t('featureGuides.environments.body')}
      primaryLabel={t('featureGuides.environments.action')}
      onPrimary={onSetUp}
      onClose={onClose}
    />
  )
}

export function EnvironmentsFeatureGuide({ workspacePath, available, environmentCount, onSetUp }: {
  workspacePath: string
  available: boolean
  environmentCount: number
  onSetUp: () => void
}): JSX.Element | null {
  const foreground = useWorkspaceProjectsStore((state) => state.foregroundWorkspacePath)
  const sameProject = Boolean(workspacePath) && normalizeGitPathKey(workspacePath) === normalizeGitPathKey(foreground)
  const usedWorktree = useThreadStore((state) => state.threadList.some((thread) => thread.worktree != null))
  const active = useFeatureGuideStore((state) => state.active === 'environments')
  const eligible = available && sameProject && usedWorktree && environmentCount === 0

  useEffect(() => {
    if (environmentCount > 0) useFeatureGuideStore.getState().settle('environments')
  }, [environmentCount])

  useEffect(() => {
    if (!eligible) return
    eligibleMounts += 1
    useFeatureGuideStore.getState().request('environments')
    return () => {
      eligibleMounts -= 1
      queueMicrotask(() => {
        const state = useFeatureGuideStore.getState()
        if (eligibleMounts === 0 && state.active === 'environments') state.close('environments', false)
      })
    }
  }, [eligible])

  if (!active || !eligible) return null
  const close = (acted: boolean): void => useFeatureGuideStore.getState().close('environments', acted)
  return (
    <EnvironmentsGuideCard
      style={GUIDE_STYLE}
      onSetUp={() => {
        close(true)
        onSetUp()
      }}
      onClose={() => close(false)}
    />
  )
}
