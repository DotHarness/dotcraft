import { onAction } from '../core/actions.js';
import { fetchJson, requestJson } from '../core/api.js';
import { confirmDialog } from '../core/dialog.js';
import { formatRelative, formatUntil } from '../core/format.js';
import { button, emptyState, esc, facts, iconButton, loadingPanel, panelHeader, statusIndicator } from '../core/html.js';
import { registerHoverCard } from '../core/hovercard.js';
import { openMenu, selectHtml } from '../core/menu.js';
import { applyNavState, panelElement, registerPanel, switchPanel } from '../core/navigation.js';
import { capability, state } from '../core/state.js';
import { showToast } from '../core/toast.js';
import { renderPanel } from './panel.js';
import { DREAMS_FILTERS, detailOpen, detailSkeleton, dreamChangeSummary, dreamRunCard, dreamRunDetail, dreamStatus, dreamTitle } from './dreams-view.js';

const DONE = {
  apply: 'Changes applied to memory.',
  restore: 'Memory restored to how it was before this run.',
  discard: 'Run discarded. Memory is unchanged.',
  archive: 'Run archived.',
  cancel: 'Cancel requested.'
};
const CONFLICT = {
  apply: 'Memory changed after this run started, so its result wasn’t applied. Run Dreams again to consolidate the current memory.',
  restore: 'Memory changed after this run was applied, so it no longer matches the run’s result and wasn’t restored.'
};
const BUSY_REASON = 'Runs can’t be deleted while one is in progress.';

const dreams = { loaded: false, status: {}, runs: [], selectedId: null, details: null, detailLoading: false, filter: 'all', deleting: null };

const anyRunning = () => Boolean(dreams.status.running) || dreams.runs.some(run => run.status === 'running');
const deleteBlocked = () => anyRunning() || dreams.deleting !== null;
const count = id => (id === 'all' ? dreams.runs.length : dreams.runs.filter(DREAMS_FILTERS[id]).length);

function runNowReason() {
  if (anyRunning()) return 'A run is already in progress.';
  return dreams.status.enabled ? '' : 'Dreams is off for this workspace.';
}

function runList() {
  const visible = dreams.filter === 'all' ? dreams.runs : dreams.runs.filter(DREAMS_FILTERS[dreams.filter]);
  if (!visible.length) return '<p class="dash-dim dash-small dash-dreams__none">No runs match this filter.</p>';
  return `<ul class="dash-select-list" aria-label="Dreams runs">${visible.map(run => {
    const status = dreamStatus(run);
    const changes = dreamChangeSummary(run);
    return `<li><button type="button" class="dash-select-row" data-action="select-dream-run" data-run="${esc(run.id)}"${run.id === dreams.selectedId ? ' aria-current="true"' : ''}${run.reviewStatus === 'archived' ? ' data-faded' : ''}>
      <span class="dash-select-row__mark">${statusIndicator(status.tone, status.label)}</span>
      <span class="dash-select-row__text">
        <span class="dash-select-row__title">${esc(dreamTitle(run))}</span>
        <span class="dash-select-row__sub">${facts([run.scope ? 'Agent memory' : 'Workspace memory', ...(changes.length ? changes : [status.label])].map(esc))}</span>
      </span>
      <span class="dash-select-row__meta dash-num">${formatRelative(run.endedAt ?? run.startedAt)}</span>
    </button></li>`;
  }).join('')}</ul>`;
}

function detail() {
  if (dreams.detailLoading) return detailSkeleton();
  const data = dreams.details?.run?.id === dreams.selectedId ? dreams.details : null;
  const run = data?.run ?? dreams.runs.find(candidate => candidate.id === dreams.selectedId);
  return run ? dreamRunDetail(run, data) : emptyState('moon', 'Select a run to review');
}

