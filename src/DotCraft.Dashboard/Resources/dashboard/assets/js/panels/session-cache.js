import { formatPercent, formatRelative, plural, shortHash } from '../core/format.js';
import { esc, facts, mono, statusIndicator } from '../core/html.js';

const FIELD_CHANGE = {
  both: 'System prompt and tool schema changed',
  tools: 'Tool schema changed',
  prompt: 'System prompt changed'
};

const FORK_PURPOSES = {
  promptSuggestion: 'Prompt suggestion fork',
  memoryExtraction: 'Memory extraction fork',
  sideChat: 'Side chat fork'
};

const FINISH_LABELS = {
  length: 'Stopped at the output limit',
  content_filter: 'Stopped by the content filter',
  contentfilter: 'Stopped by the content filter'
};

export function abnormalFinish(session) {
  const reason = session.lastFinishReason;
  if (!reason || ['stop', 'tool_calls', 'toolcalls'].includes(reason.toLowerCase())) return '';
  return FINISH_LABELS[reason.toLowerCase()] ?? reason;
}

export function inheritsParentContext(session) {
  return session.forkMode === 'all' || Object.hasOwn(FORK_PURPOSES, session.forkMode ?? '');
}

function prefixSignal(session) {
  if (!inheritsParentContext(session)) return '';
  if (session.parentPrefix?.status === 'staticShared') return 'Inherited history changed';
  if (session.parentPrefix?.status === 'diverged') return 'Cache prefix differs';
  return '';
}

// One short label per row, trouble first, so a list shows where to look.
function cacheSignal(session) {
  const prefix = prefixSignal(session);
  if (prefix) return prefix;
  if (session.lastPromptCacheChangeKind === 'drift' || session.promptDriftCount > 0) return 'Cache changed';
  if (session.rollbackCount > 0) return 'Rolled back';
  return '';
}

export function troubleMarker(session) {
  const failed = session.errorCount > 0;
  const label = failed ? plural(session.errorCount, 'error') : abnormalFinish(session) || cacheSignal(session);
  return label
    ? `<span class="dash-trouble" data-tooltip="${esc(label)}" aria-label="${esc(label)}" role="img">${statusIndicator(failed ? 'error' : 'warning')}</span>`
    : '';
}

function withDot(tone, text) {
  return `<span class="dash-status">${statusIndicator(tone)}<span>${esc(text)}</span></span>`;
}

function changeText(session) {
  const fields = session.lastPromptCacheChangedFields ?? [];
  if (fields.includes('prompt') && fields.includes('tools')) return FIELD_CHANGE.both;
  return fields.includes('tools') ? FIELD_CHANGE.tools : FIELD_CHANGE.prompt;
}

export function cacheVerdict(session) {
  const when = session.lastPromptCacheChangeAt ? formatRelative(session.lastPromptCacheChangeAt) : '';
  const drifts = session.promptDriftCount > 0 ? plural(session.promptDriftCount, 'change') : '';
  let verdict;
  if (session.lastPromptCacheChangeKind === 'drift') verdict = withDot('warning', changeText(session));
  else if (session.lastPromptCacheChangeKind === 'toolExtension') verdict = withDot('neutral', 'Tools extended');
  else if (session.promptDriftCount > 0) verdict = withDot('warning', 'Changed');
  else verdict = esc('Stable');
  const extra = facts([drifts, when].filter(Boolean).map(esc));
  const hashes = facts([`prompt ${mono(shortHash(session.systemPromptHash))}`, `tools ${mono(shortHash(session.toolSchemaHash))}`]);
  return `<span class="dash-inline-list">${verdict}${drifts || when ? `<span class="dash-dim dash-num">${extra}</span>` : ''}</span>
    <span class="dash-kv__quiet">${hashes}</span>`;
}

export function rollbackText(session) {
  return session.rollbackCount > 0 ? withDot('warning', plural(session.rollbackCount, 'rollback')) : 'None';
}

export function forkModeText(session) {
  const mode = session.forkMode;
  if (mode === 'all') return 'Inherits the full conversation';
  if (mode === 'none') return 'Fresh context';
  if (/^\d+$/.test(mode ?? '')) return esc(`Inherits the last ${plural(Number(mode), 'turn')}`);
  return FORK_PURPOSES[mode] ?? '<span class="dash-dim">Fork mode not recorded</span>';
}

export function firstRequestText(session) {
  const input = session.firstRequest?.inputTokens;
  return input ? esc(`First request ${formatPercent((session.firstRequest.cachedInputTokens ?? 0) / input, 0)} cached`) : '';
}

export function prefixText(prefix) {
  if (prefix.status === 'compatible') return 'Reuses the parent cache prefix';
  if (prefix.status === 'staticShared') return withDot('warning', 'Inherited history was not copied intact');
  if (prefix.status === 'diverged') return withDot('warning', 'Prompt or tools differ from the parent');
  return '<span class="dash-dim">Parent prefix unavailable</span>';
}
