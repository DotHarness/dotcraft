import { useState } from 'react'
import { Settings, Trash2 } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { MoreActionsButton } from '../ui/MoreActionsButton'

export function PluginDetailActionsMenu({
  onManage,
  onRemove
}: {
  onManage?: () => void
  onRemove?: () => void
}): JSX.Element | null {
  const t = useT()
  const [position, setPosition] = useState<ContextMenuPosition | null>(null)
  if (!onManage && !onRemove) return null

  return (
    <>
      <MoreActionsButton
        label={t('plugins.moreActions')}
        open={position != null}
        onClick={(event) => {
          event.stopPropagation()
          const rect = event.currentTarget.getBoundingClientRect()
          setPosition({ x: rect.right - 200, y: rect.bottom + 4 })
        }}
      />
      {position && (
        <ContextMenu
          position={position}
          onClose={() => setPosition(null)}
          items={[
            ...(onManage ? [{ label: t('plugins.manage'), icon: <Settings size={14} />, onClick: onManage }] : []),
            ...(onRemove ? [{ label: t('plugins.uninstall'), icon: <Trash2 size={14} />, danger: true, onClick: onRemove }] : [])
          ]}
        />
      )}
    </>
  )
}
