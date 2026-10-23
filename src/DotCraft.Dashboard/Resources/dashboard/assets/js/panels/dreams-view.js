import { formatCount, formatDateTime, formatDuration, plural, truncateMiddle } from '../core/format.js';
import { copyButton } from '../core/clipboard.js';
import { button, chevron, esc, facts, icon, iconButton, kv, mono, errorNotice, skeleton } from '../core/html.js';

const CHANGE_LABEL = { added: 'New', updated: 'Edited', deleted: 'Removed' };

export const DREAMS_FILTERS = {
  pending: run => run.reviewStatus === 'pending',
  applied: run => run.reviewStatus === 'applied',
  archived: run => run.reviewStatus === 'archived',
  other: run => !['pending', 'applied', 'archived'].includes(run.reviewStatus ?? '')
};

export const detailOpen = new Map();

export function dreamStatus(run) {
  if (run.status === 'running') return { tone: 'pending', label: 'Running' };
  if (run.status === 'failed') return { tone: 'error', label: 'Failed' };
  if (run.status === 'skipped') return { tone: 'neutral', label: 'Skipped' };
  if (run.status === 'canceled') return { tone: 'neutral', label: 'Canceled' };
  switch (run.reviewStatus) {
    case 'pending': return { tone: 'warning', label: 'Needs review' };
    case 'applied': return { tone: 'success', label: run.autoApplied ? 'Applied automatically' : 'Applied' };
    case 'discarded': return { tone: 'neutral', label: 'Discarded' };
    case 'archived': return { tone: 'neutral', label: 'Archived' };
    default: return { tone: 'neutral', label: 'Finished' };
  }
}

export function dreamTitle(run) {
  return `${run.trigger === 'manual' ? 'Manual' : 'Scheduled'} run ${formatDateTime(run.startedAt)}`;
}

export function dreamChangeSummary(run) {
  if (run.status !== 'succeeded' || !run.changes) return [];
  const parts = [
    run.changes.added?.length ? `${run.changes.added.length} added` : '',
    run.changes.updated?.length ? `${run.changes.updated.length} edited` : '',
    run.changes.deleted?.length ? `${run.changes.deleted.length} removed` : ''
  ].filter(Boolean);
  return parts.length ? parts : [run.changes.indexChanged ? 'Index updated' : 'No changes'];
}

// Matching on line content (not LCS) keeps the counts and the rendered lines in agreement.
function lineDiff(oldText = '', newText = '') {
  const split = text => (String(text || '').trim() ? String(text).split(/\r?\n/) : []);
  const oldLines = split(oldText);
  const newLines = split(newText);
  const counts = lines => lines.reduce((map, line) => map.set(line, (map.get(line) ?? 0) + 1), new Map());
  const rows = [];
  let additions = 0;
  let removals = 0;
  const newLeft = counts(newLines);
  for (const line of oldLines) {
    if ((newLeft.get(line) ?? 0) > 0) newLeft.set(line, newLeft.get(line) - 1);
    else { rows.push({ type: 'del', text: line }); removals++; }
  }
  const oldLeft = counts(oldLines);
  for (const line of newLines) {
    if ((oldLeft.get(line) ?? 0) > 0) { oldLeft.set(line, oldLeft.get(line) - 1); rows.push({ type: 'ctx', text: line }); }
    else { rows.push({ type: 'add', text: line }); additions++; }
  }
  const reordered = !additions && !removals && oldLines.some((line, index) => line !== newLines[index]);
  return { rows, additions, removals, reordered };
}

function fileDiff(file, defaultOpen) {
  const key = `file:${file.name}`;
  const open = detailOpen.get(key) ?? defaultOpen;
  const diff = lineDiff(file.baseline, file.candidate);
  const marker = type => (type === 'add' ? '+' : type === 'del' ? '−' : ' ');
  const lines = diff.rows.length
    ? diff.rows.map(row => `<span class="dash-diff__line dash-diff__line--${row.type}"><span class="dash-diff__marker" aria-hidden="true">${marker(row.type)}</span>${esc(row.text || ' ')}</span>`).join('')
    : '<span class="dash-diff__line dash-diff__line--ctx"><span class="dash-diff__marker" aria-hidden="true"> </span>No line changes.</span>';
  return `<div class="dash-diff-file"${open ? ' data-open' : ''}>
    <button type="button" class="dash-diff-file__head" aria-expanded="${open}" data-action="toggle-dream-section" data-key="${esc(key)}" data-default="${defaultOpen}">
      <code class="dash-mono dash-truncate">${esc(file.name)}</code>${chevron(open)}
      <span class="dash-diff-file__kind">${esc(CHANGE_LABEL[file.change] ?? 'Edited')}</span>
      <span class="dash-diff-file__counts dash-num"><span class="dash-diff-add">+${diff.additions}</span><span class="dash-diff-del">−${diff.removals}</span></span>
    </button>
    ${open ? `<pre class="dash-diff" aria-label="Changes to ${esc(file.name)}">${diff.reordered ? '<span class="dash-diff__line dash-diff__line--ctx"><span class="dash-diff__marker" aria-hidden="true"> </span>Lines reordered.</span>' : ''}${lines}</pre>` : ''}
  </div>`;
}

function section(key, title, meta, defaultOpen, body) {
  const open = detailOpen.get(key) ?? defaultOpen;
  return `<section class="dash-detail-section">
    <button type="button" class="dash-detail-section__head" aria-expanded="${open}" data-action="toggle-dream-section" data-key="${key}" data-default="${defaultOpen}">
      <h3>${esc(title)}</h3>${meta ? `<span class="dash-section-heading__meta dash-num">${esc(meta)}</span>` : ''}${chevron(open)}
    </button>
    ${open ? `<div class="dash-detail-section__body">${body}</div>` : ''}
  </section>`;
}

