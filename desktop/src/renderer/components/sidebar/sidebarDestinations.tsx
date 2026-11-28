import { useState, type MouseEvent as ReactMouseEvent } from 'react'
import { PinOff } from 'lucide-react'
import type { DesktopPluginContributionIcon } from '@dotcraft/plugin'
import type { OwnedThreadRecord } from '../../../shared/ownedThreads'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { prefetchCoreMainView } from '../../core/coreMainViewRoutes'
import { compareContribution, resolveDesktopPluginLabel, useDesktopPluginRegistry, type ActiveDesktopPluginSpace } from '../../plugins/desktopPluginRegistry'
import { useConnectionStore } from '../../stores/connectionStore'
import { useOwnedThreadsStore } from '../../stores/ownedThreadsStore'
import { useSidebarDestinationsStore } from '../../stores/sidebarDestinationsStore'
import { enterSpace, leaveSpace, useActiveSpace } from '../../stores/spaceStore'
import { useUIStore } from '../../stores/uiStore'
import { buildOwnedThreadMenuEntries } from '../../utils/ownedThreadLifecycle'
import { resolveDesktopPluginIcon } from '../desktopPlugins/DesktopPluginIcon'
import { ContextMenu, type ContextMenuEntry, type ContextMenuPosition } from '../ui/ContextMenu'
import { SidebarNavIcon } from './SidebarNavIcon'
import { ThreadStatusIndicator } from './ThreadStatusIndicator'

export interface SidebarDestination {
  id: string
  label: string
  icon: JSX.Element
  status?: JSX.Element
  active: boolean
  testId: string
  select: () => void
  prefetch?: () => void
  space?: ActiveDesktopPluginSpace
  ownedThread?: OwnedThreadRecord
}

const PINNED_BY_DEFAULT = new Set<string>(['agents', 'skills'])

export function useSidebarDestinations(): { pinned: SidebarDestination[]; more: SidebarDestination[] } {
  const t = useT()
  const locale = useLocale()
  const activeMainView = useUIStore((s) => s.activeMainView)
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const automationsAvailable = useConnectionStore((s) => s.capabilities?.automations === true)
  const desktopMainViews = useDesktopPluginRegistry((s) => s.mainViews)
  const spaces = useDesktopPluginRegistry((s) => s.spaces)
  const activeSpace = useActiveSpace()
  const ownedThreads = useOwnedThreadsStore((s) => s.records)
  const spaceViews = new Set(spaces.flatMap((space) => space.viewKeys))
  const pins = useSidebarDestinationsStore((s) => s.pins)

  const core = (view: 'agents' | 'automations' | 'skills', label: string): SidebarDestination => ({
    id: view,
    label,
    icon: <SidebarNavIcon name={view} />,
    active: activeMainView === view,
    testId: `nav-${view}`,
    select: () => setActiveMainView(view),
    prefetch: () => prefetchCoreMainView(view)
  })

  const pluginEntries = [
    ...spaces.map((space) => {
      const active = activeSpace?.space.spaceKey === space.spaceKey
      const owned = ownedThreads.find((record) => record.pluginId === space.pluginId && record.space === space.id)
      return {
        ...space,
        destination: {
          id: space.spaceKey,
          label: resolveDesktopPluginLabel(space.label, locale),
          icon: <DesktopPluginIcon icon={space.icon} />,
          status: owned && !active ? (
            <ThreadStatusIndicator
              threadId={owned.threadId}
              active={false}
              unreadVariant="count"
              includeLinkedChats
            />
          ) : undefined,
          active,
          testId: `nav-space-${space.pluginId}-${space.id}`,
          select: () => {
            if (active) void leaveSpace()
            else enterSpace(space)
          },
          space,
          ownedThread: owned
        }
      }
    }),
    ...desktopMainViews.filter((entry) => !spaceViews.has(entry.viewKey)).map((entry) => ({
      ...entry,
      destination: {
        id: entry.viewKey,
        label: resolveDesktopPluginLabel(entry.label, locale),
        icon: <DesktopPluginIcon icon={entry.icon} />,
        active: activeMainView === entry.viewKey,
        testId: `nav-desktop-plugin-${entry.pluginId}-${entry.id}`,
        select: () => setActiveMainView(entry.viewKey)
      }
    }))
  ].sort(compareContribution)
  const spaceKeys = new Set(spaces.map((space) => space.spaceKey))

  const destinations: SidebarDestination[] = [
    ...pluginEntries.map((entry) => entry.destination),
    core('agents', t('sidebar.agents')),
    ...(automationsAvailable ? [core('automations', t('sidebar.automations'))] : []),
    core('skills', t('sidebar.skills'))
  ]

  const isPinned = (id: string): boolean => pins[id] ?? (PINNED_BY_DEFAULT.has(id) || spaceKeys.has(id))
  return {
    pinned: destinations.filter((destination) => isPinned(destination.id)),
    more: destinations.filter((destination) => !isPinned(destination.id))
  }
}

export function useUnpinContextMenu(): {
  open: (event: ReactMouseEvent, destination: Pick<SidebarDestination, 'id' | 'ownedThread'>) => void
  menu: JSX.Element | null
} {
  const t = useT()
  const setPinned = useSidebarDestinationsStore((s) => s.setPinned)
  const ownedThreads = useOwnedThreadsStore((s) => s.records)
  const [target, setTarget] = useState<{ id: string; ownedThreadId?: string; position: ContextMenuPosition } | null>(null)

  const liveOwned = target?.ownedThreadId
    ? ownedThreads.find((record) => record.threadId === target.ownedThreadId)
    : undefined
  const items: ContextMenuEntry[] = target
    ? [
        ...(liveOwned
          ? [...buildOwnedThreadMenuEntries(liveOwned, t, () => setTarget(null)), { type: 'separator' as const }]
          : []),
        {
          label: t('sidebar.unpinFromSidebar'),
          icon: <PinOff size={14} aria-hidden />,
          onClick: () => setPinned(target.id, false)
        }
      ]
    : []

  return {
    open: (event, destination) => {
      event.preventDefault()
      setTarget({
        id: destination.id,
        ownedThreadId: destination.ownedThread?.threadId,
        position: { x: event.clientX, y: event.clientY }
      })
    },
    menu: target && (
      <ContextMenu
        position={target.position}
        onClose={() => setTarget(null)}
        items={items}
      />
    )
  }
}

export function DesktopPluginIcon({ icon }: { icon?: DesktopPluginContributionIcon | null }): JSX.Element {
  const Glyph = resolveDesktopPluginIcon(icon)
  return <Glyph size={16} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
}
