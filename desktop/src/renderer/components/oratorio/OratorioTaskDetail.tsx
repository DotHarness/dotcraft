import { useEffect, useState, type ReactNode } from 'react'
import { Archive, ArrowLeft, Check, CircleDot, ExternalLink, FileText, GitBranch, GitPullRequest, RefreshCw, RotateCcw, Send, Trash2 } from 'lucide-react'
import { oratorioClient } from './oratorio-client'
import type { FollowUpDraftDto, ItemDetailResponse, ReviewDraftCommentDto, ReviewDraftDto } from './oratorio-contracts'
import { mapComment, mapItemDetail } from './oratorio-mappers'
import type { OratorioTask, TaskStage } from './oratorio-model'
import { OratorioInlineDiff } from './OratorioInlineDiff'
import { OratorioStageNav } from './OratorioStageNav'
import { GithubGlyph, GitlabGlyph } from './ProviderGlyphs'
import { Button, IconButton, Textarea } from './ui'
import { addToast } from '../../stores/toastStore'

export function OratorioTaskDetail({ task, initialStage = 'review', initialFocus, onBack, onOpenThread, onTaskChange, onStageChange }: {
  task: OratorioTask; initialStage?: TaskStage; initialFocus?: 'discussion'; onBack: () => void; onOpenThread: () => void
  onTaskChange: (task: OratorioTask) => void; onStageChange?: (stage: TaskStage) => void
}) {
  const [stage, setStage] = useState<TaskStage>(initialStage)
  const [busy, setBusy] = useState<string | null>(null)
  const detail = task.detail

  useEffect(() => setStage(initialStage), [initialStage])

  async function mutate(key: string, operation: Promise<ItemDetailResponse>, message: string): Promise<void> {
    if (busy) return
    setBusy(key)
    try {
      onTaskChange(mapItemDetail(await operation))
      addToast(message, 'success')
    } catch (error) {
      addToast(error instanceof Error ? error.message : 'Oratorio rejected the action.', 'error')
    } finally {
      setBusy(null)
    }
  }

  if (!detail) return <main className="ora-detail"><p className="ora-detail__pending-copy">Loading task details…</p></main>

  return <main className="ora-detail" aria-label={`Task details for ${task.shortId}`}>
    <header className="ora-detail__header">
      <IconButton icon={<ArrowLeft size={16} />} label="Back to board" onClick={onBack} />
      <div className="ora-detail__identity"><span className="ora-chip"><ProviderIcon task={task} />{task.repository}</span><span className="ora-chip"><KindIcon task={task} />{task.shortId}</span><strong>{task.title}</strong></div>
      <Button variant="secondary" size="sm" iconLeft={<ExternalLink size={14} />} onClick={onOpenThread} disabled={!task.run?.threadAvailable}>Open thread</Button>
    </header>
    <OratorioStageNav task={task} selectedStage={stage} onStageChange={(next) => { setStage(next); onStageChange?.(next) }} />
    <section className="ora-detail__content" id={`ora-stage-panel-${stage}`} role="tabpanel" aria-labelledby={`ora-stage-tab-${stage}`}>
      {stage === 'intake' ? <IntakeStage task={task} detail={detail} busy={busy} mutate={mutate} /> : null}
      {stage === 'analysis' ? <DiagnosticsStage task={task} detail={detail} busy={busy} mutate={mutate} onOpenThread={onOpenThread} /> : null}
      {stage === 'review' ? <ReviewStage task={task} detail={detail} busy={busy} focusDiscussion={initialFocus === 'discussion'} mutate={mutate} /> : null}
      {stage === 'decision' ? <DecisionStage task={task} detail={detail} busy={busy} mutate={mutate} /> : null}
      {stage === 'closed' ? <ClosedStage task={task} detail={detail} busy={busy} mutate={mutate} /> : null}
    </section>
  </main>
}

type Mutate = (key: string, operation: Promise<ItemDetailResponse>, message: string) => Promise<void>

