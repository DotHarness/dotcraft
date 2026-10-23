import { onAction } from '../core/actions.js';
import { fetchJson } from '../core/api.js';
import { formatCount, formatPercent } from '../core/format.js';
import { emptyState, iconButton, loadingPanel, panelHeader, sectionHeading, statGrid, statTile } from '../core/html.js';
import { panelElement, registerPanel } from '../core/navigation.js';
import { state } from '../core/state.js';
import { renderPanel } from './panel.js';
import { tokenMixBar } from './charts.js';
import { loadSessions, sessionTable } from './session-list.js';

let loaded = false;

function render(summary) {
  const sessions = state.sessions;
  renderPanel(panelElement('overview'), `${panelHeader({
    title: 'Overview',
    actions: iconButton('refreshCw', 'Refresh', { attributes: { 'data-action': 'refresh-overview' } })
  })}
  <div class="dash-panel-body">
    ${statGrid('Activity', [
      statTile('Threads', formatCount(summary.sessionCount)),
      statTile('Requests', formatCount(summary.totalRequests)),
      statTile('Tool calls', formatCount(summary.totalToolCalls)),
      statTile('Maintenance', formatCount(summary.totalMaintenanceForkRequests)),
      statTile('Errors', formatCount(summary.totalErrors))
    ])}
    ${statGrid('Tokens', [
      statTile('Total tokens', formatCount(summary.totalTokens)),
      statTile('Served from cache', formatPercent(summary.cacheHitRate)),
      statTile('Output', formatCount(summary.totalOutputTokens))
    ])}
    ${tokenMixBar('Where tokens went', [
      { label: 'Fresh input', value: summary.totalFreshInputTokens },
      { label: 'Cache hit', value: summary.totalCachedInputTokens },
      { label: 'Cache write', value: summary.totalCacheWriteInputTokens },
      { label: 'Output', value: summary.totalOutputTokens }
    ])}
    ${sectionHeading('Recent threads')}
    ${sessions.length
      ? sessionTable(sessions.slice(0, 5), { listId: 'overview' })
      : emptyState('activity', 'No activity yet', 'Start a conversation in DotCraft and its requests show up here.')}
  </div>`);
}

async function refreshOverview() {
  if (!loaded) panelElement('overview').innerHTML = loadingPanel();
  const [summary] = await Promise.all([fetchJson('/summary'), loadSessions()]);
  loaded = true;
  render(summary ?? {});
}

let refreshTimer = null;
export function refreshOverviewSoon() {
  if (state.panel !== 'overview' || refreshTimer) return;
  refreshTimer = setTimeout(() => {
    refreshTimer = null;
    refreshOverview();
  }, 1500);
}

registerPanel('overview', refreshOverview);
onAction('click', { 'refresh-overview': () => refreshOverview() });
