import { formatClock, formatDuration } from '../core/format.js';
import { esc, icon, spinner, statusIndicator } from '../core/html.js';
import { describeEvent } from './trace-describe.js';

export function eventKey(event) {
  return String(event.id ?? `${event.sessionKey}:${event.timestamp}:${event.type}`);
}

export function buildEntries(events, { turns }) {
  const entries = [];
  const openCalls = new Map();
  const totals = { input: 0, cached: 0, written: 0, output: 0, total: 0 };
  let turn = 0;
  let request = 0;
  for (const event of events) {
    if (turns && event.type === 'Request') {
      turn += 1;
      entries.push({ kind: 'turn', key: `turn:${eventKey(event)}`, turn, time: event.timestamp });
    }
    if (event.type === 'ToolCallStarted' && event.callId) {
      const entry = { kind: 'tool', key: eventKey(event), started: event, completed: null, time: event.timestamp };
      openCalls.set(event.callId, entry);
      entries.push(entry);
      continue;
    }
    const pending = event.type === 'ToolCallCompleted' && openCalls.get(event.callId);
    if (pending) {
      pending.completed = event;
      openCalls.delete(event.callId);
      continue;
    }
    const entry = { kind: 'event', key: eventKey(event), event, time: event.timestamp };
    if (event.type === 'TokenUsage') {
      request = Number(event.llmCallIndex) || request + 1;
      totals.input += Number(event.inputTokens) || 0;
      totals.cached += Number(event.cachedInputTokens) || 0;
      totals.written += Number(event.cacheWriteInputTokens) || 0;
      totals.output += Number(event.outputTokens) || 0;
      totals.total += Number(event.totalTokens) || 0;
      entry.request = request;
      entry.sessionTotals = { ...totals, requests: request };
    }
    entries.push(entry);
  }
  return entries;
}

export function entryLabel(entry) {
  if (entry.kind === 'tool') return entry.started.toolName ?? 'Tool';
  return entry.request ? `Request ${entry.request}` : describeEvent(entry.event).label;
}

export function entrySpan(entry) {
  const start = Date.parse(entry.time);
  const end = entry.kind === 'tool' && entry.completed ? Date.parse(entry.completed.timestamp) : start;
  return [start, end];
}

export function entryEvents(entry) {
  if (entry.kind === 'tool') return [entry.started, entry.completed].filter(Boolean);
  return entry.kind === 'event' ? [entry.event] : [];
}

export function entrySearchText(entry) {
  return entryEvents(entry)
    .map(event => [event.type, event.toolName, event.content, event.toolArguments, event.toolResult].filter(Boolean).join(' '))
    .join(' ')
    .toLowerCase();
}

function glyph(description) {
  return description.icon === 'thinking' ? '<span class="dash-thought-dot"></span>' : icon(description.icon, 16, 1.75);
}

function toolRow(entry) {
  const started = describeEvent(entry.started);
  const finished = entry.completed ? describeEvent(entry.completed) : null;
  const args = started.detail[1] ?? '';
  const result = finished?.detail[1] ?? '';
  return {
    glyph: glyph(started),
    label: entry.started.toolName ?? 'Tool',
    detail: `${args ? `<span class="dash-ledger__args">${esc(args)}</span>` : ''}${entry.completed
      ? `${icon('arrowRight', 12)}<span class="dash-ledger__result">${esc(result || 'No output')}</span>`
      : `<span class="dash-ledger__running">${spinner(12)}Running</span>`}`,
    status: finished?.status ? statusIndicator(finished.status, finished.statusLabel) : '',
    meta: entry.completed?.durationMs != null ? formatDuration(entry.completed.durationMs) : ''
  };
}

function eventRow(entry) {
  const description = describeEvent(entry.event);
  return {
    glyph: glyph(description),
    label: entryLabel(entry),
    detail: description.detail.filter(Boolean).map(part => `<span>${esc(part)}</span>`).join(''),
    status: description.status ? statusIndicator(description.status, description.statusLabel) : '',
    meta: ''
  };
}

export function ledgerRow(entry, { selected, threadLabel }) {
  if (entry.kind === 'turn') {
    return `<li class="dash-ledger-turn" data-key="${esc(entry.key)}"><span class="dash-ledger-turn__badge dash-num">Turn ${entry.turn}</span></li>`;
  }
  const row = entry.kind === 'tool' ? toolRow(entry) : eventRow(entry);
  return `<li class="dash-ledger-row" data-key="${esc(entry.key)}" data-kind="${entry.kind === 'tool' ? 'tool' : esc(entry.event.type)}"${selected ? ' aria-current="true"' : ''}>
    <button type="button" class="dash-ledger-row__line" data-action="select-trace-entry" data-key="${esc(entry.key)}">
      <span class="dash-ledger-row__icon" aria-hidden="true">${row.glyph}</span>
      <span class="dash-ledger-row__label">${esc(row.label)}</span>
      <span class="dash-ledger-row__detail dash-num">${row.detail}</span>
      ${threadLabel ? `<span class="dash-ledger-row__thread" data-tooltip="${esc(threadLabel)}">${esc(threadLabel)}</span>` : ''}
      <span class="dash-ledger-row__status">${row.status}</span>
      <span class="dash-ledger-row__meta dash-num">${esc(row.meta)}</span>
      <time class="dash-ledger-row__time dash-num" datetime="${esc(entry.time)}">${formatClock(entry.time)}</time>
    </button>
  </li>`;
}