function render() {
  const status = dreams.status;
  const reason = runNowReason();
  const next = status.enabled && status.nextRunAt ? `Next scheduled run ${formatUntil(status.nextRunAt)}` : 'No scheduled runs';
  const runNow = button('Run now', { variant: 'primary', size: 'sm', loading: anyRunning(), disabled: Boolean(reason), attributes: { 'data-action': 'run-dreams' } });
  renderPanel(panelElement('dreams'), `${panelHeader({
    title: 'Dreams',
    actions: `${iconButton('refreshCw', 'Refresh', { attributes: { 'data-action': 'refresh-dreams' } })}
      <span class="dc-tooltip-anchor" data-tooltip="${esc(reason || next)}">${runNow}</span>`
  })}
  <div class="dash-panel-body">
    ${dreams.runs.length ? `<div class="dash-dreams">
      <div class="dash-dreams__list">
        <div class="dash-dreams__list-head">
          ${selectHtml({
            value: dreams.filter,
            ariaLabel: 'Show runs',
            appearance: 'frameless',
            change: 'set-dreams-filter',
            options: [
              { value: 'all', label: `All runs (${count('all')})` },
              { value: 'pending', label: `Needs review (${count('pending')})` },
              { value: 'applied', label: `Applied (${count('applied')})` },
              { value: 'archived', label: `Archived (${count('archived')})` },
              { value: 'other', label: `Discarded, failed, or skipped (${count('other')})` }
            ]
          })}
          ${iconButton('trash2', 'Delete all runs', { tone: 'danger', disabled: deleteBlocked(), disabledReason: BUSY_REASON, attributes: { 'data-action': 'delete-all-dream-runs' } })}
        </div>
        ${runList()}
      </div>
      <div class="dash-dreams__detail">${detail()}</div>
    </div>` : emptyState('moon', 'No Dreams runs yet', 'Runs appear here after the first scheduled run, or when you choose Run now.')}
  </div>`);
}

async function loadRun(runId) {
  dreams.selectedId = runId;
  dreams.detailLoading = true;
  render();
  const data = await fetchJson('/dreams/runs/' + encodeURIComponent(runId));
  if (dreams.selectedId !== runId) return;
  dreams.detailLoading = false;
  dreams.details = data;
  render();
}

async function refreshDreams() {
  if (!dreams.loaded) panelElement('dreams').innerHTML = loadingPanel();
  const [status, list] = await Promise.all([fetchJson('/dreams/status'), fetchJson('/dreams/runs')]);
  dreams.loaded = true;
  dreams.status = status ?? {};
  dreams.runs = Array.isArray(list?.runs) ? list.runs : [];
  if (!dreams.runs.some(run => run.id === dreams.selectedId)) {
    dreams.selectedId = (dreams.runs.find(run => run.reviewStatus === 'pending' && run.status !== 'running') ?? dreams.runs[0])?.id ?? null;
  }
  if (dreams.selectedId) await loadRun(dreams.selectedId);
  else render();
}

function selectRun(runId) {
  history.replaceState(null, '', '#dreams/run/' + encodeURIComponent(runId));
  detailOpen.clear();
  loadRun(runId);
}

export async function probeDreams() {
  const status = capability('dreams') ? await fetchJson('/dreams/status') : null;
  state.available.dreams = status !== null;
  applyNavState();
}

export function handleDreamsHash() {
  const hash = window.location.hash || '';
  const match = /^#dreams\/run\/(.+)$/.exec(hash);
  if (hash !== '#dreams' && !match) return;
  if (match) dreams.selectedId = decodeURIComponent(match[1]);
  if (state.panel !== 'dreams') switchPanel('dreams');
  else if (match) loadRun(dreams.selectedId);
}

async function runAction(runId, action) {
  try {
    await requestJson(`/dreams/runs/${encodeURIComponent(runId)}/${action}`, 'POST');
    showToast(DONE[action], 'success');
  } catch (error) {
    if (error.status === 409 && CONFLICT[action]) showToast(CONFLICT[action], 'error', 10000);
    else showToast(error.message, 'error');
  }
  await refreshDreams();
}

