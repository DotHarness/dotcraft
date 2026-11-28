import { useEffect, useRef, useState } from 'react'
import { BellRing, FolderGit2, MessagesSquare } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import {
  ORATORIO_GUIDE_MIN_PROJECTS,
  countRecentlyOpenedProjects,
  spaceFeatureGuideId,
  type FeatureGuideId
} from '../../../shared/featureGuides'
import {
  buildDesktopPluginContributionKey,
  useDesktopPluginRegistry,
  type ActiveDesktopPluginSpace
} from '../../plugins/desktopPluginRegistry'
import { useConnectionStore } from '../../stores/connectionStore'
import { useConversationStore } from '../../stores/conversationStore'
import { useFeatureGuideStore } from '../../stores/featureGuideStore'
import { operationFailureMessage, usePluginStore } from '../../stores/pluginStore'
import { enterSpace } from '../../stores/spaceStore'
import { addToast } from '../../stores/toastStore'
import { useUIStore } from '../../stores/uiStore'
import { useWorkspaceProjectsStore } from '../../stores/workspaceProjectsStore'
import { FeatureGuideCapture, FeatureGuideMedia, FeatureGuideSpotlight } from './FeatureGuide'
import oratorioDark from '../../assets/featureGuides/oratorio-dark.webp'
import oratorioLight from '../../assets/featureGuides/oratorio-light.webp'

const ORATORIO_PLUGIN_ID = 'oratorio'
const ORATORIO_SPACE_KEY = buildDesktopPluginContributionKey(ORATORIO_PLUGIN_ID, 'conductor')
// Same id as the plugin's own space guide, so both share one record.
export const ORATORIO_GUIDE_ID: FeatureGuideId = spaceFeatureGuideId(ORATORIO_SPACE_KEY)
const ORATORIO_MEDIA = { light: oratorioLight, dark: oratorioDark }
const ACTIVATION_TIMEOUT_MS = 20_000

function findOratorioSpace(): ActiveDesktopPluginSpace | null {
  return useDesktopPluginRegistry.getState().spaces.find((space) => space.spaceKey === ORATORIO_SPACE_KEY) ?? null
}

function waitForOratorioSpace(): Promise<ActiveDesktopPluginSpace> {
  const ready = findOratorioSpace()
  if (ready) return Promise.resolve(ready)
  return new Promise((resolve, reject) => {
    const timer = window.setTimeout(() => {
      stop()
      reject(new Error('timeout'))
    }, ACTIVATION_TIMEOUT_MS)
    const stop = useDesktopPluginRegistry.subscribe(() => {
      const space = findOratorioSpace()
      if (!space) return
      window.clearTimeout(timer)
      stop()
      resolve(space)
    })
  })
}

type OratorioCatalogState = 'unknown' | 'installed' | 'available' | 'absent'

function useOratorioCatalogState(): OratorioCatalogState {
  return usePluginStore((state) => {
    if (!state.hasCompleteCatalog) return 'unknown'
    const entry = state.plugins.find((plugin) => plugin.id === ORATORIO_PLUGIN_ID)
    if (!entry) return 'absent'
    // Installed but disabled is a deliberate no, and a running plugin guides itself.
    if (entry.installed) return 'installed'
    return entry.installable ? 'available' : 'absent'
  })
}

// A project switch arms the check, which runs once the new workspace has settled.
function useOratorioGuideSignal(): void {
  const foreground = useWorkspaceProjectsStore((state) => state.foregroundWorkspacePath)
  const recentProjects = useWorkspaceProjectsStore((state) => countRecentlyOpenedProjects(
    state.projects.filter((project) => project.kind === 'local').map((project) => project.lastOpenedAt),
    Date.now()
  ))
  const catalog = useOratorioCatalogState()
  const connected = useConnectionStore((state) => state.status === 'connected')
  const idle = useConversationStore((state) => state.turnStatus === 'idle')
  const settingsOpen = useUIStore((state) => state.settingsOpen)
  const guidesOn = useFeatureGuideStore((state) => state.settings?.enabled === true && state.active === null)
  const previous = useRef('')
  const [armed, setArmed] = useState(false)

  useEffect(() => {
    if (previous.current !== '' && foreground !== '' && foreground !== previous.current) setArmed(true)
    if (foreground) previous.current = foreground
  }, [foreground])

  useEffect(() => {
    if (!armed || !connected) return
    if (catalog === 'unknown') {
      if (usePluginStore.getState().catalogStatus !== 'pending') void usePluginStore.getState().fetchPlugins().catch(() => {})
      return
    }
    if (catalog !== 'available' || findOratorioSpace()) {
      setArmed(false)
      return
    }
    if (!guidesOn || !idle || settingsOpen || recentProjects < ORATORIO_GUIDE_MIN_PROJECTS) return
    setArmed(false)
    useFeatureGuideStore.getState().request(ORATORIO_GUIDE_ID)
  }, [armed, catalog, connected, guidesOn, idle, recentProjects, settingsOpen])
}

export function OratorioInstallGuideCard({ busy, onInstall, onClose }: {
  busy: boolean
  onInstall: () => void
  onClose: () => void
}): JSX.Element {
  const t = useT()
  return (
    <FeatureGuideSpotlight
      media={(
        <FeatureGuideMedia focus="60% 40%" height={208}>
          <FeatureGuideCapture source={ORATORIO_MEDIA} style={{ left: 24, top: 24, width: 600 }} />
        </FeatureGuideMedia>
      )}
      title={t('featureGuides.oratorio.title')}
      body={t('featureGuides.oratorio.body')}
      points={[
        { icon: <MessagesSquare size={16} />, title: t('featureGuides.oratorio.ask'), body: t('featureGuides.oratorio.askBody') },
        { icon: <FolderGit2 size={16} />, title: t('featureGuides.oratorio.projects'), body: t('featureGuides.oratorio.projectsBody') },
        { icon: <BellRing size={16} />, title: t('featureGuides.oratorio.report'), body: t('featureGuides.oratorio.reportBody') }
      ]}
      primaryLabel={t('featureGuides.oratorio.action')}
      primaryBusy={busy}
      onPrimary={onInstall}
      onClose={onClose}
    />
  )
}

export function OratorioFeatureGuide(): JSX.Element | null {
  const t = useT()
  useOratorioGuideSignal()
  const active = useFeatureGuideStore((state) => state.active === ORATORIO_GUIDE_ID)
  const spaceLoaded = useDesktopPluginRegistry((state) => state.spaces.some((space) => space.spaceKey === ORATORIO_SPACE_KEY))
  const [busy, setBusy] = useState(false)
  // Once the plugin runs, its pinned entry carries the guide instead.
  if (!active || (spaceLoaded && !busy)) return null

  const close = (acted: boolean): void => useFeatureGuideStore.getState().close(ORATORIO_GUIDE_ID, acted)
  const install = async (): Promise<void> => {
    setBusy(true)
    try {
      const result = await usePluginStore.getState().installPlugin(ORATORIO_PLUGIN_ID)
      const failure = result.outcome === 'notApplied' ? operationFailureMessage(result) ?? t('plugins.installFailed') : null
      if (failure) {
        addToast(failure, 'error')
        close(false)
        return
      }
      const space = await waitForOratorioSpace()
      close(true)
      enterSpace(space)
    } catch {
      addToast(t('plugins.installFailed'), 'error')
      close(false)
    } finally {
      setBusy(false)
    }
  }
  return <OratorioInstallGuideCard busy={busy} onInstall={() => { void install() }} onClose={() => { if (!busy) close(false) }} />
}
