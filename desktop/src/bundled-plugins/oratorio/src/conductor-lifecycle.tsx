import { useEffect, useState } from 'react'
import { ActionTooltip } from '@dotcraft/plugin'
import type {
  DesktopPluginHost,
  DesktopPluginOwnedThread,
  DesktopPluginSurfaceWrapperProps,
  MenuEntry
} from '@dotcraft/plugin'
import { Archive, Pause, Play } from 'lucide-react'
import { CONDUCTOR_KEY, type ConductorProactiveController } from './conductor-proactive'
import { translateOratorio, useOratorioI18n } from './oratorio-i18n'
import './conductor-paused.css'

export interface ConductorLifecycleHooks {
  readonly proactive?: Pick<ConductorProactiveController, 'runCheckIn' | 'reset'>
}

export function useConductorThread(host: DesktopPluginHost): DesktopPluginOwnedThread | null {
  const [thread, setThread] = useState<DesktopPluginOwnedThread | null>(() => host.threads.findOwned(CONDUCTOR_KEY))
  useEffect(() => {
    setThread(host.threads.findOwned(CONDUCTOR_KEY))
    return host.threads.onOwnedChange(CONDUCTOR_KEY, setThread)
  }, [host])
  return thread
}

export async function toggleConductorPaused(
  host: DesktopPluginHost,
  conductor: DesktopPluginOwnedThread,
  hooks?: ConductorLifecycleHooks
): Promise<DesktopPluginOwnedThread | null> {
  const nextPaused = !conductor.paused
  try {
    const updated = await host.threads.setOwnedPaused(CONDUCTOR_KEY, nextPaused)
    if (conductor.paused && updated && !updated.paused) {
      void hooks?.proactive?.runCheckIn('resume')
    }
    return updated
  } catch (error) {
    host.ui.showToast({
      message: translateOratorio(host.environment.locale, 'conductorActionFailed', {
        error: error instanceof Error ? error.message : String(error)
      }),
      tone: 'error'
    })
    return null
  }
}

export async function confirmAndArchiveConductor(
  host: DesktopPluginHost,
  hooks?: ConductorLifecycleHooks
): Promise<boolean> {
  const locale = host.environment.locale
  const confirmed = await host.ui.confirm({
    title: translateOratorio(locale, 'conductorArchiveConfirmTitle'),
    message: translateOratorio(locale, 'conductorArchiveConfirmMessage'),
    confirmLabel: translateOratorio(locale, 'conductorArchiveConfirmAction'),
    cancelLabel: translateOratorio(locale, 'cancel'),
    danger: true
  })
  if (!confirmed) return false
  try {
    const archived = await host.threads.archiveOwned(CONDUCTOR_KEY)
    if (archived) hooks?.proactive?.reset()
    return archived
  } catch (error) {
    host.ui.showToast({
      message: translateOratorio(locale, 'conductorActionFailed', {
        error: error instanceof Error ? error.message : String(error)
      }),
      tone: 'error'
    })
    return false
  }
}

export function buildConductorMenuEntries(
  host: DesktopPluginHost,
  hooks?: ConductorLifecycleHooks
): MenuEntry[] {
  const conductor = host.threads.findOwned(CONDUCTOR_KEY)
  if (!conductor) return []
  const locale = host.environment.locale
  return [
    {
      label: translateOratorio(locale, conductor.paused ? 'conductorResume' : 'conductorPause'),
      icon: conductor.paused ? <Play size={14} aria-hidden /> : <Pause size={14} aria-hidden />,
      onSelect: () => {
        void toggleConductorPaused(host, conductor, hooks)
      }
    },
    {
      label: translateOratorio(locale, 'conductorArchive'),
      icon: <Archive size={14} aria-hidden />,
      danger: true,
      onSelect: () => {
        void confirmAndArchiveConductor(host, hooks)
      }
    }
  ]
}

export function createConductorComposerGate(hooks?: ConductorLifecycleHooks) {
  return function ConductorComposerGate({
    host,
    context,
    children
  }: DesktopPluginSurfaceWrapperProps<'composer'>): JSX.Element {
    const conductor = useConductorThread(host)
    const { t } = useOratorioI18n()
    if (!conductor || !conductor.paused || context.threadId !== conductor.threadId) {
      return <>{children}</>
    }
    return (
      <div className="ora-paused-composer" data-testid="conductor-paused-bar">
        <div className="ora-paused-composer__hidden" aria-hidden="true">
          {children}
        </div>
        <div className="ora-paused-composer__overlay">
          <ActionTooltip label={t('conductorResume')}>
            <button
              type="button"
              className="ora-paused-composer__button"
              aria-label={t('conductorResume')}
              data-testid="conductor-paused-bar-resume"
              onClick={() => {
                void toggleConductorPaused(host, conductor, hooks)
              }}
            >
              <Pause size={14} strokeWidth={2.2} className="ora-paused-composer__icon" aria-hidden />
              <span>{t('conductorPaused')}</span>
            </button>
          </ActionTooltip>
        </div>
      </div>
    )
  }
}