function IntakeStage({ task, detail, busy, mutate }: { task: OratorioTask; detail: ItemDetailResponse; busy: string | null; mutate: Mutate }) {
  const snapshot = detail.sourceSnapshot
  return <div className="ora-detail__stack">
    <DetailSection title="Problem" description="The source problem and intended outcome."><p className="ora-detail__prose">{task.description || 'No description provided.'}</p><dl className="ora-facts"><Fact name="Source project" value={task.repository} /><Fact name="Assignee" value={task.assignee ?? 'Unassigned'} /><Fact name="Labels" value={task.labels.join(', ') || 'None'} /></dl></DetailSection>
    <DetailSection title="Source snapshot" description="Metadata captured by Oratorio." action={<Button variant="secondary" size="sm" iconLeft={<RefreshCw size={13} />} loading={busy === 'source-sync'} onClick={() => void mutate('source-sync', oratorioClient.syncSourceDetails(task.id), 'Source details refreshed')}>Refresh</Button>}>
      <dl className="ora-facts"><Fact name="Identifier" value={task.shortId} /><Fact name="Head SHA" value={snapshot?.headSha ?? task.headSha ?? 'Not reported'} /><Fact name="Synced" value={snapshot?.syncedAt ? new Date(snapshot.syncedAt).toLocaleString() : 'Not reported'} /></dl>
    </DetailSection>
    <DetailSection title="Source activity" description="Comments imported from the provider."><CommentList detail={detail} sourceOnly /></DetailSection>
  </div>
}

function DiagnosticsStage({ task, detail, busy, mutate, onOpenThread }: { task: OratorioTask; detail: ItemDetailResponse; busy: string | null; mutate: Mutate; onOpenThread: () => void }) {
  const run = detail.runs.at(-1)
  return <div className="ora-detail__stack">
    <DetailSection title="Latest run" description="Execution summary; the conversation remains in DotCraft." action={<Button variant="secondary" size="sm" iconLeft={<ExternalLink size={13} />} onClick={onOpenThread} disabled={!run?.threadId}>Open DotCraft thread</Button>}>
      {run ? <div className="ora-run-summary"><span className="ora-state-dot" data-tone={run.status === 'failed' ? 'error' : run.status === 'succeeded' ? 'success' : 'info'} /><span><strong>{run.statusMessage || run.summary || run.status}</strong><small>Attempt {run.attempt} · {run.runnerKind} · {run.purpose}</small></span></div> : <Empty>No run recorded.</Empty>}
    </DetailSection>
    {run ? <DetailSection title="Run diagnostics" description="Technical evidence without duplicating the transcript." action={task.state === 'failed' ? <Button variant="secondary" size="sm" loading={busy === 'retry'} onClick={() => void mutate('retry', oratorioClient.itemAction(task.id, 'dispatch', { mode: 'appServer', workMode: run.purpose, deliveryPolicy: run.deliveryPolicy }), 'Run queued for retry')}>Retry run</Button> : undefined}>
      <dl className="ora-facts"><Fact name="Workspace" value={run.baseWorkspacePath ?? 'Not reported'} /><Fact name="Worktree" value={run.worktreePath ?? 'Not required'} /><Fact name="Branch" value={run.worktreeBranch ?? 'Not created'} /><Fact name="Thread" value={run.threadId ?? 'Not attached'} /><Fact name="Turn" value={run.turnId ?? 'Not attached'} /><Fact name="Worktree state" value={run.worktreeStatus} /></dl>
      {run.errorMessage ? <p className="error-text">{run.errorMessage}</p> : null}
    </DetailSection> : null}
    <DetailSection title="Rounds and timeline"><div className="ora-draft-list">{detail.rounds?.map((round) => <article className="ora-timeline-card" key={round.roundId}><strong>Round {round.roundNumber} · {round.status}</strong><p>{round.summary || 'No summary'}</p><small>{new Date(round.createdAt).toLocaleString()}</small></article>)}{detail.timeline.map((event) => <article className="ora-timeline-card" key={event.eventId}><strong>{event.title}</strong>{event.body ? <p>{event.body}</p> : null}<small>{event.actorName} · {new Date(event.createdAt).toLocaleString()}</small></article>)}</div></DetailSection>
  </div>
}

