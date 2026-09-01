import { useMemo } from 'react'
import type { CSSProperties } from 'react'
import {
  closestCenter,
  DndContext,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent
} from '@dnd-kit/core'
import {
  arrayMove,
  SortableContext,
  useSortable,
  verticalListSortingStrategy
} from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { CornerDownRight, GripVertical, ListChecks, Pencil, Trash2 } from 'lucide-react'
import { Button } from '../ui/Button'
import { RunningSpinner } from '../ui/RunningSpinner'
import { useT } from '../../contexts/LocaleContext'
import type { QueuedTurnInput } from '../../types/conversation'
import { ActionTooltip } from '../ui/ActionTooltip'
import { IconButton } from '../ui/IconButton'

const EMPTY_QUEUED_INPUTS: QueuedTurnInput[] = []

interface QueuedInputDockProps {
  queuedInputs?: QueuedTurnInput[]
  onQueueSteer?: (id: string) => void
  onQueueRemove?: (id: string) => void
  onQueueEdit?: (id: string) => void
  onQueueReorder?: (orderedQueuedInputIds: string[]) => void
  editingQueuedInputId?: string | null
}

/** The inputs queued behind the running turn, shown above the composer. */
export function QueuedInputDock({
  queuedInputs = EMPTY_QUEUED_INPUTS,
  onQueueSteer,
  onQueueRemove,
  onQueueEdit,
  onQueueReorder,
  editingQueuedInputId = null
}: QueuedInputDockProps): JSX.Element | null {
  const t = useT()
  const queuedIds = useMemo(() => queuedInputs.map((item) => item.id), [queuedInputs])
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } })
  )

  if (queuedInputs.length === 0) return null

  const title = t(
    queuedInputs.length === 1 ? 'composer.queueDockTitleOne' : 'composer.queueDockTitleMany',
    { count: queuedInputs.length }
  )

  const handleDragEnd = (event: DragEndEvent): void => {
    if (!onQueueReorder) return
    const activeId = String(event.active.id)
    const overId = event.over?.id ? String(event.over.id) : ''
    if (!overId || activeId === overId) return
    const oldIndex = queuedInputs.findIndex((item) => item.id === activeId)
    const newIndex = queuedInputs.findIndex((item) => item.id === overId)
    if (oldIndex < 0 || newIndex < 0) return
    onQueueReorder(arrayMove(queuedIds, oldIndex, newIndex))
  }

  return (
    <div data-testid="subagent-dock" style={dockFrameStyle}>
      <div style={dockHeaderStyle}>
        <div style={headerStaticStyle}>
          <DockTitle title={title} />
        </div>
      </div>

      <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
        <SortableContext items={queuedIds} strategy={verticalListSortingStrategy}>
          <QueuedInputDockSection
            queuedInputs={queuedInputs}
            showSectionLabel={false}
            separated={false}
            onSteer={onQueueSteer}
            onRemove={onQueueRemove}
            onEdit={onQueueEdit}
            onReorder={onQueueReorder}
            editingQueuedInputId={editingQueuedInputId}
          />
        </SortableContext>
      </DndContext>
    </div>
  )
}

function DockTitle({ title }: { title: string }): JSX.Element {
  return (
    <span style={titleStyle}>
      <ListChecks size={13} aria-hidden="true" style={{ color: 'var(--text-dimmed)' }} />
      <span>{title}</span>
    </span>
  )
}

function QueuedInputDockSection({
  queuedInputs,
  showSectionLabel,
  separated,
  onSteer,
  onRemove,
  onEdit,
  onReorder,
  editingQueuedInputId
}: {
  queuedInputs: QueuedTurnInput[]
  showSectionLabel: boolean
  separated: boolean
  onSteer?: (id: string) => void
  onRemove?: (id: string) => void
  onEdit?: (id: string) => void
  onReorder?: (orderedQueuedInputIds: string[]) => void
  editingQueuedInputId?: string | null
}): JSX.Element {
  const t = useT()
  const queuedIds = useMemo(() => queuedInputs.map((item) => item.id), [queuedInputs])
  const moveQueuedInput = (queuedInputId: string, delta: -1 | 1): void => {
    if (!onReorder) return
    const oldIndex = queuedInputs.findIndex((item) => item.id === queuedInputId)
    const newIndex = oldIndex + delta
    if (oldIndex < 0 || newIndex < 0 || newIndex >= queuedInputs.length) return
    onReorder(arrayMove(queuedIds, oldIndex, newIndex))
  }

  return (
    <div style={queueSectionStyle(separated)}>
      {showSectionLabel && <div style={queueSectionLabelStyle}>{t('composer.queueSection')}</div>}
      <div style={queueRowsStyle}>
        {queuedInputs.map((item) => (
          <QueuedInputDockRow
            key={item.id}
            item={item}
            label={summarizeQueuedInput(item, t)}
            onSteer={onSteer}
            onRemove={onRemove}
            onEdit={onEdit}
            onKeyboardMove={moveQueuedInput}
            editing={editingQueuedInputId === item.id}
          />
        ))}
      </div>
    </div>
  )
}

