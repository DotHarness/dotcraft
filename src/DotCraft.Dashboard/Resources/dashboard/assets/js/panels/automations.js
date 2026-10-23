import { onAction } from '../core/actions.js';
import { fetchJson } from '../core/api.js';
import { formatCount, formatDateTime, formatUntil } from '../core/format.js';
import { emptyState, esc, iconButton, loadingPanel, panelHeader, statGrid, statusLabel, statTile } from '../core/html.js';
import { applyNavState, panelElement, registerPanel } from '../core/navigation.js';
import { capability, state } from '../core/state.js';
import { renderPanel } from './panel.js';

const STATE_URL = '/orchestrators/automations/state';
const STATUS_LABEL = { active: 'Active', paused: 'Paused', completed: 'Completed' };
let loaded = false;

function runsIn(automation) {
  if (automation.executionMode !== 'thread') return '<span class="dash-secondary">A new run each time</span>';
  return automation.targetThreadId
    ? `<button type="button" class="dash-link" data-action="open-thread" data-thread="${esc(automation.targetThreadId)}">Its conversation</button>`
    : '<span class="dash-dim">Conversation unavailable</span>';
}

function table(automations) {
  return `<div class="dash-table dash-automations" role="table" aria-label="Automations">
    <div class="dash-table__head" role="row">
      <span role="columnheader">Name</span>
      <span role="columnheader">Status</span>
      <span role="columnheader" class="dash-col--opt1">Runs in</span>
      <span role="columnheader" class="dash-col-num">Next run</span>
      <span role="columnheader" class="dash-col-num dash-col--opt2">Updated</span>
    </div>
    ${automations.map(automation => {
      const name = automation.name || automation.id || '';
      return `<div class="dash-table__row" role="row">
        <span role="cell" class="dash-truncate dash-strong" title="${esc(name)}">${esc(name)}</span>
        <span role="cell">${statusLabel(automation.status === 'active' ? 'success' : 'neutral', STATUS_LABEL[automation.status] || automation.status || '–')}</span>
        <span role="cell" class="dash-col--opt1">${runsIn(automation)}</span>
        <span role="cell" class="dash-col-num dash-num">${automation.status === 'active' ? esc(formatUntil(automation.nextRunAt)) : '–'}</span>
        <span role="cell" class="dash-col-num dash-num dash-dim dash-col--opt2">${esc(formatDateTime(automation.updatedAt || automation.createdAt))}</span>
      </div>`;
    }).join('')}
  </div>`;
}

export function renderAutomations(data) {
  const automations = Array.isArray(data.automations) ? data.automations : [];
  const count = status => data.countsByStatus?.[status] ?? automations.filter(automation => automation.status === status).length;
  renderPanel(panelElement('automations'), `${panelHeader({
    title: 'Automations',
    actions: iconButton('refreshCw', 'Refresh', { attributes: { 'data-action': 'refresh-automations' } })
  })}
  <div class="dash-panel-body">
    ${automations.length
      ? `${statGrid('', [
        statTile('Active', formatCount(count('active'))),
        statTile('Paused', formatCount(count('paused'))),
        statTile('Completed', formatCount(count('completed')))
      ])}${table(automations)}`
      : emptyState('timer', 'No automations yet', 'Create one from Desktop or ask DotCraft to run something on a schedule.')}
  </div>`);
}

export async function probeAutomations() {
  state.available.automations = capability('automations') && (await fetchJson(STATE_URL)) !== null;
  applyNavState();
}

async function refreshAutomations(poll = false) {
  if (!loaded) panelElement('automations').innerHTML = loadingPanel();
  if (poll) await fetch('/dashboard/api/orchestrators/automations/refresh', { method: 'POST', credentials: 'same-origin' }).catch(() => null);
  const data = await fetchJson(STATE_URL);
  loaded = true;
  renderAutomations(data ?? {});
}

registerPanel('automations', () => refreshAutomations());
onAction('click', { 'refresh-automations': () => refreshAutomations(true) });
