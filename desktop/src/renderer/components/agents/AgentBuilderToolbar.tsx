import { useState } from 'react'
import { ArrowLeft, Eye, Pencil, Plus, Trash2 } from 'lucide-react'
import { useLocale } from '../../contexts/LocaleContext'
import { formatRelativeTime } from '../../utils/relativeTime'
import { Button } from '../ui/Button'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { IconButton } from '../ui/IconButton'
import { MoreActionsButton } from '../ui/MoreActionsButton'
import './AgentBuilderToolbar.css'

interface AgentBuilderToolbarProps {
  created: boolean
  updatedAt?: string | null
  autoSaveState: 'idle' | 'saving' | 'saved' | 'error'
  preview: boolean
  nameMissing: boolean
  onBack: () => void
  onDelete: () => void
  onCreate: () => void
  onTogglePreview: () => void
}

export function AgentBuilderToolbar({ created, updatedAt, autoSaveState, preview, nameMissing, onBack, onDelete, onCreate, onTogglePreview }: AgentBuilderToolbarProps) {
  const locale = useLocale()
  const [menuPosition, setMenuPosition] = useState<ContextMenuPosition | null>(null)

  return <header className="agent-builder-edit-head">
    <div className="agent-builder-edit-left">
      <IconButton label="Back" tooltipLabel="Back" tooltipPlacement="bottom" icon={<ArrowLeft size={16} />} size={28} onClick={onBack} />
    </div>
    <div className="agent-builder-edit-right">
      {created && <span className={`agent-builder-autosave${autoSaveState === 'error' ? ' is-error' : ''}`}>
        {autoSaveState === 'saving' ? 'Saving…' : autoSaveState === 'error' ? 'Save failed' : updatedAt ? `Updated ${formatRelativeTime(updatedAt, new Date(), locale)}` : 'Saved'}
      </span>}
      <Button size="toolbar" variant="secondary" iconLeft={preview ? <Pencil size={14} /> : <Eye size={14} />} onClick={onTogglePreview}>
        {preview ? 'Edit' : 'Preview'}
      </Button>
      {created ? <>
        <MoreActionsButton label="More actions" size={28} open={menuPosition != null} onClick={(event) => {
          const rect = event.currentTarget.getBoundingClientRect()
          setMenuPosition({ x: rect.right - 160, y: rect.bottom + 4 })
        }} />
        {menuPosition && <ContextMenu position={menuPosition} onClose={() => setMenuPosition(null)} items={[
          { label: 'Delete', icon: <Trash2 size={15} />, danger: true, onClick: () => { setMenuPosition(null); onDelete() } }
        ]} />}
      </> : <Button size="toolbar" variant="primary" iconLeft={<Plus size={14} />} disabled={nameMissing} onClick={onCreate}>Create</Button>}
    </div>
  </header>
}
