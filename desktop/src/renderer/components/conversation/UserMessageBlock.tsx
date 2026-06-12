import { useEffect, useRef, useState, type MouseEvent } from 'react'
import { Bot, CornerDownRight, Image as ImageIcon, MessagesSquare, Pencil, Sparkle, Target, Terminal, UsersRound } from 'lucide-react'
import { FileTypeIcon } from '../ui/FileTypeIcon'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { translate } from '../../../shared/locales'
import { useConversationStore } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import { useCronStore } from '../../stores/cronStore'
import { ImageLightbox } from './ImageLightbox'
import { MessageCopyButton } from './MessageCopyButton'
import { parseUserMessageSegments, segmentsFromNativeInputParts } from './parseUserMessageSegments'
import type { ConversationItem, InputPart, UserMessageImageRef } from '../../types/conversation'
import { openConversationLink, openImagePathInViewer } from '../../utils/conversationDeepLink'
import { stripSystemReminderBlocks } from '../../utils/systemReminderText'
import { resolveLocalReferencePath, resolveSkillReferencePath } from '../../utils/referencePaths'
import { addToast } from '../../stores/toastStore'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ReferencePathContextMenu } from './ReferencePathContextMenu'
import type { ContextMenuPosition } from '../ui/ContextMenu'

const imageDataUrlCache = new Map<string, string>()

interface UserMessageBlockProps {
  text: string
  nativeInputParts?: InputPart[]
  imageDataUrls?: string[]
  images?: UserMessageImageRef[]
  createdAt?: string
  deliveryMode?: ConversationItem['deliveryMode']
  triggerKind?: ConversationItem['triggerKind']
  triggerLabel?: string
  triggerRefId?: string
  sentAsGoal?: boolean
  editable?: boolean
  onEdit?: () => void
  editing?: boolean
  editText?: string
  editSubmitting?: boolean
  editSubmitDisabled?: boolean
  onEditTextChange?: (text: string) => void
  onCancelEdit?: () => void
  onSubmitEdit?: () => void
}

/**
 * Renders a user message with a subtle background tint.
 * Plain text only — no Markdown. Spec §10.3.2
 * `@relative/path` tokens (from RichInputArea) render as compact file chips.
 */
