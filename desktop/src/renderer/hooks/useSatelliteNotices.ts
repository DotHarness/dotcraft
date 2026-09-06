import { useEffect, useRef } from 'react'
import { translate, type AppLocale } from '../../shared/locales'
import type { Satellite, SatelliteEvent } from '../../shared/satellites'
import { useLocale } from '../contexts/LocaleContext'
import { useConnectionStore } from '../stores/connectionStore'
import { onSatelliteEvent } from '../stores/satellitesStore'
import { useThreadRouteStore } from '../stores/threadRouteStore'
import { useThreadStore } from '../stores/threadStore'
import { showToast } from '../stores/toastStore'

/**
 * Satellite events carry no invitation id, so an arrival is announced only while this
 * Desktop still holds an invitation it minted and has not seen expire.
 */
async function invitedFromThisDesktop(): Promise<boolean> {
  const created = (await window.api.settings.get()).createdSatelliteInviteIds ?? []
  const now = Date.now()
  return created.some((entry) => Date.parse(entry.expiresAt) > now)
}

/** The folder a newly joined machine could run this thread's work in, when there is one. */
function runnableWorkspaceId(satellite: Satellite | undefined): string | null {
  if (!satellite?.connected) return null
  const free = satellite.workspaces.find((workspace) => !workspace.busy)
  return free?.workspaceId ?? null
}

function capitalize(value: string): string {
  return value.replace(/^./, (first) => first.toUpperCase())
}

function announceArrival(event: SatelliteEvent, locale: AppLocale): void {
  const satellite = event.satellite
  const name = satellite?.displayName ?? event.peerId
  const threadId = useThreadStore.getState().activeThreadId
  const workspaceId = runnableWorkspaceId(satellite)
  const routable =
    threadId != null &&
    workspaceId != null &&
    useConnectionStore.getState().capabilities?.remoteToolHost === true

  showToast({
    type: 'info',
    key: `satellite-joined-${event.peerId}`,
    message: translate(locale, 'satellite.toast.joined.message', { name }),
    description: translate(locale, 'satellite.toast.joined.description', {
      user: capitalize(satellite?.userName ?? name)
    }),
    leading: { fallback: name },
    ...(routable
      ? {
          action: {
            label: translate(locale, 'satellite.toast.joined.action'),
            onClick: () => {
              void useThreadRouteStore
                .getState()
                .connect(threadId, event.peerId, workspaceId)
                .catch(() => {})
            }
          }
        }
      : {})
  })
}

/**
 * The two satellite notices Desktop owns: a machine joining through an invitation this
 * Desktop minted, and a join link no Satellite runtime could be handed.
 */
export function useSatelliteNotices(): void {
  const locale = useLocale()
  const localeRef = useRef(locale)
  localeRef.current = locale

  useEffect(() => {
    const unsubscribeEvent = onSatelliteEvent((event) => {
      if (event.kind !== 'joined') return
      void invitedFromThisDesktop()
        .then((invited) => {
          if (invited) announceArrival(event, localeRef.current)
        })
        .catch(() => {})
    })
    const unsubscribeJoinLink = window.api.satellites.onJoinLink((link) => {
      if (link.forwarded) return
      showToast({
        type: 'info',
        key: 'satellite-join-link',
        message: translate(localeRef.current, 'satellite.toast.joinLink.message'),
        description: translate(localeRef.current, 'satellite.toast.joinLink.description')
      })
    })
    return () => {
      unsubscribeEvent()
      unsubscribeJoinLink()
    }
  }, [])
}
