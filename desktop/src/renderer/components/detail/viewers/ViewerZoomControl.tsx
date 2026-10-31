import { useState } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { ContextMenu, type ContextMenuPosition } from '../../ui/ContextMenu'
import { ZOOM_PRESETS } from './viewerZoom'
import './viewer-zoom.css'

const MENU_HEIGHT_ESTIMATE = 220

export function ViewerZoomControl({
  zoom,
  onZoom,
  onFit,
  fitLabel
}: {
  zoom: number
  onZoom: (zoom: number) => void
  onFit: () => void
  fitLabel?: string
}): JSX.Element {
  const t = useT()
  const [menu, setMenu] = useState<ContextMenuPosition | null>(null)
  const percent = Math.round(zoom * 100)
  return (
    <>
      <button
        type="button"
        className="dc-viewer-zoom"
        aria-haspopup="menu"
        aria-expanded={menu !== null}
        aria-label={t('viewer.zoom.label', { percent })}
        onClick={(event) => {
          const rect = event.currentTarget.getBoundingClientRect()
          setMenu({ x: rect.left, y: rect.top - MENU_HEIGHT_ESTIMATE > 0 ? rect.top - MENU_HEIGHT_ESTIMATE : rect.bottom + 4 })
        }}
      >
        {percent}%
      </button>
      {menu && (
        <ContextMenu
          position={menu}
          width={160}
          autoFocus
          onClose={() => setMenu(null)}
          items={[
            ...ZOOM_PRESETS.map((preset) => ({
              label: `${preset * 100}%`,
              selection: 'radio' as const,
              checked: Math.abs(preset - zoom) < 0.001,
              onClick: () => onZoom(preset)
            })),
            { type: 'separator' as const },
            { label: fitLabel ?? t('viewer.zoom.fit'), onClick: onFit }
          ]}
        />
      )}
    </>
  )
}
