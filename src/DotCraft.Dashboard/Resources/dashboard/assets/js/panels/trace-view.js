import { onAction } from '../core/actions.js';
import { fetchJson } from '../core/api.js';
import { formatCount, plural, sessionTitle } from '../core/format.js';
import { button, emptyState, esc, icon, iconButton, loadingPanel, panelHeader, pillSwitch, segmented, spinner } from '../core/html.js';
import { openMenu } from '../core/menu.js';
import { panelElement, switchPanel } from '../core/navigation.js';
import { state } from '../core/state.js';
import { findSession, loadSessions } from './session-list.js';
import { isVisibleEvent, matchesFilter } from './trace-describe.js';
import { inspector, inspectorState } from './trace-inspector.js';
import { buildEntries, entrySearchText, entrySpan, eventKey, ledgerRow } from './trace-ledger.js';
import { rollbackEvents } from './trace-operations.js';
import { traceStrip } from './trace-strip.js';

export const ALL_THREADS = '__all__';
const PAGE_LIMIT = 1000;
const PRIMARY_FILTERS = [
  { value: 'all', label: 'All' },
  { value: 'Request', label: 'Messages' },
  { value: 'Response', label: 'Responses' },
  { value: 'Tool', label: 'Tools' },
  { value: 'TokenUsage', label: 'Tokens' },
  { value: 'Error', label: 'Errors' }
];
const MORE_FILTERS = [
  { value: 'Thinking', label: 'Thinking' },
  { value: 'AgentInstructions', label: 'Instructions' },
  { value: 'WorldStateDiagnostic', label: 'World state' },
  { value: 'Cache', label: 'Prompt cache' },
  { value: 'Maintenance', label: 'Maintenance' },
  { value: 'Provider', label: 'Provider' }
];

const view = { thread: null, raw: [], operations: [], events: [], entries: [], oldestCursor: null, hasMore: false, loadingOlder: false, filter: 'all', query: '', selected: null, range: null, follow: true, nearBottom: true, unseen: 0 };

const panel = () => panelElement('sessions');
const scroller = () => panel().querySelector('.dash-trace-scroll');
const allThreads = () => view.thread === ALL_THREADS;

export const activeThread = () => view.thread;

function threadLabel(entry) {
  if (!allThreads() || entry.kind === 'turn') return '';
  const key = (entry.kind === 'tool' ? entry.started : entry.event).sessionKey;
  const session = findSession(key);
  return session ? sessionTitle(session) : key;
}

function compose() {
  view.events = rollbackEvents(view.raw, view.operations, view.thread)
    .filter(event => isVisibleEvent(event) && matchesFilter(event, view.filter));
}

function rebuild() {
  compose();
  view.entries = buildEntries(view.events, { turns: !allThreads() && view.filter === 'all' });
}

function selectedEntry() {
  return view.entries.find(entry => entry.key === view.selected) ?? null;
}

function countText() {
  const shown = view.entries.filter(entry => entry.kind !== 'turn').length;
  const total = `${formatCount(shown)}${view.hasMore ? '+' : ''}`;
  return `${total} ${shown === 1 && !view.hasMore ? 'event' : 'events'}`;
}

function olderRow() {
  if (!view.hasMore) return '';
  return `<li class="dash-trace-older">${view.loadingOlder
    ? `${spinner(14)}<span>Loading older events</span>`
    : '<button type="button" class="dash-text-action" data-action="load-older-traces">Load older events</button>'}</li>`;
}

function jumpButton() {
  if (view.nearBottom && view.unseen === 0) return '';
  return `<button type="button" class="dash-floating-control" data-action="jump-to-latest">${icon('arrowDown', 14)}${esc(view.unseen > 0 ? plural(view.unseen, 'new event') : 'Jump to latest')}</button>`;
}

function listHtml() {
  return olderRow() + view.entries.map(entry => ledgerRow(entry, { selected: entry.key === view.selected, threadLabel: threadLabel(entry) })).join('');
}

