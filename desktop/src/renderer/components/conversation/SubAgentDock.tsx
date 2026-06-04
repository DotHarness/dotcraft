import { useEffect, useMemo } from 'react'
import type { CSSProperties, ReactNode } from 'react'
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
import { Bot, ChevronDown, CornerDownRight, ExternalLink, GripVertical, ListChecks, Square, Trash2 } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import {
  isSubAgentChildRunning,
  useSubAgentStore,
  type SubAgentChild
} from '../../stores/subAgentStore'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import { addToast } from '../../stores/toastStore'
import type { QueuedTurnInput } from '../../types/conversation'
import { RunningSpinner } from '../ui/RunningSpinner'
import { ActionTooltip } from '../ui/ActionTooltip'
import { getSubAgentAccent } from '../../utils/subAgentPresentation'

interface SubAgentDockProps {
  parentThreadId: string
}

interface BackgroundActivityDockProps extends SubAgentDockProps {
  queuedInputs?: QueuedTurnInput[]
  onQueueSteer?: (id: string) => void
  onQueueRemove?: (id: string) => void
  onQueueReorder?: (orderedQueuedInputIds: string[]) => void
}

export function SubAgentDock({ parentThreadId }: SubAgentDockProps): JSX.Element | null {
  return <BackgroundActivityDock parentThreadId={parentThreadId} />
}

export function BackgroundActivityDock({
  parentThreadId,
  queuedInputs = EMPTY_QUEUED_INPUTS,
  onQueueSteer,
  onQueueRemove,
  onQueueReorder
}: BackgroundActivityDockProps): JSX.Element | null {
  const t = useT()
  const children = useSubAgentStore((s) => s.childrenByParent.get(parentThreadId) ?? EMPTY_SUB_AGENT_CHILDREN)
  const collapsed = useSubAgentStore((s) => s.collapsedByParent.get(parentThreadId) === true)
  const setParentCollapsed = useSubAgentStore((s) => s.setParentCollapsed)
  const fetchChildren = useSubAgentStore((s) => s.fetchChildren)

  const runningChildren = useMemo(
    () => children.filter(isSubAgentChildRunning),
    [children]
  )
  const queuedIds = useMemo(() => queuedInputs.map((item) => item.id), [queuedInputs])
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } })
  )

  useEffect(() => {
    if (!parentThreadId) return
    void fetchChildren(parentThreadId)
  }, [fetchChildren, parentThreadId])

  if (children.length === 0 && queuedInputs.length === 0) return null

  const hasSubAgents = children.length > 0
  const hasQueue = queuedInputs.length > 0
  const closeableRunning = runningChildren.filter((child) => child.supportsClose && child.agentPath)
  const contentMaxHeight = Math.min(children.length * 62 + 8, 260)

  const stopAll = async (): Promise<void> => {
    try {
      await Promise.all(closeableRunning.map((child) => closeSubAgent(parentThreadId, child.agentPath!)))
      await fetchChildren(parentThreadId)
    } catch (err) {
      addToast(err instanceof Error ? err.message : String(err), 'error')
    }
  }
  const toggleCollapsed = (): void => setParentCollapsed(parentThreadId, !collapsed)
  const title = hasSubAgents
    ? t('subAgentDock.title', { count: children.length })
    : t(queuedInputs.length === 1 ? 'composer.queueDockTitleOne' : 'composer.queueDockTitleMany', { count: queuedInputs.length })

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
        {hasSubAgents ? (
          <button
            type="button"
            aria-label={collapsed ? t('subAgentDock.expand') : t('subAgentDock.collapse')}
            onClick={toggleCollapsed}
            style={headerToggleStyle}
          >
            <DockTitle icon="bot" title={title}>
              {collapsed && runningChildren.length > 0 && (
                <span style={runningSummaryStyle}>
                  {t('subAgentDock.runningSummary', { count: runningChildren.length })}
                </span>
              )}
            </DockTitle>
            <ChevronDown
              size={14}
              aria-hidden="true"
              style={{
                transform: collapsed ? 'rotate(-90deg)' : 'none',
                transition: 'transform 120ms ease',
                flexShrink: 0
              }}
            />
          </button>
        ) : (
          <div style={headerStaticStyle}>
            <DockTitle icon="queue" title={title} />
          </div>
        )}
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: '4px', flexShrink: 0 }}>
          {closeableRunning.length > 0 && (
            <ActionTooltip label={t('subAgentDock.stopAll')} placement="top">
              <button
                type="button"
                aria-label={t('subAgentDock.stopAll')}
                onClick={(event) => {
                  event.stopPropagation()
                  void stopAll()
                }}
                style={iconButtonStyle}
              >
                <Square size={12} fill="currentColor" aria-hidden="true" />
              </button>
            </ActionTooltip>
          )}
        </span>
      </div>

      {hasQueue && (
        <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
          <SortableContext items={queuedIds} strategy={verticalListSortingStrategy}>
            <QueuedInputDockSection
              queuedInputs={queuedInputs}
              showSectionLabel={hasSubAgents}
              separated={hasSubAgents}
              onSteer={onQueueSteer}
              onRemove={onQueueRemove}
              onReorder={onQueueReorder}
            />
          </SortableContext>
        </DndContext>
      )}

      {hasSubAgents && (
        <div
          aria-hidden={collapsed}
          data-testid="subagent-dock-rows"
          style={rowsViewportStyle(collapsed, contentMaxHeight)}
        >
          <div style={rowsStyle}>
            {children.map((child, index) => (
              <SubAgentDockRow
                key={child.childThreadId}
                child={child}
                parentThreadId={parentThreadId}
                color={getSubAgentAccent(child.childThreadId || child.nickname || String(index))}
                onRefresh={() => { void fetchChildren(parentThreadId) }}
              />
            ))}
          </div>
        </div>
      )}
    </div>
  )
}

