import { useState } from 'react'
import { ArrowLeft, Eye, Pencil, Plus, Trash2 } from 'lucide-react'
import { useLocale, useT } from '../../contexts/LocaleContext'
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
  const t = useT()
  const [menuPosition, setMenuPosition] = useState<ContextMenuPosition | null>(null)

  return <header className="agent-builder-edit-head">
    <div className="agent-builder-edit-left">
      <IconButton label={t('agentBuilder.toolbar.back')} tooltipLabel={t('agentBuilder.toolbar.back')} tooltipPlacement="bottom" icon={<ArrowLeft size={16} />} size={28} onClick={onBack} />
    </div>
    <div className="agent-builder-edit-right">
      {created && <span className={`agent-builder-autosave${autoSaveState === 'error' ? ' is-error' : ''}`}>
        {autoSaveState === 'saving'
          ? t('agentBuilder.toolbar.saving')
          : autoSaveState === 'error'
            ? t('agentBuilder.toolbar.saveFailed')
            : updatedAt
              ? t('agentBuilder.toolbar.updated', { time: formatRelativeTime(updatedAt, new Date(), locale) })
              : t('agentBuilder.toolbar.saved')}
      </span>}
      <Button size="toolbar" variant="secondary" iconLeft={preview ? <Pencil size={14} /> : <Eye size={14} />} onClick={onTogglePreview}>
        {preview ? t('agentBuilder.toolbar.edit') : t('agentBuilder.toolbar.preview')}
      </Button>
      {created ? <>
        <MoreActionsButton label={t('agentBuilder.toolbar.moreActions')} size={28} open={menuPosition != null} onClick={(event) => {
          const rect = event.currentTarget.getBoundingClientRect()
          setMenuPosition({ x: rect.right - 160, y: rect.bottom + 4 })
        }} />
        {menuPosition && <ContextMenu position={menuPosition} onClose={() => setMenuPosition(null)} items={[
          { label: t('agentBuilder.toolbar.delete'), icon: <Trash2 size={15} />, danger: true, onClick: () => { setMenuPosition(null); onDelete() } }
        ]} />}
      </> : <Button size="toolbar" variant="primary" iconLeft={<Plus size={14} />} disabled={nameMissing} onClick={onCreate}>{t('agentBuilder.toolbar.create')}</Button>}
    </div>
  </header>
}