function body() {
  if (!view.events.length) {
    const unfiltered = view.filter === 'all';
    return `<div class="dash-panel-body">${emptyState(
      'activity',
      unfiltered ? 'No events yet' : 'Nothing matches this filter',
      '',
      unfiltered ? '' : button('Show all events', { size: 'sm', attributes: { 'data-action': 'set-trace-filter', 'data-value': 'all' } })
    )}</div>`;
  }
  return `<div class="dash-trace-layout${view.selected ? ' dash-trace-layout--inspecting' : ''}">
    <div class="dash-trace-viewport">
      <div class="dash-trace-strip-slot">${traceStrip(view.entries, view.selected, view.range)}</div>
      <div class="dash-trace-scroll"><ol class="dash-ledger" aria-label="Trace events">${listHtml()}</ol></div>
      <div class="dash-trace-jump">${jumpButton()}</div>
    </div>
    <div class="dash-trace-inspector-slot">${inspector(selectedEntry())}</div>
  </div>`;
}

function outsideRange(entry) {
  if (!view.range || entry.kind === 'turn') return false;
  const [start, end] = entrySpan(entry);
  return end < view.range[0] || start > view.range[1];
}

function applyRange() {
  const outside = new Set(view.entries.filter(outsideRange).map(entry => entry.key));
  panel().querySelectorAll('.dash-ledger-row').forEach(row => row.toggleAttribute('data-out', outside.has(row.dataset.key)));
}

function renderStrip() {
  const slot = panel().querySelector('.dash-trace-strip-slot');
  if (slot) slot.innerHTML = traceStrip(view.entries, view.selected, view.range);
}

function setRange(range) {
  view.range = range;
  renderStrip();
  applyRange();
  applySearch();
  const first = range && view.entries.find(entry => entry.kind !== 'turn' && !outsideRange(entry));
  if (first) panel().querySelector(`.dash-ledger-row[data-key="${CSS.escape(first.key)}"]`)?.scrollIntoView({ block: 'nearest' });
}

function bindStrip(slot) {
  let drag = null;
  let suppressClick = false;
  const timeAt = (strip, clientX) => {
    const track = strip.querySelector('.dash-strip__track').getBoundingClientRect();
    const ratio = Math.min(Math.max((clientX - track.left) / track.width, 0), 1);
    const first = Number(strip.dataset.first);
    return first + ratio * (Number(strip.dataset.last) - first);
  };
  slot.addEventListener('pointerdown', event => {
    const strip = event.target.closest('.dash-strip');
    if (!strip || event.button !== 0 || !event.target.closest('.dash-strip__track')) return;
    drag = { x: event.clientX, from: timeAt(strip, event.clientX), moved: false };
  });
  slot.addEventListener('pointermove', event => {
    if (!drag) return;
    if (!drag.moved && Math.abs(event.clientX - drag.x) < 4) return;
    if (!drag.moved) slot.setPointerCapture(event.pointerId);
    drag.moved = true;
    const to = timeAt(slot.querySelector('.dash-strip'), event.clientX);
    view.range = [Math.min(drag.from, to), Math.max(drag.from, to)];
    renderStrip();
  });
  slot.addEventListener('pointerup', event => {
    if (!drag) return;
    const { moved } = drag;
    drag = null;
    if (moved) {
      suppressClick = true;
      setRange(view.range);
    } else if (!event.target.closest('.dash-strip__mark') && view.range) {
      setRange(null);
    }
  });
  slot.addEventListener('click', event => {
    if (!suppressClick) return;
    suppressClick = false;
    event.stopPropagation();
  }, true);
  slot.addEventListener('dblclick', () => view.range && setRange(null));
}

function applySearch() {
  const query = view.query.trim().toLowerCase();
  const matches = new Set(query ? view.entries.filter(entry => entry.kind !== 'turn' && entrySearchText(entry).includes(query)).map(entry => entry.key) : []);
  panel().querySelectorAll('.dash-ledger-row, .dash-strip__mark').forEach(element => {
    const key = element.dataset.key;
    element.toggleAttribute('data-dimmed', Boolean(query) && !matches.has(key));
  });
}

