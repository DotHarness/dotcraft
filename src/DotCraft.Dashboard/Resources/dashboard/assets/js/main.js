import { installActions } from './core/actions.js';
import { connectLiveEvents } from './core/connection.js';
import { icon } from './core/html.js';
import { initHoverCards } from './core/hovercard.js';
import { applyNavState, switchPanel } from './core/navigation.js';
import { loadRuntime } from './core/runtime.js';
import { state } from './core/state.js';
import { initTheme } from './core/theme.js';
import { initTooltips } from './core/tooltip.js';
import { initWorkspacePicker, showRuntimeUnavailable } from './core/workspace-picker.js';
import { probeAutomations } from './panels/automations.js';
import { handleDreamsHash, probeDreams } from './panels/dreams.js';
import { refreshOverviewSoon } from './panels/overview.js';
import { hasUnsavedSettings } from './panels/settings.js';
import { handleLiveTraceEvent } from './panels/sessions.js';
import './panels/usage.js';

function hydrateIcons() {
  document.querySelectorAll('[data-icon]').forEach(slot => {
    slot.innerHTML = icon(slot.dataset.icon, Number(slot.dataset.iconSize || 16), Number(slot.dataset.iconStroke || 2));
  });
}

async function init() {
  hydrateIcons();
  installActions();
  initTheme();
  initTooltips();
  initHoverCards();
  if (!(await initWorkspacePicker(hasUnsavedSettings))) return;
  const runtime = await loadRuntime();
  if (runtime === 'sign-in') return;
  if (runtime === 'unavailable') {
    showRuntimeUnavailable();
    return;
  }

  await Promise.all([probeDreams(), probeAutomations()]);
  applyNavState();
  document.querySelectorAll('.dash-nav').forEach(nav => { nav.hidden = false; });
  switchPanel('overview');
  handleDreamsHash();
  window.addEventListener('hashchange', handleDreamsHash);
  connectLiveEvents(event => {
    handleLiveTraceEvent(event);
    refreshOverviewSoon();
  }, () => switchPanel(state.panel));
}

init();
