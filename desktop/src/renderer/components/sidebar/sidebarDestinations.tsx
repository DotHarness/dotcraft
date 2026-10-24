import { useState, type MouseEvent as ReactMouseEvent } from 'react'
import { PinOff } from 'lucide-react'
import type { DesktopPluginContributionIcon } from '@dotcraft/plugin'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { prefetchCoreMainView } from '../../core/coreMainViewRoutes'
import { resolveDesktopPluginLabel, useDesktopPluginRegistry } from '../../plugins/desktopPluginRegistry'
import { useConnectionStore } from '../../stores/connectionStore'
import { useSidebarDestinationsStore } from '../../stores/sidebarDestinationsStore'
import { useUIStore } from '../../stores/uiStore'
import { resolveDesktopPluginIcon } from '../desktopPlugins/DesktopPluginIcon'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { SidebarNavIcon } from './SidebarNavIcon'

export interface SidebarDestination {
  id: string
  label: string
  icon: JSX.Element
  active: boolean
  testId: string
  select: () => void
  prefetch?: () => void
}

const PINNED_BY_DEFAULT = new Set<string>(['agents', 'skills'])

export function useSidebarDestinations(): { pinned: SidebarDestination[]; more: SidebarDestination[] } {
  const t = useT()
  const locale = useLocale()
  const activeMainView = useUIStore((s) => s.activeMainView)
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const automationsAvailable = useConnectionStore((s) => s.capabilities?.automations === true)
  const desktopMainViews = useDesktopPluginRegistry((s) => s.mainViews)
  const pins = useSidebarDestinationsStore((s) => s.pins)

  const core = (view: 'channels' | 'agents' | 'automations' | 'skills', label: string): SidebarDestination => ({
    id: view,
    label,
    icon: <SidebarNavIcon name={view} />,
    active: activeMainView === view,
    testId: `nav-${view}`,
    select: () => setActiveMainView(view),
    prefetch: () => prefetchCoreMainView(view)
  })

  const destinations: SidebarDestination[] = [
    core('channels', t('sidebar.channels')),
    ...desktopMainViews.map((entry) => ({
      id: entry.viewKey,
      label: resolveDesktopPluginLabel(entry.label, locale),
      icon: <DesktopPluginIcon icon={entry.icon} />,
      active: activeMainView === entry.viewKey,
      testId: `nav-desktop-plugin-${entry.pluginId}-${entry.id}`,
      select: () => setActiveMainView(entry.viewKey)
    })),
    core('agents', t('sidebar.agents')),
    ...(automationsAvailable ? [core('automations', t('sidebar.automations'))] : []),
    core('skills', t('sidebar.skills'))
  ]

  const isPinned = (id: string): boolean => pins[id] ?? PINNED_BY_DEFAULT.has(id)
  return {
    pinned: destinations.filter((destination) => isPinned(destination.id)),
    more: destinations.filter((destination) => !isPinned(destination.id))
  }
}

export function useUnpinContextMenu(): {
  open: (event: ReactMouseEvent, id: string) => void
  menu: JSX.Element | null
} {
  const t = useT()
  const setPinned = useSidebarDestinationsStore((s) => s.setPinned)
  const [target, setTarget] = useState<{ id: string; position: ContextMenuPosition } | null>(null)

  return {
    open: (event, id) => {
      event.preventDefault()
      setTarget({ id, position: { x: event.clientX, y: event.clientY } })
    },
    menu: target && (
      <ContextMenu
        position={target.position}
        onClose={() => setTarget(null)}
        items={[{
          label: t('sidebar.unpinFromSidebar'),
          icon: <PinOff size={14} aria-hidden />,
          onClick: () => setPinned(target.id, false)
        }]}
      />
    )
  }
}

export function DesktopPluginIcon({ icon }: { icon?: DesktopPluginContributionIcon | null }): JSX.Element {
  const Glyph = resolveDesktopPluginIcon(icon)
  return <Glyph size={16} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
}