function QueuedInputDockRow({
  item,
  label,
  onSteer,
  onRemove,
  onEdit,
  onKeyboardMove,
  editing
}: {
  item: QueuedTurnInput
  label: string
  onSteer?: (id: string) => void
  onRemove?: (id: string) => void
  onEdit?: (id: string) => void
  onKeyboardMove?: (id: string, delta: -1 | 1) => void
  editing: boolean
}): JSX.Element {
  const t = useT()
  const isGuidancePending = item.status === 'guidancePending'
  const canEdit = (item.status === 'queued' || isGuidancePending) && !item.triggerKind && item.sentAsGoal !== true && Boolean(onEdit)
  const {
    attributes,
    listeners,
    setActivatorNodeRef,
    setNodeRef,
    transform,
    transition,
    isDragging
  } = useSortable({ id: item.id, disabled: isGuidancePending })
  const rowDragStyle: CSSProperties = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.62 : 1,
    zIndex: isDragging ? 2 : 1
  }

  return (
    <div ref={setNodeRef} style={{ ...queueRowStyle, ...rowDragStyle }}>
      <ActionTooltip label={t('composer.queueReorderAria')} placement="top">
        <button
          type="button"
          ref={setActivatorNodeRef}
          disabled={isGuidancePending}
          aria-label={t('composer.queueReorderAria')}
          style={queueDragHandleStyle(isGuidancePending, isDragging)}
          {...attributes}
          {...listeners}
          onKeyDown={(event) => {
            if (event.key === 'ArrowUp') {
              event.preventDefault()
              onKeyboardMove?.(item.id, -1)
              return
            }
            if (event.key === 'ArrowDown') {
              event.preventDefault()
              onKeyboardMove?.(item.id, 1)
            }
          }}
        >
          <GripVertical size={14} strokeWidth={1.8} aria-hidden />
        </button>
      </ActionTooltip>
      <ActionTooltip label={label} wrapperStyle={{ display: 'block', minWidth: 0, overflow: 'hidden', flexShrink: 1 }}>
        <span style={{ ...queueLabelStyle, display: 'block' }}>{label}</span>
      </ActionTooltip>
      <ActionTooltip
        label={isGuidancePending ? t('composer.queueGuidancePending') : t('composer.queueGuide')}
        placement="top"
      >
        <Button
          variant="ghost"
          size="sm"
          iconLeft={<CornerDownRight size={13} strokeWidth={1.9} />}
          onClick={() => onSteer?.(item.id)}
          disabled={!onSteer}
          aria-pressed={isGuidancePending}
          aria-label={isGuidancePending ? t('composer.queueGuidancePending') : t('composer.queueGuide')}
        >
          <span
            className={isGuidancePending ? 'tool-running-gradient-text' : undefined}
            style={queueGuideLabelStyle}
          >
            {isGuidancePending ? t('composer.queueGuidancePending') : t('composer.queueGuide')}
          </span>
        </Button>
      </ActionTooltip>
      <IconButton
        icon={<Trash2 size={14} strokeWidth={1.8} aria-hidden />}
        label={t('composer.queueRemove')}
        tooltipLabel={t('composer.queueRemove')}
        tooltipPlacement="top"
        size={24}
        radius={5}
        onClick={() => onRemove?.(item.id)}
        disabled={!onRemove}
      />
      <IconButton
        icon={editing
          ? <RunningSpinner size={12} borderWidth={1.8} testId={`queued-editing-${item.id}`} />
          : <Pencil size={14} strokeWidth={1.8} aria-hidden />}
        label={t('composer.queueEdit')}
        tooltipLabel={t('composer.queueEdit')}
        tooltipPlacement="top"
        size={24}
        radius={5}
        onClick={() => onEdit?.(item.id)}
        disabled={!canEdit || editing}
        aria-busy={editing}
      />
    </div>
  )
}