const EMPTY_SUB_AGENT_CHILDREN: SubAgentChild[] = []
const EMPTY_QUEUED_INPUTS: QueuedTurnInput[] = []

function DockTitle({
  icon,
  title,
  children
}: {
  icon: 'bot' | 'queue'
  title: string
  children?: ReactNode
}): JSX.Element {
  const Icon = icon === 'bot' ? Bot : ListChecks
  return (
    <span style={titleStyle}>
      <Icon size={13} aria-hidden="true" style={{ color: 'var(--text-dimmed)' }} />
      <span>{title}</span>
      {children}
    </span>
  )
}

function QueuedInputDockSection({
  queuedInputs,
  showSectionLabel,
  separated,
  onSteer,
  onRemove,
  onReorder
}: {
  queuedInputs: QueuedTurnInput[]
  showSectionLabel: boolean
  separated: boolean
  onSteer?: (id: string) => void
  onRemove?: (id: string) => void
  onReorder?: (orderedQueuedInputIds: string[]) => void
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
            onKeyboardMove={moveQueuedInput}
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
  onKeyboardMove
}: {
  item: QueuedTurnInput
  label: string
  onSteer?: (id: string) => void
  onRemove?: (id: string) => void
  onKeyboardMove?: (id: string, delta: -1 | 1) => void
}): JSX.Element {
  const t = useT()
  const isGuidancePending = item.status === 'guidancePending'
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
      <span title={label} style={queueLabelStyle}>
        {label}
      </span>
      <ActionTooltip
        label={isGuidancePending ? t('composer.queueGuidancePending') : t('composer.queueGuide')}
        placement="top"
      >
        <button
          type="button"
          onClick={() => onSteer?.(item.id)}
          disabled={isGuidancePending || !onSteer}
          aria-label={isGuidancePending ? t('composer.queueGuidancePending') : t('composer.queueGuide')}
          style={{
            ...queuedSteerButtonStyle,
            opacity: isGuidancePending ? 0.55 : 1,
            cursor: isGuidancePending || !onSteer ? 'default' : 'pointer'
          }}
        >
          <CornerDownRight size={13} strokeWidth={1.9} aria-hidden style={{ flexShrink: 0 }} />
          <span>{isGuidancePending ? t('composer.queueGuidancePending') : t('composer.queueGuide')}</span>
        </button>
      </ActionTooltip>
      <ActionTooltip label={t('composer.queueRemove')} placement="top">
        <button
          type="button"
          onClick={() => onRemove?.(item.id)}
          disabled={!onRemove}
          aria-label={t('composer.queueRemove')}
          style={queuedIconButtonStyle}
        >
          <Trash2 size={14} strokeWidth={1.8} aria-hidden />
        </button>
      </ActionTooltip>
    </div>
  )
}