function ReviewStage({ task, detail, busy, focusDiscussion, mutate }: { task: OratorioTask; detail: ItemDetailResponse; busy: string | null; focusDiscussion: boolean; mutate: Mutate }) {
  const [comment, setComment] = useState('')
  const [followUpEdits, setFollowUpEdits] = useState<Record<string, string>>({})
  const drafts = detail.reviewDrafts ?? []
  return <div className="ora-detail__stack">
    <DetailSection title="Review drafts" description="Resolve findings before publishing them to the source provider.">
      {drafts.length ? drafts.map((draft) => <ReviewDraftCard key={draft.draftId} draft={draft} busy={busy} mutate={mutate} />) : <Empty>No review draft for this task.</Empty>}
    </DetailSection>
    <DetailSection title="Implementation drafts" description="Delivery remains explicit until accepted by the operator.">
      {detail.implementationDrafts?.length ? detail.implementationDrafts.map((draft) => <article className="ora-draft-card" key={draft.draftId}><header><span className="ora-status">{draft.status}</span><strong>{draft.proposedPrTitle || draft.summary}</strong></header><p>{draft.summary}</p><dl className="ora-facts"><Fact name="Branch" value={draft.branchName ?? 'Pending'} /><Fact name="Commit" value={draft.commitSha ?? 'Pending'} /><Fact name="Changed files" value={draft.changedFiles.join(', ') || 'Not reported'} /><Fact name="Tests" value={draft.tests.join(', ') || 'Not reported'} /></dl>{draft.errorMessage ? <p className="error-text">{draft.errorMessage}</p> : null}{draft.status === 'draft' || draft.status === 'deliveryFailed' ? <div className="ora-section-actions"><Button variant="primary" size="sm" loading={busy === `deliver-${draft.draftId}`} onClick={() => void mutate(`deliver-${draft.draftId}`, oratorioClient.deliverImplementation(draft.draftId), 'Implementation delivered')}>Deliver</Button></div> : null}</article>) : <Empty>No implementation draft.</Empty>}
    </DetailSection>
    <DetailSection title="Follow-up drafts" description="Deferred work remains attached to the source task.">
      {detail.followUpDrafts?.length ? detail.followUpDrafts.map((draft) => <FollowUpCard key={draft.draftId} draft={draft} value={followUpEdits[draft.draftId] ?? draft.body} onChange={(value) => setFollowUpEdits((current) => ({ ...current, [draft.draftId]: value }))} busy={busy} mutate={mutate} />) : <Empty>No follow-up drafts.</Empty>}
    </DetailSection>
    <DetailSection className="ora-detail-section--discussion" title="Discussion" description="Add internal feedback or ask the Agent on the completed thread."><CommentList detail={detail} /><Textarea autoFocus={focusDiscussion} value={comment} onChange={(event) => setComment(event.target.value)} placeholder="Add feedback or ask the Agent…" aria-label="Discussion reply" /><div className="ora-section-actions"><Button variant="secondary" size="sm" disabled={!comment.trim()} loading={busy === 'comment'} onClick={() => { const body = comment.trim(); void mutate('comment', oratorioClient.addComment(task.id, body, task.round), 'Comment added').then(() => setComment('')) }}>Add comment</Button><Button variant="primary" size="sm" iconLeft={<Send size={13} />} disabled={!comment.trim() || detail.discussionTurns?.some((turn) => turn.status === 'pending' || turn.status === 'running')} loading={busy === 'ask'} onClick={() => { const body = comment.trim(); void mutate('ask', oratorioClient.askAgent(task.id, body, task.round), 'Agent discussion started').then(() => setComment('')) }}>Ask Agent</Button></div></DetailSection>
  </div>
}

function ReviewDraftCard({ draft, busy, mutate }: { draft: ReviewDraftDto; busy: string | null; mutate: Mutate }) {
  const [summary, setSummary] = useState(draft.summaryBody)
  return <article className="ora-draft-card ora-draft-card--editor"><header><span className="ora-status">{draft.status}</span><strong>{draft.majorCount} major · {draft.minorCount} minor · {draft.suggestionCount} suggestions</strong></header><Textarea value={summary} readOnly={draft.status !== 'draft' && draft.status !== 'publishFailed'} onChange={(event) => setSummary(event.target.value)} aria-label="Review summary" />
    <div className="ora-draft-list">{draft.comments.map((finding) => <FindingCard key={finding.draftCommentId} draft={draft} finding={finding} busy={busy} mutate={mutate} />)}</div>
    {draft.warnings.map((warning) => <p className="error-text" key={warning}>{warning}</p>)}
    {draft.status === 'draft' || draft.status === 'publishFailed' ? <div className="ora-section-actions"><Button variant="danger" size="sm" iconLeft={<Trash2 size={13} />} loading={busy === `discard-${draft.draftId}`} onClick={() => void mutate(`discard-${draft.draftId}`, oratorioClient.discardReviewDraft(draft.draftId), 'Review draft discarded')}>Discard</Button><Button variant="secondary" size="sm" loading={busy === `save-${draft.draftId}`} onClick={() => void mutate(`save-${draft.draftId}`, oratorioClient.updateReviewDraft(draft.draftId, { summaryBody: summary }), 'Review draft saved')}>Save</Button><Button variant="primary" size="sm" loading={busy === `publish-${draft.draftId}`} onClick={() => void mutate(`publish-${draft.draftId}`, oratorioClient.publishReviewDraft(draft.draftId), 'Review published')}>Publish review</Button></div> : null}
  </article>
}

