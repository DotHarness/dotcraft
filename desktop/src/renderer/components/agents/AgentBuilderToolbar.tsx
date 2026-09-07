import { useEffect, useRef, useState } from 'react'
import { ArrowLeft, Eye, MoreHorizontal, Pencil, Plus, Trash2 } from 'lucide-react'
import { useLocale } from '../../contexts/LocaleContext'
import { formatRelativeTime } from '../../utils/relativeTime'
import { Button } from '../ui/Button'
import { IconButton } from '../ui/IconButton'
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
  const [menuOpen, setMenuOpen] = useState(false)
  const menuRef = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (!menuOpen) return
    const onDown = (event: MouseEvent) => {
      if (!menuRef.current?.contains(event.target as Node)) setMenuOpen(false)
    }
    document.addEventListener('mousedown', onDown, true)
    return () => document.removeEventListener('mousedown', onDown, true)
  }, [menuOpen])

  return <header className="agent-builder-edit-head">
    <div className="agent-builder-edit-left">
      <IconButton label="Back" icon={<ArrowLeft size={18} />} size={30} onClick={onBack} />
    </div>
    <div className="agent-builder-edit-right">
      {created && <span className={`agent-builder-autosave${autoSaveState === 'error' ? ' is-error' : ''}`}>
        {autoSaveState === 'saving' ? 'Saving…' : autoSaveState === 'error' ? 'Save failed' : updatedAt ? `Updated ${formatRelativeTime(updatedAt, new Date(), locale)}` : 'Saved'}
      </span>}
      <Button size="toolbar" variant="secondary" iconLeft={preview ? <Pencil size={14} /> : <Eye size={14} />} onClick={onTogglePreview}>
        {preview ? 'Edit' : 'Preview'}
      </Button>
      {created ? <div className="agent-builder-menu" ref={menuRef}>
        <IconButton label="More actions" icon={<MoreHorizontal size={18} />} size={30} aria-expanded={menuOpen} onClick={() => setMenuOpen(value => !value)} />
        {menuOpen && <div className="agent-builder-menu-pop" role="menu">
          <button type="button" className="agent-builder-menu-item is-danger" role="menuitem" onClick={() => { setMenuOpen(false); onDelete() }}>
            <Trash2 size={15} /> Delete
          </button>
        </div>}
      </div> : <Button size="toolbar" variant="primary" iconLeft={<Plus size={14} />} disabled={nameMissing} onClick={onCreate}>Create</Button>}
    </div>
  </header>
}