export function UserMessageBlock({
  text,
  nativeInputParts,
  imageDataUrls,
  images,
  createdAt,
  deliveryMode,
  triggerKind,
  triggerLabel,
  triggerRefId,
  sentAsGoal = false,
  editable = false,
  onEdit,
  editing = false,
  editText,
  editSubmitting = false,
  editSubmitDisabled = false,
  onEditTextChange,
  onCancelEdit,
  onSubmitEdit
}: UserMessageBlockProps): JSX.Element {
  const t = useT()
  const editAreaRef = useRef<HTMLTextAreaElement | null>(null)
  const [lightboxSrc, setLightboxSrc] = useState<string | null>(null)
  const [hovered, setHovered] = useState(false)
  const [focusedWithin, setFocusedWithin] = useState(false)
  const [editButtonHovered, setEditButtonHovered] = useState(false)
  const [editButtonFocused, setEditButtonFocused] = useState(false)
  const [hydratedImages, setHydratedImages] = useState<Array<{ url: string; absolutePath?: string }>>(
    (imageDataUrls ?? []).map((url) => ({ url }))
  )
  const [failedImages, setFailedImages] = useState<UserMessageImageRef[]>([])
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const remoteWorkspaceActive = useConversationStore((s) => s.remoteWorkspaceActive)
  const activeThreadId = useThreadStore((s) => s.activeThreadId)
  const hasImages = hydratedImages.length > 0
  const displayText = stripSystemReminderBlocks(text)
  const segments = nativeInputParts != null && nativeInputParts.length > 0
    ? segmentsFromNativeInputParts(nativeInputParts)
    : displayText.length > 0
      ? parseUserMessageSegments(displayText)
      : []
  const textSegments = segments
  const sentTime = formatMessageTime(createdAt)
  const actionsVisible = hovered || focusedWithin
  const editButtonChromeVisible = editButtonHovered || editButtonFocused
  const isGuidance = deliveryMode === 'guidance'

  useEffect(() => {
    if (!editing) return
    const el = editAreaRef.current
    if (!el) return
    el.style.height = 'auto'
    const lineHeight = parseInt(getComputedStyle(el).lineHeight) || 20
    const maxHeight = lineHeight * 8 + 24
    el.style.height = `${Math.min(el.scrollHeight, maxHeight)}px`
    el.focus()
  }, [editing, editText])

  useEffect(() => {
    let cancelled = false

    const hydrateImages = async (): Promise<void> => {
      if (Array.isArray(imageDataUrls) && imageDataUrls.length > 0) {
        if (cancelled) return
        setHydratedImages(imageDataUrls.map((url) => ({ url })))
        setFailedImages([])
        return
      }
      if (!Array.isArray(images) || images.length === 0) {
        if (cancelled) return
        setHydratedImages([])
        setFailedImages([])
        return
      }
      if (remoteWorkspaceActive) {
        if (cancelled) return
        setHydratedImages([])
        setFailedImages(images)
        return
      }

      const loaded: Array<{ url: string; absolutePath?: string }> = []
      const failed: UserMessageImageRef[] = []
      for (const image of images) {
        const cached = imageDataUrlCache.get(image.path)
        if (cached) {
          loaded.push({ url: cached, absolutePath: image.path })
          continue
        }
        try {
          const result = await window.api.workspace.readImageAsDataUrl({ path: image.path })
          const dataUrl = result.dataUrl
          if (dataUrl) {
            imageDataUrlCache.set(image.path, dataUrl)
            loaded.push({ url: dataUrl, absolutePath: image.path })
          } else {
            failed.push(image)
          }
        } catch {
          failed.push(image)
        }
      }
      if (cancelled) return
      setHydratedImages(loaded)
      setFailedImages(failed)
    }

    void hydrateImages()
    return () => {
      cancelled = true
    }
  }, [imageDataUrls, images, remoteWorkspaceActive])

  return (
    <>
      <div
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
        onFocusCapture={() => setFocusedWithin(true)}
        onBlurCapture={(e) => {
          if (!e.currentTarget.contains(e.relatedTarget)) {
            setFocusedWithin(false)
          }
        }}
        style={{
          alignSelf: 'flex-end',
          width: editing ? 'min(100%, var(--conversation-reading-width))' : undefined,
          maxWidth: editing
            ? 'var(--conversation-reading-width)'
            : 'min(82%, var(--conversation-reading-width))',
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'flex-end'
        }}
      >
        {!editing && isGuidance && (
          <div
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              justifyContent: 'flex-end',
              gap: '5px',
              margin: '0 4px 5px 0',
              color: 'var(--text-tertiary)',
              fontSize: '11px',
              lineHeight: 1.2,
              fontWeight: 500,
              userSelect: 'none'
            }}
          >
            <CornerDownRight size={13} strokeWidth={1.8} aria-hidden />
            <span>{t('conversation.steeredConversation')}</span>
          </div>
        )}
        <div
          style={{
            width: '100%',
            backgroundColor: 'var(--user-message-bg)',
            borderRadius: '12px',
            padding: '9px 13px',
            fontFamily: 'var(--font-body)',
            fontSize: 'var(--text-body-size)',
            fontWeight: 'var(--conversation-font-weight)',
            lineHeight: 'var(--text-body-line-height)',
            color: 'var(--text-primary)',
            whiteSpace: 'pre-wrap',
            wordBreak: 'break-word',
            display: 'flex',
            flexDirection: 'column',
            gap: '6px',
            userSelect: 'text'
          }}
        >
          {editing ? (
            <>
              <textarea
                ref={editAreaRef}
                value={editText ?? displayText}
                aria-label={t('conversation.editTextarea')}
                disabled={editSubmitting}
                onChange={(e) => onEditTextChange?.(e.currentTarget.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Escape') {
                    e.preventDefault()
                    onCancelEdit?.()
                    return
                  }
                  if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
                    e.preventDefault()
                    if (!editSubmitDisabled) {
                      onSubmitEdit?.()
                    }
                  }
                }}
                style={{
                  width: '100%',
                  minHeight: '72px',
                  maxHeight: '184px',
                  resize: 'none',
                  overflowY: 'auto',
                  border: 'none',
                  outline: 'none',
                  background: 'transparent',
                  color: 'var(--text-primary)',
                  font: 'inherit',
                  lineHeight: 'inherit',
                  padding: 0
                }}
              />
              <div
                style={{
                  display: 'flex',
                  justifyContent: 'flex-end',
                  alignItems: 'center',
                  gap: '8px'
                }}
              >
                <button
                  type="button"
                  onClick={onCancelEdit}
                  disabled={editSubmitting}
                  style={{
                    height: 32,
                    padding: '0 12px',
                    borderRadius: 16,
                    border: '1px solid var(--border-default)',
                    background: 'var(--bg-secondary)',
                    color: 'var(--text-secondary)',
                    cursor: editSubmitting ? 'default' : 'pointer',
                    opacity: editSubmitting ? 0.7 : 1
                  }}
                >
                  {t('common.cancel')}
                </button>
                <button
                  type="button"
                  onClick={onSubmitEdit}
                  disabled={editSubmitDisabled}
                  aria-label={t('conversation.editSend')}
                  style={{
                    height: 32,
                    padding: '0 14px',
                    borderRadius: 16,
                    border: '1px solid transparent',
                    background: editSubmitDisabled ? 'var(--bg-tertiary)' : 'var(--text-primary)',
                    color: editSubmitDisabled ? 'var(--text-dimmed)' : 'var(--bg-primary)',
                    cursor: editSubmitDisabled ? 'not-allowed' : 'pointer',
                    fontWeight: 600
                  }}
                >
                  {editSubmitting ? t('conversation.editSending') : t('conversation.editSend')}
                </button>
              </div>
            </>
          ) : (
            <>
          {(hasImages || failedImages.length > 0) && (
          <div
            style={{
              display: 'flex',
              flexDirection: 'row',
              flexWrap: 'wrap',
              gap: '8px'
            }}
          >
            {hydratedImages.map((imageItem, idx) => (
              <button
                key={`${idx}-${imageItem.url.slice(0, 32)}`}
                type="button"
                onClick={() => setLightboxSrc(imageItem.url)}
                style={{
                  padding: 0,
                  border: 'none',
                  background: 'transparent',
                  cursor: 'pointer',
                  borderRadius: '6px',
                  overflow: 'hidden',
                  lineHeight: 0
                }}
                aria-label={`View attached image ${idx + 1}`}
              >
                <img
                  src={imageItem.url}
                  alt=""
                  style={{
                    maxHeight: '80px',
                    maxWidth: '120px',
                    objectFit: 'cover',
                    display: 'block'
                  }}
                />
              </button>
            ))}
            {failedImages.map((imageItem, idx) => {
              const label = imageItem.fileName || basename(imageItem.path)
              return (
                <ActionTooltip key={`failed-image-${imageItem.path}-${idx}`} label={imageItem.path}>
                <button
                  type="button"
                  onClick={() => {
                    if (remoteWorkspaceActive || !workspacePath || !activeThreadId) return
                    void openImagePathInViewer({
                      absolutePath: imageItem.path,
                      workspacePath,
                      threadId: activeThreadId,
                      t
                    })
                  }}
                  disabled={remoteWorkspaceActive || !workspacePath || !activeThreadId}
                  aria-label={t('conversation.openImageAttachmentAria', { file: label })}
                  style={{
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: '6px',
                    maxWidth: '180px',
                    padding: '6px 8px',
                    border: '1px solid var(--border-default)',
                    borderRadius: '6px',
                    background: 'var(--bg-secondary)',
                    color: 'var(--text-secondary)',
                    cursor: !remoteWorkspaceActive && workspacePath && activeThreadId ? 'pointer' : 'default',
                    font: 'inherit',
                    fontSize: '12px',
                    lineHeight: 1.2
                  }}
                >
                  <ImageIcon size={14} strokeWidth={1.9} aria-hidden style={{ flexShrink: 0 }} />
                  <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                    {label}
                  </span>
                </button>
                </ActionTooltip>
              )
            })}
          </div>
        )}
        {textSegments.length > 0 && (
          <span>
            {textSegments.map((seg, idx) =>
              seg.type === 'text' ? (
                <span key={`t-${idx}`}>{seg.value}</span>
              ) : seg.type === 'fileRef' ? (
                <FileRefChip
                  key={`f-${idx}-${seg.relativePath}`}
                  displayPath={seg.relativePath}
                  targetPath={seg.targetPath ?? seg.relativePath}
                  workspacePath={workspacePath}
                  activeThreadId={activeThreadId}
                  remoteWorkspaceActive={remoteWorkspaceActive}
                />
              ) : seg.type === 'commandRef' ? (
                <CommandRefChip key={`c-${idx}-${seg.commandText}`} commandText={seg.commandText} />
              ) : (
                <SkillRefChip key={`s-${idx}-${seg.skillName}`} skillName={seg.skillName} />
              )
            )}
          </span>
        )}
        {triggerKind && (
          <TriggerSourcePill
            kind={triggerKind}
            label={triggerLabel}
            refId={triggerRefId}
          />
        )}
        {!triggerKind && sentAsGoal && <SentAsGoalPill />}
            </>
          )}
        </div>
        {!editing && (
          <div
            style={{
              minHeight: '24px',
              marginTop: '8px',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'flex-end',
              gap: '6px',
              color: 'var(--text-tertiary)',
              fontSize: '11px',
              lineHeight: 1,
              userSelect: 'none'
            }}
          >
            {sentTime && (
              <ActionTooltip label={sentTime.title}>
                <span
                  style={{
                    padding: '0 2px',
                    opacity: actionsVisible ? 1 : 0,
                    transition: 'opacity 120ms ease'
                  }}
                >
                  {sentTime.label}
                </span>
              </ActionTooltip>
            )}
            {editable && onEdit && (
              <ActionTooltip
                label={t('conversation.editMessage')}
                placement="top"
                wrapperStyle={{
                  display: 'inline-flex',
                  opacity: actionsVisible ? 1 : 0,
                  pointerEvents: actionsVisible ? 'auto' : 'none',
                  transition: 'opacity 120ms ease'
                }}
              >
                <button
                  type="button"
                  onClick={onEdit}
                  aria-label={t('conversation.editMessage')}
                  onMouseEnter={() => setEditButtonHovered(true)}
                  onMouseLeave={() => setEditButtonHovered(false)}
                  onFocus={() => setEditButtonFocused(true)}
                  onBlur={() => setEditButtonFocused(false)}
                  style={{
                    width: '24px',
                    height: '24px',
                    borderRadius: '6px',
                    border: '1px solid transparent',
                    background: editButtonChromeVisible ? 'var(--bg-tertiary)' : 'transparent',
                    color: editButtonChromeVisible ? 'var(--text-primary)' : 'var(--text-secondary)',
                    display: 'inline-flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    cursor: 'pointer',
                    transition: 'opacity 120ms ease, color 120ms ease, background 120ms ease, border-color 120ms ease'
                  }}
                >
                  <Pencil size={14} aria-hidden />
                </button>
              </ActionTooltip>
            )}
            <MessageCopyButton
              getText={() => displayText}
              visible={actionsVisible && displayText.length > 0}
              disabled={displayText.length === 0}
              wrapperStyle={{
                position: 'static',
                display: 'inline-flex',
                opacity: actionsVisible && displayText.length > 0 ? 1 : 0,
                pointerEvents: actionsVisible && displayText.length > 0 ? 'auto' : 'none',
                transition: 'opacity 120ms ease'
              }}
            />
          </div>
        )}
      </div>
      {lightboxSrc != null && (
        <ImageLightbox src={lightboxSrc} onClose={() => { setLightboxSrc(null) }} />
      )}
    </>
  )
}

