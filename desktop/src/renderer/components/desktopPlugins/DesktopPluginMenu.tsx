import type { MenuEntry, MenuProps } from '@dotcraft/plugin'
import { cloneElement, useRef, useState, type JSX, type MouseEvent, type ReactElement } from 'react'

import { ContextMenu, type ContextMenuEntry, type ContextMenuPosition } from '../ui/ContextMenu'

interface MenuTriggerProps {
  onClick?: (event: MouseEvent<HTMLElement>) => void
  'aria-haspopup'?: 'menu'
  'aria-expanded'?: boolean
}

function toContextMenuEntry(entry: MenuEntry): ContextMenuEntry {
  if ('type' in entry) return entry
  return {
    label: entry.label,
    onClick: entry.onSelect,
    icon: entry.icon,
    disabled: entry.disabled,
    danger: entry.danger
  }
}

export function DesktopPluginMenu({ items, children }: MenuProps): JSX.Element {
  const [open, setOpen] = useState<{ position: ContextMenuPosition; keyboard: boolean } | null>(null)
  const triggerRef = useRef<HTMLElement | null>(null)
  const trigger = children as ReactElement<MenuTriggerProps>

  const close = (): void => {
    if (open?.keyboard) triggerRef.current?.focus()
    setOpen(null)
  }

  return (
    <>
      {cloneElement(trigger, {
        'aria-haspopup': 'menu',
        'aria-expanded': open != null,
        onClick: (event: MouseEvent<HTMLElement>) => {
          trigger.props.onClick?.(event)
          if (event.defaultPrevented) return
          triggerRef.current = event.currentTarget
          const rect = event.currentTarget.getBoundingClientRect()
          setOpen({ position: { x: rect.left, y: rect.bottom + 4 }, keyboard: event.detail === 0 })
        }
      })}
      {open && (
        <ContextMenu
          items={items.map(toContextMenuEntry)}
          position={open.position}
          onClose={close}
          autoFocus={open.keyboard}
        />
      )}
    </>
  )
}
