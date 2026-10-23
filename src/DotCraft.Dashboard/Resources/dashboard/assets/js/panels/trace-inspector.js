import { formatClock, formatCount, formatPercent, prettyJson } from '../core/format.js';
import { chevron, esc, icon, iconButton, kv } from '../core/html.js';
import { describeEvent } from './trace-describe.js';
import { entryLabel } from './trace-ledger.js';

const PREVIEW_LIMIT = 4000;
export const inspectorState = { full: new Set(), metadata: new Set() };

function block(title, body) {
  return `<section class="dash-inspector__section"><h3 class="dash-inspector__heading">${esc(title)}</h3>${body}</section>`;
}

function content({ text, mono, tone }, id) {
  const long = text.length > PREVIEW_LIMIT;
  const full = inspectorState.full.has(id);
  const shown = long && !full ? `${text.slice(0, PREVIEW_LIMIT)}…` : text;
  return `${mono ? `<pre class="dash-code${tone === 'error' ? ' dash-code--error' : ''}">${esc(shown)}</pre>` : `<p class="dash-prose">${esc(shown)}</p>`}
    ${long ? `<button type="button" class="dash-text-action" data-action="toggle-inspector-full" data-id="${esc(id)}">${full ? 'Show less' : 'Show all'}</button>` : ''}`;
}

function metadata(event, id) {
  if (!event.metadataJson) return '';
  const open = inspectorState.metadata.has(id);
  return `<section class="dash-inspector__section">
    <button type="button" class="dash-text-action dash-text-action--disclosure" aria-expanded="${open}" data-action="toggle-inspector-metadata" data-id="${esc(id)}">Raw metadata${chevron(open)}</button>
    ${open ? `<pre class="dash-code">${esc(prettyJson(event.metadataJson))}</pre>` : ''}
  </section>`;
}

function toolBody(entry) {
  const started = describeEvent(entry.started);
  const finished = entry.completed ? describeEvent(entry.completed) : null;
  const facts = [...(finished?.facts ?? started.facts)];
  return `${facts.length ? block('Details', kv(facts)) : ''}
    ${started.content?.text ? block('Arguments', content(started.content, `${entry.key}:args`)) : ''}
    ${finished?.content?.text ? block('Result', content(finished.content, `${entry.key}:result`)) : entry.completed ? '' : block('Result', '<p class="dash-dim">Still running.</p>')}
    ${metadata(entry.started, `${entry.key}:md`)}`;
}

function sessionSoFar(totals) {
  const num = value => `<span class="dash-num">${formatCount(value)}</span>`;
  return block('Session so far', kv([
    ['Requests', num(totals.requests)],
    ['Total', num(totals.total)],
    ['Cache hit', `<span class="dash-num">${formatPercent(totals.input ? totals.cached / totals.input : 0)}</span>`],
    ['Cache write', num(totals.written)],
    ['Output', num(totals.output)]
  ]));
}

function eventBody(entry) {
  const description = describeEvent(entry.event);
  return `${description.facts.length ? block('Details', kv(description.facts)) : ''}
    ${entry.sessionTotals ? sessionSoFar(entry.sessionTotals) : ''}
    ${description.content?.text ? block('Content', content(description.content, `${entry.key}:content`)) : ''}
    ${metadata(entry.event, `${entry.key}:md`)}`;
}

export function inspector(entry) {
  if (!entry) return '';
  const description = describeEvent(entry.kind === 'tool' ? entry.started : entry.event);
  const label = entryLabel(entry);
  return `<aside class="dash-inspector" aria-label="${esc(label)}">
    <header class="dash-inspector__header">
      <span class="dash-inspector__icon" aria-hidden="true">${description.icon === 'thinking' ? '<span class="dash-thought-dot"></span>' : icon(description.icon, 16, 1.75)}</span>
      <h2 class="dash-inspector__title">${esc(label)}</h2>
      <time class="dash-inspector__time dash-num">${formatClock(entry.time)}</time>
      ${iconButton('x', 'Close details', { attributes: { 'data-action': 'close-trace-inspector' } })}
    </header>
    <div class="dash-inspector__body">${entry.kind === 'tool' ? toolBody(entry) : eventBody(entry)}</div>
  </aside>`;
}
