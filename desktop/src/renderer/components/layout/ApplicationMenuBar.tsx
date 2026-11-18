import { useCallback, useEffect, useRef, useState, type KeyboardEvent as ReactKeyboardEvent, type MouseEvent as ReactMouseEvent, type Ref } from 'react'
import { Menu } from 'lucide-react'

import type { ApplicationMenuEntry, ApplicationMenuSnapshot } from '../../../shared/applicationMenu'
import { TOP_LEVEL_MENU_IDS, type TopLevelMenuId } from '../../../shared/locales'
import { useT } from '../../contexts/LocaleContext'
import { ContextMenu, type ContextMenuEntry, type ContextMenuPosition } from '../ui/ContextMenu'
import css from './ApplicationMenuBar.module.css'

const MENU_LABEL_KEY: Record<TopLevelMenuId, string> = {
  file: 'menu.file',
  edit: 'menu.edit',
  view: 'menu.view',
  window: 'menu.window',
  help: 'menu.help'
}

type OpenMenuId = TopLevelMenuId | 'all'

interface OpenMenu {
  id: OpenMenuId
  position: ContextMenuPosition
  autoFocus: boolean
}

interface ApplicationMenuBarProps {
  menusRef: Ref<HTMLDivElement>
  collapsed: boolean
}

export function ApplicationMenuBar({ menusRef, collapsed }: ApplicationMenuBarProps): JSX.Element {
  const t = useT()
  const [openMenu, setOpenMenu] = useState<OpenMenu | null>(null)
  const [snapshot, setSnapshot] = useState<ApplicationMenuSnapshot | null>(null)
  const openMenuRef = useRef<OpenMenu | null>(null)
  const triggerRefs = useRef(new Map<OpenMenuId, HTMLButtonElement>())
  const restoreFocusRef = useRef<HTMLElement | null>(null)
  openMenuRef.current = openMenu

  const open = useCallback((id: OpenMenuId, autoFocus: boolean) => {
    if (!openMenuRef.current) {
      restoreFocusRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null
      setSnapshot(null)
      void window.api.menu.getApplicationMenu().then(setSnapshot)
    }
    setOpenMenu({ id, position: menuPosition(triggerRefs.current.get(id)!), autoFocus })
  }, [])

  const close = useCallback(() => {
    setOpenMenu(null)
    const target = restoreFocusRef.current
    restoreFocusRef.current = null
    target?.focus()
  }, [])

  const invoke = useCallback((commandId: number) => {
    restoreFocusRef.current?.focus()
    void window.api.menu.invokeItem(commandId)
  }, [])

  useEffect(() => {
    const id = openMenu?.id
    if (!id || id === 'all') return
    const handleKeyDown = (event: KeyboardEvent): void => {
      if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return
      event.preventDefault()
      const index = TOP_LEVEL_MENU_IDS.indexOf(id)
      const step = event.key === 'ArrowRight' ? 1 : -1
      open(TOP_LEVEL_MENU_IDS[(index + step + TOP_LEVEL_MENU_IDS.length) % TOP_LEVEL_MENU_IDS.length], true)
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [open, openMenu?.id])

  useEffect(() => {
    if (!openMenu) return
    window.addEventListener('blur', close)
    return () => window.removeEventListener('blur', close)
  }, [close, openMenu])

  const triggerProps = (id: OpenMenuId) => ({
    ref: (element: HTMLButtonElement | null) => {
      if (element) triggerRefs.current.set(id, element)
      else triggerRefs.current.delete(id)
    },
    type: 'button' as const,
    'aria-haspopup': 'menu' as const,
    'aria-expanded': openMenu?.id === id,
    'data-open': openMenu?.id === id || undefined,
    onMouseDown: (event: ReactMouseEvent<HTMLButtonElement>) => {
      if (event.button !== 0) return
      event.preventDefault()
      if (openMenuRef.current?.id === id) close()
      else open(id, false)
    },
    onMouseEnter: () => {
      const current = openMenuRef.current
      if (current && current.id !== 'all' && id !== 'all' && current.id !== id) open(id, false)
    },
    onKeyDown: (event: ReactKeyboardEvent<HTMLButtonElement>) => {
      if (event.key !== 'Enter' && event.key !== ' ' && event.key !== 'ArrowDown') return
      event.preventDefault()
      open(id, true)
    }
  })

  const label = (id: TopLevelMenuId): string => t(MENU_LABEL_KEY[id])
  const items = openMenu && snapshot ? menuItems(openMenu.id, snapshot, label, invoke) : null

  return (
    <>
      <div
        ref={menusRef}
        className={css.menus}
        data-application-menu-bar
        data-collapsed={collapsed || undefined}
        aria-hidden={collapsed || undefined}
        inert={collapsed}
      >
        {TOP_LEVEL_MENU_IDS.map((id) => (
          <button key={id} className={css.trigger} {...triggerProps(id)}>
            {label(id)}
          </button>
        ))}
      </div>

      {collapsed && (
        <div className={css.menus} data-application-menu-bar>
          <button className={css.trigger} {...triggerProps('all')}>
            <Menu size={15} aria-hidden="true" />
            {t('menu.all')}
          </button>
        </div>
      )}

      {openMenu && items && (
        <ContextMenu
          key={openMenu.id}
          items={items}
          position={openMenu.position}
          width={openMenu.id === 'view' ? 260 : 220}
          autoFocus={openMenu.autoFocus}
          ignoreOutsidePress={isMenuBarPress}
          onClose={close}
        />
      )}
    </>
  )
}

function isMenuBarPress(event: MouseEvent): boolean {
  return event.target instanceof Element && event.target.closest('[data-application-menu-bar]') !== null
}

function menuPosition(trigger: HTMLElement): ContextMenuPosition {
  const rect = trigger.getBoundingClientRect()
  // Open below the title bar so the menu never overlaps the window drag region.
  const bar = trigger.closest('.dotcraft-custom-menu-bar')?.getBoundingClientRect()
  return { x: rect.left, y: bar?.bottom ?? rect.bottom }
}

function menuItems(
  id: OpenMenuId,
  snapshot: ApplicationMenuSnapshot,
  label: (id: TopLevelMenuId) => string,
  invoke: (commandId: number) => void
): ContextMenuEntry[] {
  if (id !== 'all') return toContextMenuEntries(snapshot[id], invoke)
  return TOP_LEVEL_MENU_IDS.map((menuId) => ({
    label: label(menuId),
    onClick: () => {},
    submenu: toContextMenuEntries(snapshot[menuId], invoke)
  }))
}

function toContextMenuEntries(entries: ApplicationMenuEntry[], invoke: (commandId: number) => void): ContextMenuEntry[] {
  return entries.map((entry) => entry.type === 'separator'
    ? { type: 'separator' }
    : {
        label: entry.label,
        shortcut: entry.accelerator,
        disabled: !entry.enabled,
        onClick: () => invoke(entry.commandId)
      })
}
