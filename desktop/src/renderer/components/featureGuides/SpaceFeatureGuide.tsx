import { useEffect, type CSSProperties } from 'react'
import { createPortal } from 'react-dom'
import type { DesktopPluginSpaceGuide } from '@dotcraft/plugin'
import { spaceFeatureGuideId } from '../../../shared/featureGuides'
import { useLocale } from '../../contexts/LocaleContext'
import { resolveDesktopPluginLabel, type ActiveDesktopPluginSpace } from '../../plugins/desktopPluginRegistry'
import { useConversationStore } from '../../stores/conversationStore'
import { useFeatureGuideStore } from '../../stores/featureGuideStore'
import { enterSpace } from '../../stores/spaceStore'
import { useTransientOverlayStore } from '../../stores/transientOverlayStore'
import { useUIStore } from '../../stores/uiStore'
import { DesktopPluginContributionBoundary } from '../desktopPlugins/DesktopPluginContributionBoundary'
import { FeatureGuideCoachmark, FeatureGuideMedia } from './FeatureGuide'
import styles from './FeatureGuide.module.css'

export function SpaceGuideCard({ guide, onPrimary, onClose, style, className }: {
  guide: DesktopPluginSpaceGuide
  onPrimary: () => void
  onClose: () => void
  style?: CSSProperties
  className?: string
}): JSX.Element {
  const locale = useLocale()
  const Media = guide.media
  return (
    <FeatureGuideCoachmark
      className={className}
      style={style}
      media={(
        <FeatureGuideMedia focus="90% 60%" height={168}>
          <div className={styles.spaceMedia} inert>
            <DesktopPluginContributionBoundary identity={guide.title.default} fallback={null}>
              <Media />
            </DesktopPluginContributionBoundary>
          </div>
        </FeatureGuideMedia>
      )}
      title={resolveDesktopPluginLabel(guide.title, locale)}
      body={resolveDesktopPluginLabel(guide.body, locale)}
      primaryLabel={resolveDesktopPluginLabel(guide.action, locale)}
      onPrimary={onPrimary}
      onClose={onClose}
    />
  )
}

export function SpaceFeatureGuide({ space, anchorName }: { space: ActiveDesktopPluginSpace; anchorName: string }): JSX.Element | null {
  const id = spaceFeatureGuideId(space.spaceKey)
  const requested = useFeatureGuideStore((state) => state.spaceRequests.get(id) === space.host)
  const enabled = useFeatureGuideStore((state) => state.settings?.enabled === true)
  const activeId = useFeatureGuideStore((state) => state.active)
  const idle = useConversationStore((state) => state.turnStatus === 'idle')
  const sidebarAvailable = useUIStore((state) => !state.sidebarCollapsed && !state.settingsOpen)
  const overlayOpen = useTransientOverlayStore((state) => state.topDepth > 0)
  const eligible = enabled && idle && sidebarAvailable && !overlayOpen && (activeId === id || (activeId === null && requested))

  useEffect(() => {
    if (!eligible) return
    const closeIfActive = (): void => {
      const state = useFeatureGuideStore.getState()
      if (state.active === id) state.close(id, false)
    }
    const sync = (): void => {
      if (document.hidden) closeIfActive()
      else useFeatureGuideStore.getState().request(id)
    }
    let mounted = true
    queueMicrotask(() => { if (mounted) sync() })
    document.addEventListener('visibilitychange', sync)
    return () => {
      mounted = false
      document.removeEventListener('visibilitychange', sync)
      closeIfActive()
    }
  }, [eligible, id, space.host])

  if (!eligible || activeId !== id || !space.guide) return null
  const close = (acted: boolean): void => useFeatureGuideStore.getState().close(id, acted)
  return createPortal(
    <SpaceGuideCard
      className={styles.anchoredSpace}
      style={{ positionAnchor: anchorName }}
      guide={space.guide}
      onPrimary={() => { close(true); enterSpace(space) }}
      onClose={() => close(false)}
    />,
    document.body
  )
}