function formatMessageTime(createdAt?: string): { label: string; title: string } | null {
  if (!createdAt) return null
  const date = new Date(createdAt)
  if (!Number.isFinite(date.getTime())) return null

  return {
    label: new Intl.DateTimeFormat(undefined, {
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
      hourCycle: 'h23'
    }).format(date),
    title: new Intl.DateTimeFormat(undefined, {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hour12: false,
      hourCycle: 'h23'
    }).format(date)
  }
}

function SkillRefChip({ skillName }: { skillName: string }): JSX.Element {
  const t = useT()
  const remoteWorkspaceActive = useConversationStore((s) => s.remoteWorkspaceActive)
  const [contextMenu, setContextMenu] = useState<{ position: ContextMenuPosition; targetPath: string } | null>(null)

  async function handleContextMenu(event: MouseEvent<HTMLSpanElement>): Promise<void> {
    event.preventDefault()
    event.stopPropagation()
    if (remoteWorkspaceActive) {
      addToast(t('skillDetail.openFolderRemoteUnavailable'), 'warning')
      return
    }
    const targetPath = await resolveSkillReferencePath(skillName)
    if (!targetPath) {
      addToast(t('conversation.reference.skillPathUnavailable'), 'warning')
      return
    }
    setContextMenu({
      position: { x: event.clientX, y: event.clientY },
      targetPath
    })
  }

  return (
    <>
      <ActionTooltip label={`$${skillName}`}>
      <span
        onContextMenu={(event) => { void handleContextMenu(event) }}
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: '4px',
          verticalAlign: '-0.2em',
          margin: '0 4px',
          padding: '1px 7px',
          borderRadius: '7px',
          border: '1px solid color-mix(in srgb, var(--success) 38%, transparent)',
          background: 'color-mix(in srgb, var(--success) 16%, transparent)',
          color: 'var(--success)',
          fontSize: '12px',
          lineHeight: 1.25,
          whiteSpace: 'nowrap',
          userSelect: 'none',
          fontWeight: 600,
          maxWidth: 'var(--inline-reference-max-width)'
        }}
      >
        <Sparkle size={12} strokeWidth={2.25} aria-hidden />
        <span>{skillName}</span>
      </span>
      </ActionTooltip>
      {contextMenu && (
        <ReferencePathContextMenu
          position={contextMenu.position}
          targetPath={contextMenu.targetPath}
          onClose={() => setContextMenu(null)}
        />
      )}
    </>
  )
}

