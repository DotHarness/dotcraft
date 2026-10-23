import { onAction } from '../core/actions.js';
import { fetchJson } from '../core/api.js';
import { formatCount, formatPercent, formatRelative, humanizeKey } from '../core/format.js';
import { emptyState, esc, iconButton, loadingPanel, panelHeader, sectionHeading, segmented, statGrid, statTile } from '../core/html.js';
import { panelElement, registerPanel } from '../core/navigation.js';
import { horizontalBars, tokenMixBar } from './charts.js';
import { renderPanel } from './panel.js';

const KIND_PLURAL = { user: 'people', thread: 'threads', session: 'sessions', group: 'groups', mixed: 'records' };
const KIND_TITLE = { user: 'Top people', thread: 'Top threads', session: 'Top sessions', group: 'Top groups', mixed: 'Top records' };

let loaded = false;
let data = null;
let filter = 'all';

function sourceLabel(id) {
  const value = String(id || '');
  if (value.toLowerCase() === 'acp') return 'ACP';
  if (value.length <= 3) return value.toUpperCase();
  const label = humanizeKey(value);
  return label.charAt(0).toUpperCase() + label.slice(1);
}

function kindKey(kind) {
  return String(kind || '').toLowerCase();
}

function breakdownTable(title, entries, relatedLabel) {
  if (!entries.length) {
    return `<div class="dash-breakdown"><h3 class="dash-subheading">${esc(title)}</h3><p class="dash-dim dash-small">Nothing recorded yet.</p></div>`;
  }
  const related = relatedLabel ? relatedLabel.charAt(0).toUpperCase() + relatedLabel.slice(1) : '';
  return `<div class="dash-breakdown"><h3 class="dash-subheading">${esc(title)}</h3>
    <div class="dash-table dash-usage-table" role="table" aria-label="${esc(title)}">
      <div class="dash-table__head" role="row">
        <span role="columnheader">Name</span>
        <span role="columnheader" class="dash-col-num">Total</span>
        <span role="columnheader" class="dash-col-num dash-col--opt1">Input</span>
        <span role="columnheader" class="dash-col-num dash-col--opt2">Fresh</span>
        <span role="columnheader" class="dash-col-num">Cached</span>
        <span role="columnheader" class="dash-col-num dash-col--opt2">Written</span>
        <span role="columnheader" class="dash-col-num dash-col--opt1">Output</span>
        <span role="columnheader" class="dash-col-num dash-col--opt2">Calls</span>
        <span role="columnheader" class="dash-col-num dash-col--opt2">${esc(related)}</span>
        <span role="columnheader" class="dash-col-num dash-col--opt1">Last active</span>
      </div>
      ${entries.map(entry => `<div class="dash-table__row" role="row">
        <span role="cell" class="dash-usage-name"><span class="dash-truncate" title="${esc(entry.id)}">${esc(entry.label || entry.id)}</span></span>
        <span role="cell" class="dash-col-num dash-num dash-strong">${formatCount(entry.totalTokens)}</span>
        <span role="cell" class="dash-col-num dash-num dash-col--opt1">${formatCount(entry.totalInputTokens)}</span>
        <span role="cell" class="dash-col-num dash-num dash-col--opt2">${formatCount(entry.totalFreshInputTokens)}</span>
        <span role="cell" class="dash-col-num dash-num">${formatPercent(entry.cacheHitRate, 0)}</span>
        <span role="cell" class="dash-col-num dash-num dash-col--opt2">${formatCount(entry.totalCacheWriteInputTokens)}</span>
        <span role="cell" class="dash-col-num dash-num dash-col--opt1">${formatCount(entry.totalOutputTokens)}</span>
        <span role="cell" class="dash-col-num dash-num dash-col--opt2">${formatCount(entry.llmCallCount)}</span>
        <span role="cell" class="dash-col-num dash-num dash-col--opt2">${relatedLabel ? formatCount(entry.relatedSubjectCount) : ''}</span>
        <span role="cell" class="dash-col-num dash-num dash-dim dash-col--opt1">${formatRelative(entry.lastActiveAt)}</span>
      </div>`).join('')}
    </div></div>`;
}

