import { useState, type CSSProperties } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import { SidebarIcon } from '../ui/AppIcons'
import { DotCraftLogo } from '../ui/DotCraftLogo'
import { IconButton } from '../ui/IconButton'
import { ACTION_SHORTCUTS } from '../ui/shortcutKeys'
import { cancelSidebarPeekOpen, openSidebarPeekSoon } from './sidebarPeek'

export function SidebarToggleButton({ showBrand = false, style }: {
  showBrand?: boolean
  style?: CSSProperties
}): JSX.Element {
  const t = useT()
  const collapsed = useUIStore((s) => s.sidebarCollapsed)
  const toggleSidebar = useUIStore((s) => s.toggleSidebar)
  const [hovered, setHovered] = useState(false)
  const label = collapsed ? t('sidebar.expandAria') : t('sidebar.collapseAria')
  const icon = showBrand && collapsed && !hovered
    ? <DotCraftLogo size={18} />
    : <SidebarIcon collapsed={collapsed} />

  return (
    <IconButton
      data-sidebar-toggle=""
      size={28}
      label={label}
      tooltipLabel={label}
      shortcut={ACTION_SHORTCUTS.toggleSidebar}
      tooltipPlacement={collapsed ? 'right' : 'bottom'}
      onClick={toggleSidebar}
      onPointerEnter={() => {
        setHovered(true)
        if (collapsed) openSidebarPeekSoon()
      }}
      onPointerLeave={() => {
        setHovered(false)
        cancelSidebarPeekOpen()
      }}
      style={style}
      icon={icon}
    />
  )
}