export function detailSkeleton() {
  return `<div class="dash-dream-detail" aria-busy="true">${skeleton('60%', 18)}${skeleton('82%', 12)}${skeleton('100%', 120)}</div>`;
}

function cardRow(iconName, html) {
  return `<div class="dash-hovercard__row">${icon(iconName, 14)}<span>${html}</span></div>`;
}

export function dreamRunCard(run) {
  const status = dreamStatus(run);
  const duration = run.endedAt ? Date.parse(run.endedAt) - Date.parse(run.startedAt) : null;
  const usage = run.usage;
  const rows = [
    cardRow('brain', run.scope ? `Agent memory ${mono(run.scope)}` : 'Workspace memory'),
    cardRow('clock', `<span class="dash-num">${esc(formatDateTime(run.startedAt))}</span>${duration != null ? `<span class="dash-num dash-dim">${esc(formatDuration(duration))}</span>` : ''}`),
    cardRow('search', `<span class="dash-num">${esc(plural(run.candidateThreadCount ?? 0, 'thread'))}</span><span class="dash-num">${esc(plural(run.evidenceReadCount ?? 0, 'read'))}</span><span class="dash-num">${esc(plural(run.evidenceSearchCount ?? 0, 'search', 'searches'))}</span>`)
  ];
  if (usage) rows.push(cardRow('gauge', `<span class="dash-num">${esc(formatCount(usage.totalTokens))} tokens</span><span class="dash-num dash-dim">${esc(formatCount(usage.outputTokens))} out</span><span class="dash-num dash-dim">${esc(formatCount(usage.cachedInputTokens))} cached</span>`));
  return `<div class="dash-hovercard__header">
      <span class="dash-hovercard__title">${esc(dreamTitle(run))}</span>
      <span class="dash-hovercard__meta">${esc(status.label)}</span>
    </div>
    ${rows.join('')}
    <div class="dash-hovercard__footer">
      <span class="dash-inline-list">${mono(truncateMiddle(run.id, 30))}${copyButton(run.id, 'Copy run ID')}</span>
    </div>`;
}

export function dreamRunDetail(run, data) {
  const files = Array.isArray(data?.files) ? data.files : [];
  const running = run.status === 'running';
  const pending = run.reviewStatus === 'pending' && !running;
  const failed = run.errorType || (run.message && run.status === 'failed');
  return `<article class="dash-dream-detail" aria-label="${esc(dreamTitle(run))}">
    <header class="dash-dream-detail__head">
      <div class="dash-dream-detail__title">
        <h2 class="dash-truncate dash-dream-detail__name" data-hovercard="dream-run" data-run="${esc(run.id)}">${esc(dreamTitle(run))}</h2>
      </div>
      <div class="dash-dream-detail__actions">
        ${run.threadId ? button('Open trace', { variant: 'ghost', size: 'sm', iconName: 'externalLink', attributes: { 'data-action': 'open-thread', 'data-thread': run.threadId } }) : ''}
        ${running ? button('Cancel run', { size: 'sm', attributes: { 'data-action': 'dream-action', 'data-op': 'cancel', 'data-run': run.id } }) : ''}
        ${pending ? button('Discard', { variant: 'outline', size: 'sm', attributes: { 'data-action': 'dream-action', 'data-op': 'discard', 'data-run': run.id } }) : ''}
        ${pending ? button('Apply to memory', { variant: 'primary', size: 'sm', attributes: { 'data-action': 'dream-action', 'data-op': 'apply', 'data-run': run.id } }) : ''}
        ${iconButton('ellipsis', 'More actions', { iconSize: 16, attributes: { 'aria-haspopup': 'menu', 'aria-expanded': 'false', 'data-action': 'open-dream-menu', 'data-run': run.id } })}
      </div>
    </header>
    ${failed ? errorNotice('This run failed', [run.errorType, run.message].filter(Boolean).join(': ')) : run.message ? `<p class="dash-secondary">${esc(run.message)}</p>` : ''}
    ${section('summary', 'Summary', '', true, `<p class="dash-prose">${esc(run.summary ?? (running ? 'The run is still working.' : 'No summary recorded.'))}</p>`)}
    ${section('changes', 'Changes', `${files.length} ${files.length === 1 ? 'file' : 'files'}`, true, files.length
      ? files.map(file => fileDiff(file, files.length <= 4)).join('')
      : `<p class="dash-dim">${running ? 'Changes appear when the run finishes.' : 'No memory files changed.'}</p>`)}
    ${section('manifest', 'Input manifest', '', false, `${run.inputManifestPath ? `<code class="dash-mono dash-dim">${esc(run.inputManifestPath)}</code>` : ''}<pre class="dash-code">${esc(data?.manifest || 'No manifest recorded.')}</pre>`)}
    ${section('evidence', 'Evidence', plural(run.evidenceThreadIds?.length ?? 0, 'thread'), false, `${run.evidenceThreadIds?.length
      ? `<ul class="dash-plain-list">${run.evidenceThreadIds.map(id => `<li><button type="button" class="dash-link dash-mono" data-action="open-thread" data-thread="${esc(id)}">${esc(id)}</button></li>`).join('')}</ul>`
      : '<p class="dash-dim">No evidence threads recorded.</p>'}${kv([['Thread', mono(run.threadId ?? 'None')], ['Turn', mono(run.turnId ?? 'None')]])}`)}
  </article>`;
}