function sourceSection(source) {
  return `<section class="dash-detail-section">
    ${sectionHeading(sourceLabel(source.sourceId))}
    ${breakdownTable(KIND_TITLE[kindKey(source.subjectKind)] ?? 'Top records', source.subjects)}
    ${source.contextKind ? breakdownTable(KIND_TITLE[kindKey(source.contextKind)] ?? 'Top records', source.contexts, KIND_PLURAL[kindKey(source.subjectKind)] ?? 'records') : ''}
  </section>`;
}

function body() {
  if (!data) return emptyState('chartColumn', 'Token usage isn’t recorded here');
  const sources = data;
  if (!sources.length) return emptyState('chartColumn', 'No token usage yet', 'Usage is recorded after the first model call from Desktop, a channel, or an automation.');
  const sum = pick => sources.reduce((total, source) => total + (pick(source) || 0), 0);
  const input = sum(s => s.totalInputTokens);
  const cached = sum(s => s.totalCachedInputTokens);
  const visible = filter === 'all' ? sources : sources.filter(source => source.sourceId === filter);
  return `${statGrid('Totals', [
    statTile('Total tokens', formatCount(sum(s => s.totalTokens))),
    statTile('Served from cache', formatPercent(input ? cached / input : 0)),
    statTile('Model calls', formatCount(sum(s => s.llmCallCount))),
    statTile('Sources', formatCount(sources.length))
  ])}
  <div class="dash-chart-pair">
    ${tokenMixBar('Where tokens went', [
      { label: 'Fresh input', value: sum(s => s.totalFreshInputTokens) },
      { label: 'Cache hit', value: cached },
      { label: 'Cache write', value: sum(s => s.totalCacheWriteInputTokens) },
      { label: 'Output', value: sum(s => s.totalOutputTokens) }
    ])}
    ${horizontalBars('Tokens by source', sources.map(source => ({
      label: sourceLabel(source.sourceId),
      value: source.totalTokens,
      detail: `${formatPercent(source.cacheHitRate, 0)} cached`
    })))}
  </div>
  <div class="dash-filter-bar dash-filter-bar--inline">
    ${segmented('Source', [{ value: 'all', label: 'All sources' }, ...sources.map(source => ({ value: source.sourceId, label: sourceLabel(source.sourceId) }))], filter, 'set-usage-filter')}
  </div>
  ${visible.map(sourceSection).join('')}`;
}

function render() {
  renderPanel(panelElement('usage'), `${panelHeader({
    title: 'Token usage',
    actions: iconButton('refreshCw', 'Refresh', { attributes: { 'data-action': 'refresh-usage' } })
  })}<div class="dash-panel-body">${body()}</div>`);
}

async function loadUsage() {
  const sources = await fetchJson('/usage/sources');
  if (!Array.isArray(sources)) return null;
  return Promise.all(sources.map(async source => {
    const id = encodeURIComponent(source.sourceId);
    const [subjects, contexts] = await Promise.all([
      fetchJson(`/usage/sources/${id}/subjects`),
      source.contextKind ? fetchJson(`/usage/sources/${id}/contexts`) : null
    ]);
    return { ...source, subjects: Array.isArray(subjects) ? subjects : [], contexts: Array.isArray(contexts) ? contexts : [] };
  }));
}

async function refreshUsage() {
  if (!loaded) panelElement('usage').innerHTML = loadingPanel();
  data = await loadUsage();
  loaded = true;
  if (data && filter !== 'all' && !data.some(source => source.sourceId === filter)) filter = 'all';
  render();
}

registerPanel('usage', refreshUsage);
onAction('click', {
  'refresh-usage': () => refreshUsage(),
  'set-usage-filter': el => {
    filter = el.dataset.value;
    render();
  }
});
