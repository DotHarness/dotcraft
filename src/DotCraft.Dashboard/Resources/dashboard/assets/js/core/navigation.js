import { onAction } from './actions.js';
import { state } from './state.js';

const panels = {};

export function registerPanel(name, show, leave) {
  panels[name] = { show, leave };
}

export function panelElement(name) {
  return document.getElementById(`panel-${name}`);
}

function navDisabledReason(name) {
  if (name === 'dreams' && !state.available.dreams) {
    return state.runtime.readOnly ? 'Dreams isn’t available in the read-only viewer.' : 'Dreams isn’t set up for this workspace.';
  }
  if (name === 'automations' && !state.available.automations) {
    return state.runtime.readOnly ? 'Automations need a running DotCraft.' : 'Automations need the Automations module.';
  }
  return '';
}

export function switchPanel(name) {
  if (navDisabledReason(name)) return;
  if (state.panel !== name) panels[state.panel]?.leave?.();
  state.panel = name;
  document.querySelectorAll('.dash-nav-row').forEach(row => {
    if (row.dataset.panel === name) row.setAttribute('aria-current', 'page');
    else row.removeAttribute('aria-current');
  });
  document.querySelectorAll('.dash-content > .dash-panel').forEach(panel => {
    panel.hidden = panel.id !== `panel-${name}`;
  });
  panels[name]?.show();
}

export function applyNavState() {
  document.querySelectorAll('.dash-nav-row').forEach(row => {
    const reason = navDisabledReason(row.dataset.panel);
    row.dataset.tooltip = reason || row.getAttribute('aria-label');
    if (reason) row.setAttribute('aria-disabled', 'true');
    else row.removeAttribute('aria-disabled');
  });
  if (navDisabledReason(state.panel)) switchPanel('overview');
}

export function setNavMarker(name, html) {
  const marker = document.querySelector(`.dash-nav-row[data-panel="${name}"] .dash-nav-row__marker`);
  if (marker) {
    marker.innerHTML = html;
    marker.hidden = !html;
  }
}

onAction('click', { 'switch-panel': el => switchPanel(el.dataset.panel) });
