import { onAction } from '../core/actions.js';
import { fetchJson, requestJson } from '../core/api.js';
import { button, emptyState, esc, icon, iconButton, loadingPanel, errorNotice, panelHeader, statusIndicator } from '../core/html.js';
import { panelElement, registerPanel, setNavMarker } from '../core/navigation.js';
import { capability } from '../core/state.js';
import { showToast } from '../core/toast.js';
import { validateJson } from './settings-fields.js';
import { renderGroups } from './settings-form.js';
import { collectForm, settings } from './settings-model.js';

let loaded = false;
let baseline = '';
let dirty = false;

const panel = () => panelElement('settings');
const form = () => panel().querySelector('.dash-settings');

function signature() {
  return JSON.stringify(collectForm(form(), { rawJson: true }));
}

function setDirty(next) {
  dirty = next;
  setNavMarker('settings', dirty ? statusIndicator('warning', 'Unsaved changes') : '');
  const bar = panel().querySelector('.dash-save-bar');
  if (!bar) return;
  bar.querySelector('.dash-save-bar__status').hidden = !dirty;
  bar.querySelectorAll('.dc-button').forEach(btn => { btn.disabled = !dirty; });
}

function checkDirty() {
  if (form()) setDirty(signature() !== baseline);
}

function render() {
  panel().innerHTML = `${panelHeader({
    title: 'Settings',
    description: settings.configPath ? `Saved to <code class="dash-mono">${esc(settings.configPath)}</code>` : '',
    actions: iconButton('refreshCw', 'Reload from disk', { attributes: { 'data-action': 'reload-settings' } })
  })}
  <div class="dash-panel-body dash-settings">
    <label class="dash-search">${icon('search', 15)}<input class="dc-field" data-bare data-input="filter-settings" placeholder="Search settings" aria-label="Search settings"></label>
    ${renderGroups()}
  </div>
  <div class="dash-save-bar" role="region" aria-label="Save changes">
    <span class="dash-status dash-save-bar__status" hidden>${statusIndicator('warning')}Unsaved changes</span>
    <span class="dash-save-bar__spacer"></span>
    ${button('Cancel', { size: 'sm', disabled: true, attributes: { 'data-action': 'cancel-settings' } })}
    ${button('Save workspace settings', { variant: 'primary', size: 'sm', disabled: true, attributes: { 'data-action': 'save-settings' } })}
  </div>`;
  baseline = signature();
  setDirty(false);
  const body = form();
  for (const type of ['input', 'change', 'click']) body.addEventListener(type, () => queueMicrotask(checkDirty));
}

function renderMessage(html) {
  panel().innerHTML = `${panelHeader({ title: 'Settings' })}<div class="dash-panel-body">${html}</div>`;
}

async function loadModelOptions() {
  settings.modelOptions = [];
  settings.modelError = '';
  const data = await requestJson('/config/models', 'GET').catch(error => error.body ?? { errorMessage: error.message });
  if (data?.success === true && Array.isArray(data.models)) {
    const ids = data.models.map(model => String(model?.id ?? model?.Id ?? '').trim()).filter(Boolean);
    settings.modelOptions = [...new Set(ids)].sort((a, b) => a.localeCompare(b));
  } else {
    settings.modelError = data?.errorMessage ? String(data.errorMessage) : 'The model list isn’t available.';
  }
}

async function refreshSettings() {
  if (!capability('settings')) {
    renderMessage(emptyState('lock', 'Settings can’t be changed in the read-only viewer', 'This Dashboard was opened with dotcraft dashboard, which only reads traces. Open it from a running DotCraft to edit workspace settings.'));
    return;
  }
  if (!loaded) panel().innerHTML = loadingPanel();
  const [schema, data] = await Promise.all([fetchJson('/config/schema'), fetchJson('/config/edit'), loadModelOptions()]);
  loaded = true;
  if (!Array.isArray(schema) || !data) {
    renderMessage(errorNotice('Settings couldn’t be loaded', '', button('Try again', { size: 'sm', attributes: { 'data-action': 'reload-settings' } })));
    return;
  }
  settings.schema = schema;
  settings.global = data.global ?? {};
  settings.workspace = data.workspace ?? {};
  settings.configPath = data.workspacePath ?? '';
  render();
}

async function saveSettings() {
  const invalid = [...form().querySelectorAll('[data-input="validate-json"]')].filter(textarea => !validateJson(textarea));
  if (invalid.length) {
    invalid[0].focus();
    showToast('Fix the JSON before saving.', 'error');
    return;
  }
  const data = collectForm(form());
  const save = panel().querySelector('[data-action="save-settings"]');
  save.disabled = true;
  try {
    await requestJson('/config/workspace', 'POST', data);
    showToast('Workspace settings saved. Some changes apply after DotCraft restarts.', 'success');
    await refreshSettings();
  } catch (error) {
    showToast(`Workspace settings weren’t saved. ${error.message}`, 'error');
    save.disabled = false;
  }
}

function filterSettings(query) {
  const q = query.trim().toLowerCase();
  form().querySelectorAll('.dc-settings-group').forEach(group => {
    const titleMatch = !q || group.dataset.search.includes(q);
    let any = false;
    group.querySelectorAll('[data-search-row]').forEach(row => {
      const match = titleMatch || row.textContent.toLowerCase().includes(q) || row.dataset.searchRow.includes(q);
      row.hidden = !match;
      any ||= match;
    });
    group.hidden = !titleMatch && !any;
  });
}

registerPanel('settings', () => {
  if (!dirty) refreshSettings();
});
onAction('click', {
  'reload-settings': () => refreshSettings(),
  'save-settings': () => saveSettings(),
  'cancel-settings': () => render()
});
onAction('input', { 'filter-settings': el => filterSettings(el.value) });