function SubAgentDockRow({
  child,
  parentThreadId,
  color,
  onRefresh
}: {
  child: SubAgentChild
  parentThreadId: string
  color: string
  onRefresh: () => void
}): JSX.Element {
  const t = useT()
  const running = isSubAgentChildRunning(child)
  const statusLabel = running
    ? child.lastToolDisplay?.trim() || t('subAgentDock.running')
    : formatSubAgentStatus(child, t)
  const canOpen = child.isPlaceholder !== true
  const roleMeta = formatDockAgentRole(child.agentRole)

  const openThread = (): void => {
    useThreadStore.getState().setActiveThreadId(child.childThreadId)
    useUIStore.getState().setActiveMainView('conversation')
  }

  const stop = async (): Promise<void> => {
    if (!child.agentPath) return
    try {
      await closeSubAgent(parentThreadId, child.agentPath)
      onRefresh()
    } catch (err) {
      addToast(err instanceof Error ? err.message : String(err), 'error')
    }
  }

  return (
    <div style={rowContainerStyle}>
      <div style={rowStyle}>
        <span style={statusSlotStyle}>
          {running ? (
            <RunningSpinner
              title={t('subAgentDock.running')}
              testId={`subagent-dock-running-${child.childThreadId}`}
            />
          ) : (
            <span aria-hidden style={{ width: 6, height: 6, borderRadius: 999, background: 'var(--text-dimmed)', opacity: 0.58 }} />
          )}
        </span>
        <span style={nameGroupStyle}>
          <span style={{ ...nicknameStyle, color }} title={child.nickname}>{child.nickname}</span>
          {roleMeta && <span style={metaStyle}>({roleMeta})</span>}
        </span>
        <span
          className={running ? 'tool-running-gradient-text' : undefined}
          style={descriptionStyle}
          title={statusLabel}
        >
          {statusLabel}
        </span>
        {canOpen ? (
          <button type="button" onClick={openThread} style={textButtonStyle}>
            <ExternalLink size={12} aria-hidden="true" />
            <span>{t('subAgentDock.open')}</span>
          </button>
        ) : (
          <span aria-hidden style={{ width: 1 }} />
        )}
        {child.supportsClose && child.agentPath && running && canOpen && (
          <ActionTooltip label={t('subAgentDock.stop')} placement="top">
            <button
              type="button"
              aria-label={t('subAgentDock.stopAria', { name: child.nickname })}
              onClick={() => { void stop() }}
              style={iconButtonStyle}
            >
              <Square size={11} fill="currentColor" aria-hidden="true" />
            </button>
          </ActionTooltip>
        )}
      </div>
    </div>
  )
}

function formatSubAgentStatus(
  child: SubAgentChild,
  t: (key: string, vars?: Record<string, string | number>) => string
): string {
  const normalized = child.status.trim().toLowerCase()
  if (normalized === 'closed' || normalized === 'completed') return t('subAgentDock.completed')
  if (normalized === 'failed') return t('subAgentDock.failed')
  if (normalized === 'cancelled' || normalized === 'canceled') return t('subAgentDock.cancelled')
  if (child.isCompleted) return t('subAgentDock.completed')
  return t('subAgentDock.idle')
}