export function renderDetail() {
  const session = allThreads() ? null : findSession(view.thread);
  const title = allThreads() ? 'All threads' : session ? sessionTitle(session) : view.thread;
  const moreFilter = MORE_FILTERS.find(option => option.value === view.filter);
  panel().innerHTML = `${panelHeader({
    titleHtml: `<span class="dash-truncate" data-tooltip="${esc(title)}">${esc(title)}</span>`,
    leading: iconButton('chevronLeft', 'Back to sessions', { iconSize: 16, attributes: { 'data-action': 'close-thread' } }),
    actions: `${session?.finalSystemPrompt ? button('System prompt', { variant: 'ghost', size: 'sm', iconName: 'fileText', attributes: { 'data-action': 'show-system-prompt', 'data-thread': session.sessionKey } }) : ''}
      <label class="dash-switch-label"><span>Follow new events</span>${pillSwitch(view.follow, 'Follow new events', 'toggle-follow')}</label>`
  })}
  <div class="dash-filter-bar">
    ${segmented('Show events', PRIMARY_FILTERS, moreFilter ? 'more' : view.filter, 'set-trace-filter')}
    ${iconButton('listFilter', moreFilter ? `Showing ${moreFilter.label}` : 'More event kinds', { active: Boolean(moreFilter), attributes: { 'aria-haspopup': 'menu', 'aria-expanded': 'false', 'data-action': 'open-trace-filters' } })}
    <label class="dash-search dash-search--compact">${icon('search', 15)}<input class="dc-field" data-bare data-input="search-trace" value="${esc(view.query)}" placeholder="Search events" aria-label="Search events"></label>
    <span class="dash-filter-bar__count dash-num">${countText()}</span>
  </div>
  ${body()}`;
  applyRange();
  applySearch();
  const strip = panel().querySelector('.dash-trace-strip-slot');
  if (strip) bindStrip(strip);
  const scroll = scroller();
  if (!scroll) return;
  scroll.addEventListener('scroll', onScroll);
  scroll.scrollTop = scroll.scrollHeight;
  view.nearBottom = true;
}

function renderLedger({ keepScroll } = {}) {
  const list = panel().querySelector('.dash-ledger');
  if (!list) {
    renderDetail();
    return;
  }
  const scroll = scroller();
  const fromBottom = scroll.scrollHeight - scroll.scrollTop;
  list.innerHTML = listHtml();
  renderStrip();
  applyRange();
  applySearch();
  if (keepScroll) scroll.scrollTop = scroll.scrollHeight - fromBottom;
  else if (view.follow && view.nearBottom) scroll.scrollTop = scroll.scrollHeight;
  updateChrome();
}

function updateChrome() {
  const jump = panel().querySelector('.dash-trace-jump');
  if (jump) jump.innerHTML = jumpButton();
  const count = panel().querySelector('.dash-filter-bar__count');
  if (count) count.textContent = countText();
}

function onScroll() {
  const scroll = scroller();
  view.nearBottom = scroll.scrollHeight - scroll.scrollTop - scroll.clientHeight < 80;
  if (view.nearBottom) view.unseen = 0;
  updateChrome();
  if (scroll.scrollTop < 120) loadOlder();
}

async function fetchPage(beforeCursor) {
  const params = new URLSearchParams({ limit: String(PAGE_LIMIT) });
  if (view.filter !== 'all') params.set('filter', view.filter);
  if (beforeCursor) params.set('beforeCursor', beforeCursor);
  const base = allThreads() ? '/events/page' : `/sessions/${encodeURIComponent(view.thread)}/events/page`;
  return fetchJson(`${base}?${params}`);
}

export async function loadThread() {
  const thread = view.thread;
  panel().innerHTML = loadingPanel();
  const [page, operations] = await Promise.all([
    fetchPage(null),
    allThreads() ? [] : fetchJson(`/sessions/${encodeURIComponent(thread)}/operations`),
    state.sessions.length ? null : loadSessions()
  ]);
  if (view.thread !== thread) return;
  view.raw = Array.isArray(page?.events) ? page.events : [];
  view.operations = Array.isArray(operations) ? operations : [];
  view.oldestCursor = page?.oldestCursor ?? null;
  view.hasMore = page?.hasMore === true;
  view.unseen = 0;
  rebuild();
  if (!selectedEntry()) view.selected = null;
  renderDetail();
}