function summarizeQueuedInput(
  item: QueuedTurnInput,
  t: (key: string, vars?: Record<string, string | number>) => string
): string {
  const text = item.displayText?.trim()
  if (text) return text.length > 90 ? `${text.slice(0, 90)}...` : text
  const parts = item.nativeInputParts ?? item.materializedInputParts ?? []
  const files = parts.filter((part) => part.type === 'fileRef').length
  const images = parts.filter((part) => part.type === 'image' || part.type === 'localImage').length
  const labels: string[] = []
  if (files > 0) {
    labels.push(t(files === 1 ? 'composer.queueFileCountOne' : 'composer.queueFileCountMany', { count: files }))
  }
  if (images > 0) {
    labels.push(t(images === 1 ? 'composer.queueImageCountOne' : 'composer.queueImageCountMany', { count: images }))
  }
  return labels.length > 0 ? labels.join(', ') : t('composer.queueFallbackLabel')
}

const dockFrameStyle: CSSProperties = {
  width: 'calc(100% - 40px)',
  maxWidth: 'none',
  margin: '0 auto -1px',
  borderTop: '1px solid var(--composer-input-border)',
  borderRight: '1px solid var(--composer-input-border)',
  borderBottom: '0 solid transparent',
  borderLeft: '1px solid var(--composer-input-border)',
  borderRadius: '16px 16px 0 0',
  background: 'var(--background-activity-dock-background)',
  backdropFilter: 'var(--glass-blur-soft)',
  WebkitBackdropFilter: 'var(--glass-blur-soft)',
  boxShadow: 'var(--background-activity-dock-shadow)',
  overflow: 'hidden',
  pointerEvents: 'auto'
}


const dockHeaderStyle: CSSProperties = {
  minHeight: '28px',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: '8px',
  padding: '0 4px 0 10px',
  color: 'var(--text-secondary)',
  fontSize: '12px'
}


const headerStaticStyle: CSSProperties = {
  minWidth: 0,
  minHeight: '28px',
  flex: '1 1 auto',
  display: 'inline-flex',
  alignItems: 'center',
  padding: '4px 0 3px',
  color: 'var(--text-secondary)'
}

const titleStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: '6px',
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}


function queueSectionStyle(separated: boolean): CSSProperties {
  return {
    padding: separated ? '0 10px 7px' : '0 10px 8px',
    borderBottom: separated ? '1px solid color-mix(in srgb, var(--glass-border) 46%, transparent)' : 'none'
  }
}

const queueSectionLabelStyle: CSSProperties = {
  padding: '1px 0 5px 18px',
  color: 'var(--text-dimmed)',
  fontSize: '11px',
  lineHeight: '14px'
}

const queueRowsStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: '3px'
}

const queueRowStyle: CSSProperties = {
  position: 'relative',
  minHeight: '26px',
  display: 'grid',
  gridTemplateColumns: '18px minmax(0, 1fr) auto 24px 24px',
  alignItems: 'center',
  gap: '6px',
  color: 'var(--text-secondary)',
  fontSize: '12px',
  borderRadius: '7px',
  background: 'transparent'
}

function queueDragHandleStyle(disabled: boolean, isDragging: boolean): CSSProperties {
  return {
    width: 18,
    height: 22,
    padding: 0,
    border: 'none',
    borderRadius: 5,
    background: 'transparent',
    color: isDragging ? 'var(--text-secondary)' : 'var(--text-dimmed)',
    opacity: disabled ? 0.45 : 1,
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    cursor: disabled ? 'default' : isDragging ? 'grabbing' : 'grab'
  }
}

const queueLabelStyle: CSSProperties = {
  minWidth: 0,
  overflow: 'hidden',
  whiteSpace: 'nowrap',
  textOverflow: 'ellipsis'
}

const queueGuideLabelStyle: CSSProperties = {
  lineHeight: 1.25
}