async function closeSubAgent(parentThreadId: string, target: string): Promise<void> {
  await window.api.appServer.sendRequest('subagent/close', {
    parentThreadId,
    target
  })
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

function formatDockAgentRole(agentRole: string | null | undefined): string {
  const role = agentRole?.trim()
  if (!role || role.toLowerCase() === 'default') return ''
  return role
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

const headerToggleStyle: CSSProperties = {
  minWidth: 0,
  minHeight: '28px',
  flex: '1 1 auto',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: '8px',
  padding: '4px 0 3px',
  border: 'none',
  background: 'transparent',
  color: 'var(--text-secondary)',
  fontSize: '12px',
  textAlign: 'left',
  cursor: 'pointer'
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

const runningSummaryStyle: CSSProperties = {
  flexShrink: 0,
  color: 'var(--text-dimmed)'
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
  gridTemplateColumns: '18px minmax(0, 1fr) auto auto',
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

const queuedSteerButtonStyle: CSSProperties = {
  border: 'none',
  background: 'transparent',
  color: 'var(--text-dimmed)',
  cursor: 'pointer',
  display: 'inline-flex',
  alignItems: 'center',
  gap: '4px',
  fontSize: '12px',
  minHeight: '24px',
  padding: '2px 4px',
  borderRadius: '5px'
}

const queuedIconButtonStyle: CSSProperties = {
  width: '24px',
  height: '24px',
  border: 'none',
  background: 'transparent',
  color: 'var(--text-dimmed)',
  cursor: 'pointer',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: 0,
  borderRadius: '5px',
  flexShrink: 0
}

function rowsViewportStyle(collapsed: boolean, maxHeight: number): CSSProperties {
  return {
    maxHeight: collapsed ? 0 : maxHeight,
    opacity: collapsed ? 0 : 1,
    transform: collapsed ? 'translateY(-4px)' : 'translateY(0)',
    overflow: 'hidden',
    visibility: collapsed ? 'hidden' : 'visible',
    pointerEvents: collapsed ? 'none' : 'auto',
    transition: collapsed
      ? 'max-height 170ms ease, opacity 150ms ease, transform 170ms ease, visibility 0ms linear 170ms'
      : 'max-height 170ms ease, opacity 150ms ease, transform 170ms ease'
  }
}

const rowsStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: '5px',
  padding: '7px 10px'
}

const rowContainerStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: '4px',
  minWidth: 0
}

const rowStyle: CSSProperties = {
  minHeight: '24px',
  display: 'grid',
  gridTemplateColumns: '16px minmax(0, max-content) minmax(0, 1fr) auto auto',
  alignItems: 'center',
  gap: '6px',
  fontSize: '13px'
}

const statusSlotStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  width: 16
}

const nicknameStyle: CSSProperties = {
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
  fontWeight: 600
}

const nameGroupStyle: CSSProperties = {
  minWidth: 0,
  display: 'inline-flex',
  alignItems: 'center',
  gap: '5px',
  overflow: 'hidden',
  whiteSpace: 'nowrap'
}

const metaStyle: CSSProperties = {
  color: 'var(--text-dimmed)',
  flexShrink: 0,
  fontSize: '12px'
}

const descriptionStyle: CSSProperties = {
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
  color: 'var(--text-secondary)'
}

const iconButtonStyle: CSSProperties = {
  width: 24,
  height: 24,
  padding: 0,
  border: 'none',
  borderRadius: 6,
  background: 'transparent',
  color: 'var(--text-dimmed)',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  cursor: 'pointer'
}

const textButtonStyle: CSSProperties = {
  border: 'none',
  background: 'transparent',
  color: 'var(--text-dimmed)',
  display: 'inline-flex',
  alignItems: 'center',
  gap: '4px',
  padding: '2px 4px',
  fontSize: '12px',
  cursor: 'pointer'
}