async function loadOlder() {
  if (view.loadingOlder || !view.hasMore || !view.oldestCursor) return;
  view.loadingOlder = true;
  const thread = view.thread;
  panel().querySelector('.dash-trace-older').outerHTML = olderRow();
  const page = await fetchPage(view.oldestCursor);
  view.loadingOlder = false;
  if (view.thread !== thread) return;
  const known = new Set(view.raw.map(eventKey));
  const older = (Array.isArray(page?.events) ? page.events : []).filter(event => !known.has(eventKey(event)));
  view.oldestCursor = page?.oldestCursor ?? view.oldestCursor;
  view.hasMore = page?.hasMore === true;
  view.raw = [...older, ...view.raw];
  rebuild();
  renderLedger({ keepScroll: true });
}

export function handleLiveTraceEvent(event) {
  if (state.panel !== 'sessions' || !view.thread) return;
  if (!allThreads() && event.sessionKey !== view.thread) return;
  if (!isVisibleEvent(event) || !matchesFilter(event, view.filter)) return;
  if (view.raw.some(existing => eventKey(existing) === eventKey(event))) return;
  view.raw.push(event);
  rebuild();
  if (!(view.follow && view.nearBottom)) view.unseen += 1;
  renderLedger();
  if (view.selected) renderInspector();
}

export function openThread(thread) {
  view.thread = thread;
  view.filter = 'all';
  view.query = '';
  view.selected = null;
  view.range = null;
  view.raw = [];
  view.operations = [];
  view.events = [];
  view.loadingOlder = false;
  inspectorState.full.clear();
  inspectorState.metadata.clear();
  if (state.panel === 'sessions') loadThread();
  else switchPanel('sessions');
}

export function closeThread() {
  view.thread = null;
}

function renderInspector() {
  const slot = panel().querySelector('.dash-trace-inspector-slot');
  if (slot) slot.innerHTML = inspector(selectedEntry());
}

function select(key) {
  view.selected = view.selected === key ? null : key;
  const entry = selectedEntry();
  if (entry && outsideRange(entry)) setRange(null);
  panel().querySelector('.dash-trace-layout')?.classList.toggle('dash-trace-layout--inspecting', Boolean(view.selected));
  panel().querySelectorAll('.dash-ledger-row, .dash-strip__mark').forEach(element => {
    if (element.dataset.key === view.selected) element.setAttribute('aria-current', 'true');
    else element.removeAttribute('aria-current');
  });
  renderInspector();
  panel().querySelector(`.dash-ledger-row[data-key="${CSS.escape(key)}"]`)?.scrollIntoView({ block: 'nearest' });
}

function rerenderInspector(set, id) {
  if (set.has(id)) set.delete(id);
  else set.add(id);
  renderInspector();
}

function jumpToLatest() {
  const scroll = scroller();
  scroll?.scrollTo({ top: scroll.scrollHeight, behavior: 'smooth' });
  view.unseen = 0;
  updateChrome();
}

function setFilter(filter) {
  view.filter = filter;
  view.range = null;
  loadThread();
}

onAction('click', {
  'set-trace-filter': el => setFilter(el.dataset.value),
  'open-trace-filters': el => openMenu(el, MORE_FILTERS.map(option => ({
    label: option.label,
    iconName: view.filter === option.value ? 'check' : undefined,
    onSelect: () => setFilter(view.filter === option.value ? 'all' : option.value)
  }))),
  'toggle-follow': () => {
    view.follow = !view.follow;
    panel().querySelector('.dc-switch')?.setAttribute('aria-checked', String(view.follow));
    if (view.follow) jumpToLatest();
  },
  'jump-to-latest': () => jumpToLatest(),
  'load-older-traces': () => loadOlder(),
  'select-trace-entry': el => select(el.dataset.key),
  'close-trace-inspector': () => view.selected && select(view.selected),
  'toggle-inspector-full': el => rerenderInspector(inspectorState.full, el.dataset.id),
  'toggle-inspector-metadata': el => rerenderInspector(inspectorState.metadata, el.dataset.id)
});

document.addEventListener('keydown', event => {
  if (event.key === 'Escape' && view.range && state.panel === 'sessions') setRange(null);
});

onAction('input', {
  'search-trace': el => {
    view.query = el.value;
    applySearch();
  }
});