function FindingCard({ draft, finding, busy, mutate }: { draft: ReviewDraftDto; finding: ReviewDraftCommentDto; busy: string | null; mutate: Mutate }) {
  const key = `finding-${finding.draftCommentId}`
  return <article className="ora-finding" data-status={finding.resolutionState}><header><span><strong>{finding.title}</strong><small>{finding.path}:{finding.line}</small></span><span className="ora-status">{finding.resolutionKind ?? finding.resolutionState}</span></header><p>{finding.body}</p>{finding.suggestionOriginal != null && finding.suggestionReplacement != null ? <OratorioInlineDiff filePath={finding.path} line={finding.line} before={finding.suggestionOriginal} after={finding.suggestionReplacement} /> : null}<footer>{finding.resolutionState === 'open' ? <><Button variant="ghost" size="sm" disabled={busy !== null} onClick={() => void mutate(key, oratorioClient.resolveFinding(draft.draftId, finding.draftCommentId, 'dismissed'), 'Finding dismissed')}>Dismiss</Button><Button variant="secondary" size="sm" disabled={busy !== null} onClick={() => void mutate(key, oratorioClient.resolveFinding(draft.draftId, finding.draftCommentId, 'fixed'), 'Finding resolved')}>Mark fixed</Button></> : <Button variant="ghost" size="sm" iconLeft={<RotateCcw size={13} />} disabled={busy !== null} onClick={() => void mutate(key, oratorioClient.reopenFinding(draft.draftId, finding.draftCommentId), 'Finding reopened')}>Reopen</Button>}</footer></article>
}

function FollowUpCard({ draft, value, onChange, busy, mutate }: { draft: FollowUpDraftDto; value: string; onChange: (value: string) => void; busy: string | null; mutate: Mutate }) {
  return <article className="ora-draft-card ora-draft-card--editor"><header><span className="ora-status">{draft.status}</span><strong>{draft.title}</strong></header><Textarea value={value} readOnly={draft.status !== 'draft'} onChange={(event) => onChange(event.target.value)} aria-label={`Follow-up ${draft.title}`} />{draft.status === 'draft' ? <div className="ora-section-actions"><Button variant="danger" size="sm" onClick={() => void mutate(`follow-discard-${draft.draftId}`, oratorioClient.discardFollowUp(draft.draftId), 'Follow-up discarded')}>Discard</Button><Button variant="secondary" size="sm" onClick={() => void mutate(`follow-save-${draft.draftId}`, oratorioClient.updateFollowUp(draft.draftId, { body: value }), 'Follow-up saved')}>Save</Button><Button variant="primary" size="sm" loading={busy === `follow-create-${draft.draftId}`} onClick={() => void mutate(`follow-create-${draft.draftId}`, oratorioClient.createTaskFromFollowUp(draft.draftId), 'Local task created')}>Create local task</Button></div> : null}</article>
}

