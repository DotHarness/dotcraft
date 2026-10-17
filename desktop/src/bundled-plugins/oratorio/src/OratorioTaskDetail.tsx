import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import { AlertCircle, AlertTriangle, Archive, ArrowLeft, Check, ChevronDown, CircleCheck, CircleDashed, CircleDot, CircleX, ExternalLink, FileDiff, GitBranch, Maximize2, MessageSquareText, RefreshCw, RotateCcw, Send, Trash2, X } from 'lucide-react'
import { oratorioClient } from './oratorio-client'
import type { CommentDto, DecisionDto, DiscussionTurnDto, FollowUpDraftDto, ImplementationDraftDto, ItemDetailResponse, ReviewDraftCommentDto, ReviewDraftDto, RunDto, SourceWriteDto } from './oratorio-contracts'
import { useOratorioI18n, type OratorioMessageKey, type OratorioTranslate } from './oratorio-i18n'
import { mapItemDetail } from './oratorio-mappers'
import { repositoryName, repositoryPath, type OratorioTask } from './oratorio-model'
import { OratorioCommentCard } from './OratorioCommentCard'
import { ProviderIcon, taskStateKey, taskStateTone } from './OratorioTaskParts'
import oratorioIcon from './assets/oratorio-icon.svg'
import { Button, IconButton, InlineDiff, Markdown, Menu, Spinner, SplitButton, Textarea, UserAvatar } from './ui'
import { oratorioHost, showOratorioToast } from './runtime'

type Tone = 'error' | 'success' | 'info' | 'warning' | 'neutral'
type Mutate = (key: string, operation: Promise<ItemDetailResponse>, message: string) => Promise<void>
interface PartProps { task: OratorioTask; detail: ItemDetailResponse; busy: string | null; mutate: Mutate }
type Decision = 'approve' | 'request-changes' | 'reject'