function CommandRefChip({ commandText }: { commandText: string }): JSX.Element {
  const label = commandText.startsWith('/') ? commandText.slice(1) : commandText
  return (
    <ActionTooltip label={commandText}>
    <span
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: '4px',
          verticalAlign: '-0.2em',
          margin: '0 4px',
          padding: '1px 7px',
          borderRadius: '7px',
          border: '1px solid color-mix(in srgb, var(--accent) 38%, transparent)',
          background: 'color-mix(in srgb, var(--accent) 16%, transparent)',
          color: 'var(--accent)',
          fontSize: '12px',
          lineHeight: 1.25,
          whiteSpace: 'nowrap',
          userSelect: 'none',
          fontWeight: 600,
          maxWidth: 'var(--inline-reference-max-width)'
        }}
      >
      <Terminal size={12} strokeWidth={2.25} aria-hidden />
      <span>{label}</span>
    </span>
    </ActionTooltip>
  )
}

function FileRefChip({
  displayPath,
  targetPath,
  workspacePath,
  activeThreadId,
  remoteWorkspaceActive
}: {
  displayPath: string
  targetPath: string
  workspacePath: string
  activeThreadId: string | null
  remoteWorkspaceActive: boolean
}): JSX.Element {
  const t = useT()
  const [contextMenu, setContextMenu] = useState<{ position: ContextMenuPosition; targetPath: string } | null>(null)
  const fileName = displayPath.split(/[/\\]/).pop() ?? displayPath
  const resolvedTargetPath = resolveLocalReferencePath(targetPath, workspacePath)
  const title = resolvedTargetPath ?? targetPath
  const canOpen = !remoteWorkspaceActive && workspacePath.length > 0 && !!activeThreadId

  return (
    <>
      <ActionTooltip label={title}>
      <button
        type="button"
        aria-label={t('conversation.openFileRefAria', { file: fileName })}
        disabled={!canOpen}
        onContextMenu={(event) => {
          if (!resolvedTargetPath) return
          event.preventDefault()
          event.stopPropagation()
          setContextMenu({
            position: { x: event.clientX, y: event.clientY },
            targetPath: resolvedTargetPath
          })
        }}
        onClick={() => {
          if (!canOpen || !activeThreadId) return
          void openConversationLink({
            target: targetPath,
            workspacePath,
            threadId: activeThreadId,
            t
          })
        }}
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: '4px',
          verticalAlign: '-0.2em',
          margin: '0 4px',
          padding: '1px 7px',
          borderRadius: '7px',
          border: '1px solid color-mix(in srgb, var(--border-active) 44%, transparent)',
          background: 'color-mix(in srgb, var(--bg-tertiary) 88%, transparent)',
          color: 'var(--text-primary)',
          fontSize: '12px',
          lineHeight: 1.25,
          whiteSpace: 'nowrap',
          userSelect: 'none',
          maxWidth: 'var(--inline-reference-max-width)',
          cursor: canOpen ? 'pointer' : 'default',
          font: 'inherit'
        }}
      >
        <FileTypeIcon path={displayPath} size={12} />
        <span>{fileName}</span>
      </button>
      </ActionTooltip>
      {contextMenu && (
        <ReferencePathContextMenu
          position={contextMenu.position}
          targetPath={contextMenu.targetPath}
          onClose={() => setContextMenu(null)}
        />
      )}
    </>
  )
}