function DecisionStage({ task, detail, busy, mutate }: { task: OratorioTask; detail: ItemDetailResponse; busy: string | null; mutate: Mutate }) {
  const [feedback, setFeedback] = useState('')
  return <div className="ora-detail__stack"><DetailSection title="Decision" description="Choose the outcome for this review round.">{task.state === 'awaiting-review' ? <><Textarea value={feedback} onChange={(event) => setFeedback(event.target.value)} placeholder="Optional feedback" aria-label="Decision feedback" /><div className="ora-decision-actions"><Button variant="danger" disabled={busy !== null} onClick={() => void mutate('reject', oratorioClient.itemAction(task.id, 'reject', { body: feedback }), 'Task rejected')}>Reject</Button><Button variant="secondary" disabled={busy !== null} onClick={() => void mutate('request-changes', oratorioClient.itemAction(task.id, 'request-changes', { body: feedback }), 'Changes requested')}>Request changes</Button><Button variant="primary" iconLeft={<Check size={14} />} disabled={busy !== null} onClick={() => void mutate('approve', oratorioClient.itemAction(task.id, 'approve', { body: feedback }), 'Task approved')}>Approve</Button></div></> : <Empty>This task is not awaiting a decision.</Empty>}</DetailSection>
    <DetailSection title="Source writes" description="Every external mutation remains auditable and retryable.">{detail.sourceWrites?.length ? detail.sourceWrites.map((write) => <article className="ora-write-row" key={write.writeId}><span className="ora-state-dot" data-tone={write.status === 'failed' ? 'error' : write.status === 'succeeded' ? 'success' : 'info'} /><span><strong>{write.intent}</strong><small>{write.status} · attempt {write.attemptCount}{write.errorMessage ? ` · ${write.errorMessage}` : ''}</small></span>{write.status === 'failed' ? <Button variant="secondary" size="sm" loading={busy === `write-${write.writeId}`} onClick={() => void mutate(`write-${write.writeId}`, oratorioClient.retrySourceWrite(write.writeId), 'Source write retried')}>Retry</Button> : write.externalUrl ? <a href={write.externalUrl}>Open artifact</a> : null}</article>) : <Empty>No source writes.</Empty>}</DetailSection>
    <DetailSection title="Decision history">{detail.decisions?.length ? detail.decisions.map((decision) => <article className="ora-timeline-card" key={decision.decisionId}><strong>{decision.decision}</strong><p>{decision.body || 'No note'}</p><small>{decision.authorName} · {new Date(decision.createdAt).toLocaleString()}</small></article>) : <Empty>No decision recorded.</Empty>}</DetailSection></div>
}

function ClosedStage({ task, detail, busy, mutate }: { task: OratorioTask; detail: ItemDetailResponse; busy: string | null; mutate: Mutate }) {
  return <div className="ora-detail__stack"><DetailSection title="Outcome"><dl className="ora-facts"><Fact name="Current state" value={task.state} /><Fact name="Check" value={task.check ?? 'Not reported'} /><Fact name="Latest run" value={task.run ? `Attempt ${task.run.attempt} · ${task.run.status}` : 'No run'} /><Fact name="Round" value={String(task.round ?? 1)} /></dl><div className="ora-section-actions">{task.state === 'archived' ? <Button variant="secondary" iconLeft={<RotateCcw size={14} />} loading={busy === 'reopen'} onClick={() => void mutate('reopen', oratorioClient.itemAction(task.id, 'reopen'), 'Task reopened')}>Reopen</Button> : <Button variant="ghost" iconLeft={<Archive size={14} />} loading={busy === 'archive'} onClick={() => void mutate('archive', oratorioClient.itemAction(task.id, 'archive'), 'Task archived')}>Archive</Button>}</div></DetailSection><DetailSection title="Final result"><p className="ora-detail__prose">{detail.runs.at(-1)?.summary || task.description || 'No final summary.'}</p></DetailSection></div>
}

function CommentList({ detail, sourceOnly = false }: { detail: ItemDetailResponse; sourceOnly?: boolean }) {
  const comments = (detail.comments ?? []).filter((comment) => !sourceOnly || Boolean(comment.source)).map(mapComment)
  return comments.length ? <div className="ora-discussion">{comments.map((comment) => <article key={comment.id} data-role={comment.role}><span className="ora-avatar">{comment.author.slice(0, 1)}</span><div><header><strong>{comment.author}</strong><small>{comment.time}</small></header><p>{comment.body}</p></div></article>)}</div> : <Empty>No comments.</Empty>
}

function DetailSection({ title, description, action, className, children }: { title: string; description?: string; action?: ReactNode; className?: string; children: ReactNode }) { return <section className={`ora-detail-section${className ? ` ${className}` : ''}`}><header><span><h2>{title}</h2>{description ? <p>{description}</p> : null}</span>{action}</header><div>{children}</div></section> }
function Fact({ name, value }: { name: string; value: string }) { return <div><dt>{name}</dt><dd>{value}</dd></div> }
function Empty({ children }: { children: ReactNode }) { return <p className="ora-detail__pending-copy">{children}</p> }
function ProviderIcon({ task }: { task: OratorioTask }) { return task.provider === 'github' ? <GithubGlyph /> : task.provider === 'gitlab' ? <GitlabGlyph /> : <FileText size={14} /> }
function KindIcon({ task }: { task: OratorioTask }) { return task.kind === 'Pull request' ? <GitPullRequest size={13} /> : task.kind === 'Issue' ? <CircleDot size={13} /> : <GitBranch size={13} /> }
