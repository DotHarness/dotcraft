import type { CSSProperties } from 'react'
import { useUIStore } from '../../stores/uiStore'
import { SidebarToggleButton } from './SidebarToggleButton'
import css from './MacSidebarTitleBar.module.css'

const noDrag: CSSProperties = { WebkitAppRegion: 'no-drag' }

export function MacSidebarTitleBar(): JSX.Element | null {
  const collapsed = useUIStore((s) => s.sidebarCollapsed)
  if (!collapsed) return null
  return (
    <div
      className={css.bar}
      onDoubleClick={(event) => {
        if (event.target !== event.currentTarget) return
        void window.api.window.toggleMaximize()
      }}
    >
      <SidebarToggleButton style={noDrag} />
    </div>
  )
}