function basename(filePath: string): string {
  return filePath.split(/[\\/]/).pop() ?? filePath
}

function TriggerSourcePill({
  kind,
  label,
  refId
}: {
  kind: NonNullable<ConversationItem['triggerKind']>
  label?: string
  refId?: string
}): JSX.Element {
  const locale = useLocale()
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const setAutomationsTab = useUIStore((s) => s.setAutomationsTab)
  const selectCronJob = useCronStore((s) => s.selectCronJob)

  const canNavigate =
    (kind === 'cron' && !!refId) || (kind === 'automation' && !!refId) || kind === 'team'
    || (kind === 'thread' && !!refId)
  const isGoal = kind === 'goal'
  const isTeam = kind === 'team'
  const isApp = kind === 'app'
  const isThread = kind === 'thread'
  const isSubAgentFollowup = kind === 'subagentFollowupTask'
  const isSubAgentMailbox = kind === 'subagentMailbox'
  const isSubAgentInput = kind === 'subagentInput'
  const isSubAgent = isSubAgentFollowup || isSubAgentMailbox || isSubAgentInput
  const badgeText = isGoal
    ? translate(locale, 'goal.triggeredBy.badge')
    : isTeam
      ? translate(locale, 'teams.triggeredBy.badge')
      : isApp
        ? translate(locale, 'app.triggeredBy.badge')
        : isThread
          ? translate(locale, 'thread.triggeredBy.badge')
          : isSubAgent
            ? translate(locale, 'subAgent.triggeredBy.badge')
            : translate(locale, 'automation.triggeredBy.badge')
  const detailText = isGoal
    ? (label || translate(locale, 'goal.triggeredBy.generic'))
    : isTeam
      ? label
        ? translate(locale, 'teams.triggeredBy.detail', { label })
        : translate(locale, 'teams.triggeredBy.generic')
      : isApp
        ? label
          ? translate(locale, 'app.triggeredBy.detail', { label })
          : translate(locale, 'app.triggeredBy.generic')
        : isSubAgent
          ? label
            ? translate(
                locale,
                isSubAgentFollowup
                  ? 'subAgent.triggeredBy.followup'
                  : isSubAgentMailbox
                    ? 'subAgent.triggeredBy.mailbox'
                    : 'subAgent.triggeredBy.input',
                { label }
              )
            : translate(locale, 'subAgent.triggeredBy.generic')
          : isThread
            ? label
              ? translate(locale, 'thread.triggeredBy.detail', { label })
              : translate(locale, 'thread.triggeredBy.generic')
            : label
              ? translate(
                  locale,
                  kind === 'heartbeat'
                    ? 'automation.triggeredBy.heartbeat'
                    : kind === 'cron'
                      ? 'automation.triggeredBy.cron'
                      : 'automation.triggeredBy.task',
                  { label }
                )
              : translate(locale, 'automation.triggeredBy.generic')

  const onClick = canNavigate
    ? () => {
        if (kind === 'thread') {
          if (refId) {
            useThreadStore.getState().setActiveThreadId(refId)
            setActiveMainView('conversation')
          }
          return
        }
        if (kind === 'team') {
          setActiveMainView('teams')
          return
        }
        setActiveMainView('automations')
        if (kind === 'cron') {
          setAutomationsTab('cron')
          if (refId) selectCronJob(refId)
        } else if (kind === 'automation') {
          setAutomationsTab('tasks')
        }
      }
    : undefined

  const commonStyle = {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '4px',
    padding: '2px 8px',
    borderRadius: '999px',
    border: '1px solid color-mix(in srgb, var(--border-active) 36%, transparent)',
    background: 'color-mix(in srgb, var(--bg-tertiary) 80%, transparent)',
    color: 'var(--text-dimmed)',
    fontSize: '11px',
    lineHeight: 1.25,
    fontWeight: 500,
    alignSelf: 'flex-start',
    userSelect: 'none' as const
  }

  const title = `${badgeText} · ${detailText}`

  if (onClick) {
    return (
      <ActionTooltip label={title} wrapperStyle={{ display: 'inline-flex' }}>
        <button
          type="button"
          onClick={onClick}
          aria-label={title}
          style={{ ...commonStyle, cursor: 'pointer', border: commonStyle.border }}
        >
          {isTeam ? (
            <UsersRound size={11} strokeWidth={2.1} aria-hidden />
          ) : isThread || isSubAgent ? (
            <MessagesSquare size={11} strokeWidth={2.1} aria-hidden />
          ) : (
            <Bot size={11} strokeWidth={2.1} aria-hidden />
          )}
          <span>{badgeText}</span>
        </button>
      </ActionTooltip>
    )
  }

  return (
    <ActionTooltip label={title} wrapperStyle={{ display: 'inline-flex' }}>
    <span style={commonStyle}>
      {isGoal ? (
        <Target size={11} strokeWidth={2.1} aria-hidden />
      ) : isTeam ? (
        <UsersRound size={11} strokeWidth={2.1} aria-hidden />
      ) : isThread || isSubAgent ? (
        <MessagesSquare size={11} strokeWidth={2.1} aria-hidden />
      ) : (
        <Bot size={11} strokeWidth={2.1} aria-hidden />
      )}
      <span>{badgeText}</span>
    </span>
    </ActionTooltip>
  )
}

function SentAsGoalPill(): JSX.Element {
  const locale = useLocale()
  const label = translate(locale, 'goal.sentAsGoal.badge')

  return (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: '4px',
        padding: '2px 8px',
        borderRadius: '999px',
        border: '1px solid color-mix(in srgb, var(--success) 34%, transparent)',
        background: 'color-mix(in srgb, var(--success) 12%, transparent)',
        color: 'var(--text-dimmed)',
        fontSize: '11px',
        lineHeight: 1.25,
        fontWeight: 500,
        alignSelf: 'flex-start',
        userSelect: 'none'
      }}
    >
      <Target size={11} strokeWidth={2.1} aria-hidden />
      <span>{label}</span>
    </span>
  )
}