export function OratorioTaskDetail({ task, presentation = 'page', closeOnEscape = true, onBack, onExpand, onOpenThread, onTaskChange }: {
  task: OratorioTask; presentation?: 'page' | 'drawer'; closeOnEscape?: boolean; onBack: () => void; onExpand?: () => void
  onOpenThread: () => void; onTaskChange: (task: OratorioTask) => void
}) {
  const { t } = useOratorioI18n()
  const [busy, setBusy] = useState<string | null>(null)
  const detail = task.detail
  const drawer = presentation === 'drawer'

  useEffect(() => {
    if (!drawer || !closeOnEscape) return
    function onKeyDown(event: KeyboardEvent): void {
      if (event.key !== 'Escape' || event.defaultPrevented) return
      event.preventDefault()
      onBack()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [drawer, closeOnEscape, onBack])

  async function mutate(key: string, operation: Promise<ItemDetailResponse>, message: string): Promise<void> {
    if (busy) return
    setBusy(key)
    try {
      onTaskChange(mapItemDetail(await operation))
      showOratorioToast({ message, tone: 'success' })
    } catch (error) {
      showOratorioToast({ message: error instanceof Error ? error.message : t('actionRejected'), tone: 'error' })
    } finally {
      setBusy(null)
    }
  }

  const Root = drawer ? 'aside' : 'main'
  const label = t('taskDetailsFor', { task: task.shortId })
  const closing = drawer ? <>
    {onExpand ? <IconButton icon={<Maximize2 size={15} />} label={t('fullDetails')} tooltipLabel={t('fullDetails')} onClick={onExpand} /> : null}
    <IconButton icon={<X size={15} />} label={t('close')} onClick={onBack} />
  </> : null

  if (!detail) return <Root className="ora-detail" data-presentation={presentation} aria-label={label}>
    <header className="ora-detail__header">
      {drawer ? null : <IconButton icon={<ArrowLeft size={16} />} label={t('backToBoard')} onClick={onBack} />}
      <span className="ora-detail__actions">{closing}</span>
    </header>
    <p className="ora-detail__empty">{t('loadingDetails')}</p>
  </Root>

  const props: PartProps = { task, detail, busy, mutate }
  const externalUrl = detail.item.externalUrl
  const sourceNumber = /^[#!]\d/.test(task.sourceLabel) ? task.sourceLabel : task.shortId
  return <Root className="ora-detail" data-presentation={presentation} aria-label={label}>
    <header className="ora-detail__header">
      {drawer ? null : <IconButton icon={<ArrowLeft size={16} />} label={t('backToBoard')} onClick={onBack} />}
      <Swap swapKey={task.state} align="start"><StatusMenu {...props} /></Swap>
      {drawer ? <span className="ora-detail__spacer" /> : <span className="ora-detail__source" title={task.repository ? repositoryPath(task.repository) : undefined}>
        {task.repository ? <><ProviderIcon task={task} /><span>{repositoryName(task.repository)}</span></> : null}
        <span className="ora-detail__number">{sourceNumber}</span>
      </span>}
      <span className="ora-detail__actions">
        {externalUrl ? <IconButton icon={<ExternalLink size={15} />} label={t('openOnProvider', { provider: providerName(task.provider) })} tooltipLabel={t('openOnProvider', { provider: providerName(task.provider) })} onClick={() => void oratorioHost().navigation.openExternal(externalUrl)} /> : null}
        <IconButton icon={<MessageSquareText size={15} />} label={t('actionOpenThread')} tooltipLabel={t('actionOpenThread')} disabled={!task.run?.threadAvailable} onClick={onOpenThread} />
        <Swap swapKey={task.state}><PrimaryAction {...props} /></Swap>
        {closing}
      </span>
    </header>
    <section className="ora-detail__content dc-scrollbar-stable">
      <div className="ora-detail__page">
        <article className="ora-detail__main">
          <h1 className="ora-detail__title">{task.title}</h1>
          {task.branch ? <p className="ora-meta-line"><GitBranch size={14} aria-hidden="true" /><code>{task.branch}</code></p> : null}
          {task.body ? <Markdown content={task.body} /> : <p className="ora-detail__note">{t('noDescription')}</p>}
          {drawer ? <Rail {...props} sourceNumber={sourceNumber} /> : null}
          <Activity {...props} />
        </article>
        {drawer ? null : <Rail {...props} />}
      </div>
    </section>
  </Root>
}

function Swap({ swapKey, align = 'end', children }: { swapKey: string; align?: 'start' | 'end'; children: ReactNode }) {
  const inner = useRef<HTMLSpanElement>(null)
  const [width, setWidth] = useState<number | null>(null)
  const [moving, setMoving] = useState(false)
  useLayoutEffect(() => {
    const node = inner.current
    if (!node) return
    let last: number | null = null
    const measure = () => {
      const next = node.offsetWidth
      if (last !== null && last !== next) setMoving(true)
      last = next
      setWidth(next)
    }
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(node)
    return () => observer.disconnect()
  }, [])
  return <span className="ora-swap" data-align={align} data-moving={moving || undefined} style={width === null ? undefined : { width }} onTransitionEnd={(event) => { if (event.target === event.currentTarget) setMoving(false) }}>
    <span ref={inner} className="ora-swap__inner"><span key={swapKey} className="ora-swap__item">{children}</span></span>
  </span>
}

function StatusMenu({ task, busy, mutate }: PartProps) {
  const { t } = useOratorioI18n()
  const items = task.state === 'archived'
    ? task.capabilities.reopen ? [{ label: t('actionReopen'), icon: <RotateCcw size={14} />, disabled: busy !== null, onSelect: () => void mutate('reopen', oratorioClient.itemAction(task.id, 'reopen'), t('toastReopened')) }] : []
    : task.capabilities.archive ? [{ label: t('actionArchive'), icon: <Archive size={14} />, disabled: busy !== null, onSelect: () => void mutate('archive', oratorioClient.itemAction(task.id, 'archive'), t('toastArchived')) }] : []
  const label = <><StateIcon task={task} /><span>{t(taskStateKey(task))}</span></>
  if (!items.length) return <span className="ora-status-button" data-static="true">{label}</span>
  return <Menu items={items}>
    <button type="button" className="ora-status-button" aria-label={t('changeStatus')}>{label}<ChevronDown size={14} aria-hidden="true" /></button>
  </Menu>
}

function StateIcon({ task }: { task: OratorioTask }) {
  const icon = task.state === 'approved' ? <CircleCheck size={15} />
    : task.state === 'rejected' ? <CircleX size={15} />
      : task.state === 'archived' ? <Archive size={15} />
        : task.state === 'failed' ? <AlertCircle size={15} />
          : task.state === 'discovered' ? <CircleDashed size={15} /> : <CircleDot size={15} />
  return <span className="ora-state-icon" data-tone={taskStateTone(task)} aria-hidden="true">{icon}</span>
}

const START_ACTIONS: Array<{ capability: 'implement' | 'autoTarget' | 'reviewOnly'; label: OratorioMessageKey; workMode: RunDto['purpose']; deliveryPolicy: RunDto['deliveryPolicy'] }> = [
  { capability: 'implement', label: 'actionImplement', workMode: 'implementation', deliveryPolicy: 'manualDelivery' },
  { capability: 'autoTarget', label: 'actionAutoTarget', workMode: 'implementation', deliveryPolicy: 'autoPr' },
  { capability: 'reviewOnly', label: 'actionReviewOnly', workMode: 'reviewAnalysis', deliveryPolicy: 'manualDelivery' },
]

function dispatchRun({ task, mutate }: PartProps, key: string, workMode: RunDto['purpose'], deliveryPolicy: RunDto['deliveryPolicy'], toast: string): void {
  void mutate(key, oratorioClient.itemAction(task.id, 'dispatch', { mode: 'appServer', workMode, deliveryPolicy }), toast)
}

function confirmCancelRun({ task, mutate }: PartProps, t: OratorioTranslate): void {
  void oratorioHost().ui.confirm({ title: t('actionCancelRun'), message: t('cancelRunMessage', { task: task.shortId }), confirmLabel: t('actionCancelRun'), danger: true })
    .then((confirmed) => { if (confirmed) void mutate('cancel-run', oratorioClient.itemAction(task.id, 'cancel-run', {}), t('toastRunCancelled')) })
}

function PrimaryAction(props: PartProps) {
  const { task, detail, busy, mutate } = props
  const { t } = useOratorioI18n()
  const run = detail.runs.at(-1)
  if (task.state === 'discovered') {
    const starts = START_ACTIONS.filter((action) => task.capabilities[action.capability])
    if (!starts.length) return null
    const start = (action: (typeof START_ACTIONS)[number]) => dispatchRun(props, action.capability, action.workMode, action.deliveryPolicy, t('toastWorkQueued'))
    if (starts.length === 1) return <Button variant="primary" size="sm" loading={busy === starts[0].capability} disabled={busy !== null} onClick={() => start(starts[0])}>{t(starts[0].label)}</Button>
    return <SplitButton
      variant="primary"
      label={t(starts[0].label)}
      menuLabel={t('moreStartOptions')}
      disabled={busy !== null}
      onClick={() => start(starts[0])}
      items={starts.slice(1).map((action) => ({ label: t(action.label), onSelect: () => start(action) }))}
    />
  }
  if ((task.state === 'dispatching' || task.state === 'running') && task.capabilities.cancelRun) {
    return <Button variant="primary" size="sm" loading={busy === 'cancel-run'} disabled={busy !== null} onClick={() => confirmCancelRun(props, t)}>{t('actionCancelRun')}</Button>
  }
  if (task.state === 'failed' && task.capabilities.retry && run) {
    return <Button variant="primary" size="sm" loading={busy === 'retry'} disabled={busy !== null} onClick={() => dispatchRun(props, 'retry', run.purpose, run.deliveryPolicy, t('toastRetryQueued'))}>{t('retry')}</Button>
  }
  if (task.state === 'awaiting-review' && task.capabilities.decide) return <ReviewPopover {...props} />
  if ((task.state === 'approved' || task.state === 'rejected') && task.capabilities.reReview) {
    return <Button variant="primary" size="sm" loading={busy === 're-review'} disabled={busy !== null} onClick={() => void mutate('re-review', oratorioClient.itemAction(task.id, 'rereview', {}), t('toastWorkQueued'))}>{t('actionReReview')}</Button>
  }
  return null
}

const DECISIONS: Array<{ id: Decision; label: OratorioMessageKey; toast: OratorioMessageKey }> = [
  { id: 'approve', label: 'actionApprove', toast: 'toastApproved' },
  { id: 'request-changes', label: 'actionRequestChanges', toast: 'toastChangesRequested' },
  { id: 'reject', label: 'actionReject', toast: 'toastRejected' },
]

function ReviewPopover({ task, busy, mutate }: PartProps) {
  const { t } = useOratorioI18n()
  const [open, setOpen] = useState(false)
  const [decision, setDecision] = useState<Decision>('approve')
  const [note, setNote] = useState('')
  const anchor = useRef<HTMLSpanElement>(null)

  useEffect(() => {
    if (!open) return
    const onPointer = (event: PointerEvent) => { if (!anchor.current?.contains(event.target as Node)) setOpen(false) }
    document.addEventListener('pointerdown', onPointer)
    return () => document.removeEventListener('pointerdown', onPointer)
  }, [open])

  const close = () => { setOpen(false); anchor.current?.querySelector('button')?.focus() }
  const blocked = busy !== null || (decision === 'request-changes' && !note.trim())
  const submit = () => {
    if (blocked) return
    const chosen = DECISIONS.find((entry) => entry.id === decision)!
    void mutate(decision, oratorioClient.itemAction(task.id, decision, { body: note.trim() }), t(chosen.toast)).then(() => { setOpen(false); setNote(''); setDecision('approve') })
  }

  return <span className="ora-popover-anchor" ref={anchor}>
    <Button variant="primary" size="sm" className="ora-review-trigger" aria-haspopup="dialog" aria-expanded={open} onClick={() => setOpen((current) => !current)}>{t('actionReview')}<ChevronDown size={13} aria-hidden="true" /></Button>
    {open ? <div className="ora-popover" role="dialog" aria-label={t('submitReview')} onKeyDown={(event) => { if (event.key === 'Escape') { event.stopPropagation(); close() } }}>
      <div className="ora-popover__choices" role="radiogroup" aria-label={t('submitReview')}>
        {DECISIONS.map((entry) => <label key={entry.id} className="ora-popover__choice">
          <input type="radio" name="ora-review-decision" checked={decision === entry.id} onChange={() => setDecision(entry.id)} />
          <span>{t(entry.label)}</span>
        </label>)}
      </div>
      <Textarea
        autoFocus
        value={note}
        onChange={(event) => setNote(event.target.value)}
        onKeyDown={(event) => { if (event.key === 'Enter' && (event.metaKey || event.ctrlKey)) { event.preventDefault(); submit() } }}
        placeholder={decision === 'request-changes' ? t('feedbackPlaceholder') : t('optionalComment')}
        aria-label={decision === 'request-changes' ? t('requestChangesFeedback') : t('optionalComment')}
      />
      <div className="ora-popover__footer">
        <Button variant="ghost" size="sm" onClick={close}>{t('cancel')}</Button>
        <Button variant={decision === 'reject' ? 'danger' : 'primary'} size="sm" loading={busy === decision} disabled={blocked} onClick={submit}>{t('submitReview')}</Button>
      </div>
    </div> : null}
  </span>
}

type TimelineEntry = { key: string; at: number; node: ReactNode }

function timeOf(value: string | null | undefined): number {
  const parsed = value ? Date.parse(value) : Number.NaN
  return Number.isNaN(parsed) ? Number.MAX_SAFE_INTEGER : parsed
}

function runTime(detail: ItemDetailResponse, run: RunDto): number {
  const results = [...(detail.reviewDrafts ?? []), ...(detail.implementationDrafts ?? []), ...(detail.followUpDrafts ?? [])]
    .filter((draft) => draft.runId === run.runId).map((draft) => timeOf(draft.createdAt) - 1)
  return Math.min(timeOf(run.startedAt ?? run.completedAt), ...results)
}

function Activity(props: PartProps) {
  const { task, detail, busy, mutate } = props
  const { t } = useOratorioI18n()
  const [comment, setComment] = useState('')
  const decisions = detail.decisions ?? []
  const decisionComments = new Set(decisions.map((decision) => decision.commentId).filter(Boolean))
  const comments = detail.comments ?? []
  const latestRun = detail.runs.at(-1)
  const discussionBusy = Boolean(detail.discussionTurns?.some((turn) => turn.status === 'pending' || turn.status === 'running'))
  const entries: TimelineEntry[] = [
    ...detail.runs.map((run) => ({ key: `run-${run.runId}`, at: runTime(detail, run), node: <RunEvent task={task} run={run} latest={run === latestRun} /> })),
    ...(detail.reviewDrafts ?? []).filter((draft) => draft.status !== 'discarded').map((draft) => ({ key: `review-${draft.draftId}`, at: timeOf(draft.createdAt), node: <ReviewDraftCard draft={draft} write={(detail.sourceWrites ?? []).find((write) => write.writeId === draft.sourceWriteId)} busy={busy} mutate={mutate} /> })),
    ...(detail.implementationDrafts ?? []).map((draft) => ({ key: `impl-${draft.draftId}`, at: timeOf(draft.createdAt), node: <ImplementationCard draft={draft} busy={busy} mutate={mutate} /> })),
    ...(detail.followUpDrafts ?? []).filter((draft) => draft.status !== 'discarded').map((draft) => ({ key: `follow-${draft.draftId}`, at: timeOf(draft.createdAt), node: <FollowUpCard draft={draft} busy={busy} mutate={mutate} /> })),
    ...comments.filter((entry) => !decisionComments.has(entry.commentId)).map((entry) => ({ key: `comment-${entry.commentId}`, at: timeOf(entry.createdAt), node: <CommentEvent comment={entry} /> })),
    ...decisions.map((decision) => ({ key: `decision-${decision.decisionId}`, at: timeOf(decision.createdAt), node: <DecisionEvent decision={decision} comment={comments.find((entry) => entry.commentId === decision.commentId)} /> })),
    ...(detail.sourceWrites ?? []).filter((write) => write.status === 'failed' && !(detail.reviewDrafts ?? []).some((draft) => draft.sourceWriteId === write.writeId)).map((write) => ({ key: `write-${write.writeId}`, at: timeOf(write.updatedAt), node: <WriteFailedEvent write={write} busy={busy} mutate={mutate} /> })),
    ...(detail.discussionTurns ?? []).filter((turn) => turn.status === 'pending' || turn.status === 'running').map((turn) => ({ key: `turn-${turn.discussionTurnId}`, at: timeOf(turn.startedAt ?? turn.createdAt), node: <ReplyingEvent turn={turn} /> })),
  ].sort((left, right) => left.at - right.at)

  function addComment(): void {
    const body = comment.trim()
    if (!body || busy) return
    void mutate('comment', oratorioClient.addComment(task.id, body, task.round), t('toastCommentAdded')).then(() => setComment(''))
  }

  return <section className="ora-activity" aria-label={t('sectionActivity')}>
    <h2>{t('sectionActivity')}</h2>
    {entries.length ? <ol className="ora-timeline">{entries.map((entry) => <li key={entry.key}>{entry.node}</li>)}</ol> : null}
    <OratorioCommentCard
      value={comment}
      onChange={setComment}
      placeholder={t('discussionPlaceholder')}
      ariaLabel={t('discussionReply')}
      onSubmit={addComment}
      actions={<>
        <Button variant="secondary" size="sm" disabled={!comment.trim()} loading={busy === 'comment'} onClick={addComment}>{t('addComment')}</Button>
        <Button variant="primary" size="sm" iconLeft={<Send size={13} />} disabled={!comment.trim() || discussionBusy} loading={busy === 'ask'} onClick={() => { const body = comment.trim(); void mutate('ask', oratorioClient.askAgent(task.id, body, task.round), t('toastDiscussionStarted')).then(() => setComment('')) }}>{t('askAgent')}</Button>
      </>}
    />
  </section>
}

function Event({ icon, tone = 'neutral', children, time, action, body }: { icon: ReactNode; tone?: Tone; children: ReactNode; time?: string | null; action?: ReactNode; body?: ReactNode }) {
  const { relativeTime } = useOratorioI18n()
  return <div className="ora-tile">
    <div className="ora-tile__head">
      <span className="ora-tile__icon" data-tone={tone} aria-hidden="true">{icon}</span>
      <span className="ora-tile__text">{children}</span>
      {time ? <Time value={time} label={relativeTime(time)} /> : null}
      {action}
    </div>
    {body ? <div className="ora-tile__body">{body}</div> : null}
  </div>
}

function RunEvent({ task, run, latest }: { task: OratorioTask; run: RunDto; latest: boolean }) {
  const { t } = useOratorioI18n()
  const active = run.status === 'queued' || run.status === 'dispatching' || run.status === 'running'
  const failed = run.status === 'failed' || run.status === 'timedOut'
  const icon = active ? <Spinner size={14} label={t(runStatusKey(run.status))} /> : run.status === 'succeeded' ? <Check size={14} /> : failed ? <X size={14} /> : <CircleDashed size={14} />
  const detailText = active ? (latest ? task.run?.activity : null) || run.statusMessage : failed ? run.errorMessage : run.summary || run.statusMessage
  return <Event icon={icon} tone={failed ? 'error' : 'neutral'} time={run.completedAt ?? run.startedAt} body={detailText ? <p className={failed ? 'ora-tile__note ora-tile__note--error' : 'ora-tile__note'}>{detailText}</p> : null}>
    {t(run.purpose === 'implementation' ? 'purposeImplementation' : 'purposeReview')}<span className="ora-tile__status">{t(runStatusKey(run.status))}</span>
  </Event>
}

function DecisionEvent({ decision, comment }: { decision: DecisionDto; comment?: CommentDto }) {
  const { t } = useOratorioI18n()
  const icon = decision.decision === 'approve' ? <Check size={14} /> : decision.decision === 'requestChanges' ? <FileDiff size={14} /> : decision.decision === 'reject' ? <X size={14} /> : <RotateCcw size={14} />
  const tone: Tone = decision.decision === 'approve' ? 'success' : decision.decision === 'requestChanges' ? 'error' : 'neutral'
  const body = comment?.body || decision.body
  return <Event icon={icon} tone={tone} time={decision.createdAt} body={body ? <Markdown content={body} /> : null}>
    {t(decisionEventKey(decision.decision), { author: decision.authorName })}
  </Event>
}

function WriteFailedEvent({ write, busy, mutate }: { write: SourceWriteDto; busy: string | null; mutate: Mutate }) {
  const { t } = useOratorioI18n()
  const key = `write-${write.writeId}`
  return <Event
    icon={<AlertTriangle size={14} />}
    tone="warning"
    time={write.updatedAt}
    action={<Button variant="secondary" size="sm" loading={busy === key} disabled={busy !== null} onClick={() => void mutate(key, oratorioClient.retrySourceWrite(write.writeId), t('toastWriteRetried'))}>{t('retry')}</Button>}
    body={write.errorMessage ? <p className="ora-tile__note">{write.errorMessage}</p> : null}
  >{t('writeFailed', { provider: providerName(write.source) })}</Event>
}

function ReplyingEvent({ turn }: { turn: DiscussionTurnDto }) {
  const { t } = useOratorioI18n()
  return <Event icon={<Spinner size={14} label={t('oratorioReplying')} />} time={turn.startedAt ?? turn.createdAt}>{t('oratorioReplying')}</Event>
}

function CommentEvent({ comment }: { comment: CommentDto }) {
  const { t } = useOratorioI18n()
  const agent = comment.authorKind.toLowerCase() === 'agent'
  const author = agent ? t('oratorio') : comment.source ? comment.authorName || comment.authorKind : t('you')
  const avatar = agent ? <img src={oratorioIcon} alt="" /> : comment.source ? author.slice(0, 1).toLocaleUpperCase() : <UserAvatar size={24} />
  return <Event icon={<span className="ora-avatar" data-role={agent ? 'agent' : 'operator'}>{avatar}</span>} time={comment.createdAt} body={<Markdown content={comment.body} />}>{author}</Event>
}

function Card({ title, meta, status, actions, children }: { title: ReactNode; meta?: string; status?: ReactNode; actions?: ReactNode; children: ReactNode }) {
  return <article className="ora-tile ora-tile--card">
    <header className="ora-tile__head">
      <span className="ora-tile__icon" aria-hidden="true"><span className="ora-avatar" data-role="agent"><img src={oratorioIcon} alt="" /></span></span>
      <span className="ora-tile__text">{title}{meta ? <span className="ora-tile__status">{meta}</span> : null}</span>
      {status}
      {actions ? <span className="ora-tile__actions">{actions}</span> : null}
    </header>
    {children}
  </article>
}

function StatusText({ tone, children }: { tone: Tone; children: ReactNode }) {
  return <span className="ora-status-text" data-tone={tone}>{children}</span>
}

function ReviewDraftCard({ draft, write, busy, mutate }: { draft: ReviewDraftDto; write?: SourceWriteDto; busy: string | null; mutate: Mutate }) {
  const { t } = useOratorioI18n()
  const open = draft.comments.filter((finding) => finding.resolutionState === 'open')
  const resolved = draft.comments.filter((finding) => finding.resolutionState !== 'open')
  const meta = [
    countLabel(t, open.filter((finding) => finding.severity === 'major').length, 'countMajor', 'countMajor'),
    countLabel(t, open.filter((finding) => finding.severity === 'minor').length, 'countMinor', 'countMinor'),
    countLabel(t, open.filter((finding) => finding.severity !== 'major' && finding.severity !== 'minor').length, 'countSuggestion', 'countSuggestions'),
  ].filter(Boolean).join(' · ')
  const failed = draft.status === 'publishFailed'
  const actions = failed ? <>
    <Button variant="ghost" size="sm" iconLeft={<Trash2 size={13} />} loading={busy === `discard-${draft.draftId}`} disabled={busy !== null} onClick={() => void mutate(`discard-${draft.draftId}`, oratorioClient.discardReviewDraft(draft.draftId), t('toastDraftDiscarded'))}>{t('discard')}</Button>
    <Button variant="secondary" size="sm" loading={busy === `publish-${draft.draftId}`} disabled={busy !== null} onClick={() => void mutate(`publish-${draft.draftId}`, oratorioClient.publishReviewDraft(draft.draftId), t('toastReviewPublished'))}>{t('retry')}</Button>
  </> : null
  const status = draft.status === 'published' ? <StatusText tone="success">{t('draftPublished')}</StatusText>
    : failed ? <StatusText tone="error">{t('draftPublishFailed')}</StatusText> : null
  return <Card title={t('reviewDraftTitle')} meta={meta} status={status} actions={actions}>
    <div className="ora-tile__section">
      <Markdown content={draft.summaryBody} />
      {failed && write?.errorMessage ? <p className="ora-detail__error">{write.errorMessage}</p> : null}
      {draft.warnings.map((warning) => <p className="ora-detail__error" key={warning}>{warning}</p>)}
    </div>
    {open.map((finding) => <Finding key={finding.draftCommentId} draft={draft} finding={finding} busy={busy} mutate={mutate} />)}
    {resolved.map((finding) => <ResolvedFinding key={finding.draftCommentId} draft={draft} finding={finding} busy={busy} mutate={mutate} />)}
  </Card>
}

function Finding({ draft, finding, busy, mutate }: { draft: ReviewDraftDto; finding: ReviewDraftCommentDto; busy: string | null; mutate: Mutate }) {
  const { t } = useOratorioI18n()
  const key = `finding-${finding.draftCommentId}`
  return <section className="ora-finding">
    <header>
      <span className="ora-finding__severity" data-severity={finding.severity}>{t(severityKey(finding.severity))}</span>
      <strong>{finding.title}</strong>
      <code title={`${finding.path}:${finding.line}`}>{finding.path}:{finding.line}</code>
    </header>
    <Markdown content={finding.body} />
    {finding.suggestionOriginal != null && finding.suggestionReplacement != null ? <InlineDiff filePath={finding.path} line={finding.line} before={finding.suggestionOriginal} after={finding.suggestionReplacement} /> : null}
    {draft.status === 'published' ? <footer>
      <Button variant="ghost" size="sm" disabled={busy !== null} onClick={() => void mutate(key, oratorioClient.resolveFinding(draft.draftId, finding.draftCommentId, 'dismissed'), t('toastFindingDismissed'))}>{t('dismiss')}</Button>
      <Button variant="secondary" size="sm" iconLeft={<Check size={13} />} disabled={busy !== null} onClick={() => void mutate(key, oratorioClient.resolveFinding(draft.draftId, finding.draftCommentId, 'fixed'), t('toastFindingResolved'))}>{t('markFixed')}</Button>
    </footer> : null}
  </section>
}

function ResolvedFinding({ draft, finding, busy, mutate }: { draft: ReviewDraftDto; finding: ReviewDraftCommentDto; busy: string | null; mutate: Mutate }) {
  const { t } = useOratorioI18n()
  return <section className="ora-finding ora-finding--resolved">
    <header>
      <Check size={14} aria-hidden="true" />
      <strong>{finding.title}</strong>
      <span className="ora-finding__resolution">{t(finding.resolutionKind === 'dismissed' ? 'resolutionDismissed' : 'resolutionFixed')}</span>
      <Button variant="ghost" size="sm" disabled={busy !== null} onClick={() => void mutate(`finding-${finding.draftCommentId}`, oratorioClient.reopenFinding(draft.draftId, finding.draftCommentId), t('toastFindingReopened'))}>{t('actionReopen')}</Button>
    </header>
  </section>
}

function ImplementationCard({ draft, busy, mutate }: { draft: ImplementationDraftDto; busy: string | null; mutate: Mutate }) {
  const { t } = useOratorioI18n()
  const deliverable = draft.status === 'draft' || draft.status === 'deliveryFailed'
  const status = draft.status === 'delivered' ? <StatusText tone="success">{t('implementationDelivered')}</StatusText>
    : draft.status === 'deliveryFailed' ? <StatusText tone="error">{t('implementationFailed')}</StatusText> : null
  const actions = deliverable || draft.pullRequestUrl ? <>
    {draft.pullRequestUrl ? <Button variant="ghost" size="sm" iconLeft={<ExternalLink size={13} />} onClick={() => void oratorioHost().navigation.openExternal(draft.pullRequestUrl!)}>{t('openPullRequest')}</Button> : null}
    {deliverable ? <Button variant="primary" size="sm" loading={busy === `deliver-${draft.draftId}`} disabled={busy !== null} onClick={() => void mutate(`deliver-${draft.draftId}`, oratorioClient.deliverImplementation(draft.draftId), t('toastDelivered'))}>{t('deliver')}</Button> : null}
  </> : null
  return <Card title={draft.proposedPrTitle || t('purposeImplementation')} status={status} actions={actions}>
    <div className="ora-tile__section">
      {draft.summary ? <Markdown content={draft.summary} /> : null}
      {draft.branchName || draft.commitSha ? <p className="ora-meta-line">
        <GitBranch size={14} aria-hidden="true" />
        {draft.branchName ? <code>{draft.branchName}</code> : null}
        {draft.commitSha ? <code>{draft.commitSha.slice(0, 7)}</code> : null}
      </p> : null}
      {draft.tests.length ? <p className="ora-meta-line">{t('factTests')}: {draft.tests.join(', ')}</p> : null}
      {draft.errorMessage ? <p className="ora-detail__error">{draft.errorMessage}</p> : null}
    </div>
  </Card>
}

function FollowUpCard({ draft, busy, mutate }: { draft: FollowUpDraftDto; busy: string | null; mutate: Mutate }) {
  const { t } = useOratorioI18n()
  const [value, setValue] = useState(draft.body)
  const editable = draft.status === 'draft'
  const actions = editable ? <>
    <Button variant="ghost" size="sm" disabled={busy !== null} onClick={() => void mutate(`follow-discard-${draft.draftId}`, oratorioClient.discardFollowUp(draft.draftId), t('toastFollowUpDiscarded'))}>{t('discard')}</Button>
    {value !== draft.body ? <Button variant="secondary" size="sm" disabled={busy !== null} onClick={() => void mutate(`follow-save-${draft.draftId}`, oratorioClient.updateFollowUp(draft.draftId, { body: value }), t('toastFollowUpSaved'))}>{t('save')}</Button> : null}
    <Button variant="primary" size="sm" loading={busy === `follow-create-${draft.draftId}`} disabled={busy !== null} onClick={() => void mutate(`follow-create-${draft.draftId}`, oratorioClient.createTaskFromFollowUp(draft.draftId), t('localTaskCreated'))}>{t('createLocalTask')}</Button>
  </> : null
  return <Card title={draft.title} meta={t('followUpLabel')} status={draft.status === 'created' ? <StatusText tone="success">{t('followUpCreated')}</StatusText> : null} actions={actions}>
    <div className="ora-tile__section">
      {editable ? <Textarea value={value} onChange={(event) => setValue(event.target.value)} aria-label={t('followUpBody', { title: draft.title })} /> : <Markdown content={draft.body} />}
    </div>
  </Card>
}

function Rail({ task, detail, busy, mutate, sourceNumber }: PartProps & { sourceNumber?: string }) {
  const { t, relativeTime } = useOratorioI18n()
  const run = detail.runs.at(-1)
  const files = (detail.implementationDrafts ?? []).at(-1)?.changedFiles ?? []
  const syncedAt = detail.sourceSnapshot?.syncedAt ?? task.syncedAt ?? null
  const active = run && (run.status === 'queued' || run.status === 'dispatching' || run.status === 'running')
  const runTone: Tone = !run ? 'neutral' : run.status === 'succeeded' ? 'success' : run.status === 'failed' || run.status === 'timedOut' ? 'error' : active ? 'info' : 'neutral'
  return <aside className="ora-rail">
    <RailSection title={t('oratorio')}>
      <p className="ora-rail__line">
        {active ? <Spinner size={14} label={t(runStatusKey(run.status))} /> : <span className="ora-indicator"><span className="ora-indicator__dot" data-tone={runTone} /></span>}
        <span>{run ? `${t(run.purpose === 'implementation' ? 'purposeImplementation' : 'purposeReview')} · ${t(runStatusKey(run.status))}` : t('runNotStarted')}</span>
      </p>
    </RailSection>
    {files.length ? <RailSection title={t('sectionChanges')}>
      <ul className="ora-rail__files">
        {files.slice(0, 6).map((file) => <li key={file} title={file}><code>{file.split('/').at(-1)}</code></li>)}
        {files.length > 6 ? <li className="ora-rail__more">{t('moreFiles', { n: files.length - 6 })}</li> : null}
      </ul>
    </RailSection> : null}
    <RailSection title={t('assignee')}><p className="ora-rail__line">{task.assignee ?? t('unassigned')}</p></RailSection>
    {task.labels.length ? <RailSection title={t('sectionLabels')}><p className="ora-rail__line">{task.labels.join(', ')}</p></RailSection> : null}
    {task.repository || syncedAt ? <RailSection title={t('sectionSource')}>
      {task.repository ? <p className="ora-rail__line ora-rail__path"><ProviderIcon task={task} /><span>{repositoryPath(task.repository)}{sourceNumber ? <span className="ora-detail__number"> {sourceNumber}</span> : null}</span></p> : null}
      {syncedAt ? <p className="ora-rail__line ora-rail__muted">
        <Time value={syncedAt} label={t('metaSynced', { time: relativeTime(syncedAt) })} />
        <IconButton icon={<RefreshCw size={13} />} label={t('refresh')} tooltipLabel={t('refresh')} disabled={busy !== null} onClick={() => void mutate('source-sync', oratorioClient.syncSourceDetails(task.id), t('toastSourceRefreshed'))} />
      </p> : null}
    </RailSection> : null}
  </aside>
}

function RailSection({ title, children }: { title: string; children: ReactNode }) {
  return <section className="ora-rail__section"><h2>{title}</h2>{children}</section>
}

function Time({ value, label }: { value: string; label: string }) {
  return <time className="ora-time" dateTime={value} title={new Date(value).toLocaleString()}>{label}</time>
}

function providerName(provider: string): string {
  return provider === 'gitlab' ? 'GitLab' : provider === 'github' ? 'GitHub' : provider
}

function countLabel(t: OratorioTranslate, n: number, one: OratorioMessageKey, other: OratorioMessageKey): string {
  return n ? t(n === 1 ? one : other, { n }) : ''
}

function runStatusKey(status: RunDto['status']): OratorioMessageKey {
  switch (status) {
    case 'queued': case 'dispatching': return 'runQueued'
    case 'running': return 'runRunning'
    case 'succeeded': return 'runSucceeded'
    case 'failed': return 'stateFailed'
    case 'cancelled': return 'runCancelled'
    default: return 'runTimedOut'
  }
}

function severityKey(severity: string): OratorioMessageKey {
  if (severity === 'major') return 'severityMajor'
  if (severity === 'minor') return 'severityMinor'
  return 'severitySuggestion'
}

function decisionEventKey(decision: DecisionDto['decision']): OratorioMessageKey {
  switch (decision) {
    case 'approve': return 'eventApproved'
    case 'requestChanges': return 'eventRequestedChanges'
    case 'reject': return 'eventRejected'
    case 'reopen': return 'eventReopened'
    default: return 'eventReReview'
  }
}