async function runNow() {
  try {
    const result = await requestJson('/dreams/run', 'POST');
    showToast(result?.run ? 'Dreams run requested.' : 'Dreams is off for this workspace.', result?.run ? 'success' : 'info');
  } catch (error) {
    showToast(error.message, 'error');
  }
  await refreshDreams();
}

async function deleteRuns(runId) {
  const all = runId === null;
  const confirmed = await confirmDialog({
    title: all ? `Delete all ${dreams.runs.length} runs?` : 'Delete this run?',
    message: 'Run data, its internal thread, and its trace are deleted for good. Memory is not changed.',
    confirmLabel: 'Delete',
    danger: true
  });
  if (!confirmed || deleteBlocked()) return;
  dreams.deleting = all ? 'all' : runId;
  render();
  try {
    const result = await requestJson(all ? '/dreams/runs' : `/dreams/runs/${encodeURIComponent(runId)}`, 'DELETE');
    const deletedCount = result?.deletedCount ?? 0;
    showToast(all ? `Deleted ${deletedCount} Dream ${deletedCount === 1 ? 'run' : 'runs'}.` : 'Dream run deleted.', 'success');
    const failures = Array.isArray(result?.traceCleanupFailures) ? result.traceCleanupFailures : [];
    for (const failure of failures) {
      showToast(`Run deleted, but its trace couldn’t be removed${failure?.threadId ? ` for thread ${failure.threadId}` : ''}${failure?.error ? `: ${failure.error}` : '.'}`, 'warning', 10000);
    }
    if (result?.partial && !failures.length) showToast('Runs were deleted, but some traces couldn’t be removed.', 'warning', 10000);
  } catch (error) {
    showToast(error.message, 'error');
  }
  dreams.deleting = null;
  await refreshDreams();
  history.replaceState(null, '', dreams.selectedId ? '#dreams/run/' + encodeURIComponent(dreams.selectedId) : '#dreams');
}

registerHoverCard('dream-run', target => {
  const run = dreams.runs.find(candidate => candidate.id === target.dataset.run);
  return run ? dreamRunCard(run) : '';
});

function openRunMenu(anchor, runId) {
  const run = dreams.runs.find(candidate => candidate.id === runId);
  if (!run) return;
  const items = [];
  if (run.reviewStatus === 'applied') items.push({ label: 'Restore memory…', iconName: 'rotateCcw', onSelect: () => restoreRun(runId) });
  if (run.status !== 'running' && run.reviewStatus !== 'archived') items.push({ label: 'Archive', iconName: 'archive', onSelect: () => runAction(runId, 'archive') });
  if (items.length) items.push('separator');
  items.push({ label: 'Delete run…', iconName: 'trash2', danger: true, disabled: deleteBlocked(), title: BUSY_REASON, onSelect: () => deleteRuns(runId) });
  openMenu(anchor, items, { align: 'right' });
}

async function restoreRun(runId) {
  const confirmed = await confirmDialog({
    title: 'Restore memory?',
    message: 'Memory goes back to how it was before this run, and the run is marked discarded.',
    confirmLabel: 'Restore'
  });
  if (confirmed) await runAction(runId, 'restore');
}

registerPanel('dreams', refreshDreams);
onAction('click', {
  'refresh-dreams': () => refreshDreams(),
  'run-dreams': () => runNow(),
  'select-dream-run': el => selectRun(el.dataset.run),
  'dream-action': el => runAction(el.dataset.run, el.dataset.op),
  'open-dream-menu': el => openRunMenu(el, el.dataset.run),
  'delete-all-dream-runs': () => deleteRuns(null),
  'toggle-dream-section': el => {
    const key = el.dataset.key;
    detailOpen.set(key, !(detailOpen.get(key) ?? el.dataset.default === 'true'));
    render();
    panelElement('dreams').querySelector(`[data-action="toggle-dream-section"][data-key="${CSS.escape(key)}"]`)?.focus();
  }
});
onAction('change', {
  'set-dreams-filter': el => {
    dreams.filter = el.value;
    render();
  }
});
