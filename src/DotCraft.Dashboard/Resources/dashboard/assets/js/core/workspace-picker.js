import { onAction } from './actions.js';
import { confirmDialog } from './dialog.js';
import { button, emptyState, errorNotice } from './html.js';
import { openSelect, selectHtml } from './menu.js';
import { currentWorkspaceId, dashboardPath } from './workspace-route.js';

const unavailableLabels = { stopped: 'Not running', disabled: 'Dashboard disabled' };
const refreshButton = () => button('Refresh', { size: 'sm', attributes: { 'data-action': 'refresh-workspaces' } });

function showNotice(html) {
  const slot = document.getElementById('workspaceNotice');
  slot.innerHTML = html;
  slot.hidden = false;
}

export function showRuntimeUnavailable() {
  showNotice(errorNotice('Dashboard is unavailable', 'Check that DotCraft is running and try again.', refreshButton()));
}

async function loadWorkspaces() {
  const response = await fetch('/dashboard/api/workspaces').catch(() => null);
  const data = response?.ok ? await response.json().catch(() => null) : null;
  return Array.isArray(data?.workspaces) ? data.workspaces : null;
}

function availableOptions(workspaces) {
  return workspaces.filter(workspace => workspace.state === 'available')
    .map(workspace => ({ value: workspace.id, label: workspace.name, title: workspace.name }));
}

export async function initWorkspacePicker(hasUnsavedSettings) {
  if (!document.body.hasAttribute('data-dashboard-shared')) return true;

  const selectedId = currentWorkspaceId();
  const slot = document.getElementById('workspaceSelector');
  slot.hidden = false;
  document.getElementById('workspaceLabel').hidden = true;
  const workspaces = await loadWorkspaces();
  const selected = workspaces?.find(workspace => workspace.id === selectedId);
  const current = selected?.state === 'available' ? selected : null;
  const currentId = current?.id ?? '';
  let currentName = current?.name;
  const placeholder = 'Select a workspace';

  slot.innerHTML = selectHtml({
    value: currentId,
    ariaLabel: 'Workspace',
    appearance: 'frameless',
    action: 'open-workspace-select',
    className: 'dash-workspace__select',
    iconName: 'folderOpen',
    placeholder,
    menuWidth: 240,
    options: current ? [{ value: currentId, label: currentName }] : []
  });
  const trigger = slot.querySelector('[role="combobox"]');
  const restoreSelection = () => {
    trigger.value = currentId;
    trigger.toggleAttribute('data-placeholder', !currentId);
    trigger.title = currentName ?? '';
    trigger.querySelector('.dc-settings-select__value').textContent = currentName ?? placeholder;
  };
  restoreSelection();
  trigger.addEventListener('change', async () => {
    const destination = trigger.value;
    restoreSelection();
    if (hasUnsavedSettings() && !(await confirmDialog({
      title: 'Discard unsaved changes?',
      message: 'Switching workspaces discards your unsaved settings.',
      confirmLabel: 'Discard and switch'
    }))) return;
    location.assign(`${dashboardPath(destination)}/`);
  });
  onAction('click', {
    'open-workspace-select': element => openSelect(element, async updateOptions => {
      const fresh = await loadWorkspaces();
      const options = fresh ? availableOptions(fresh) : [];
      const empty = { label: fresh ? 'No workspaces available' : 'Workspaces couldn’t be loaded', disabled: true };
      if (!updateOptions(options.length ? options : [empty])) return;
      currentName = fresh?.find(workspace => workspace.id === currentId)?.name ?? currentName;
      restoreSelection();
    })
  });

  if (current) return true;
  if (!workspaces) {
    showNotice(errorNotice('Workspaces couldn’t be loaded', '', refreshButton()));
    return false;
  }

  const hasAvailable = workspaces.some(workspace => workspace.state === 'available');

  const title = selectedId
    ? (unavailableLabels[selected?.state] ?? 'Workspace unavailable')
    : (hasAvailable ? 'Choose a workspace' : 'No workspaces available');
  const detail = hasAvailable
    ? 'Select an available workspace above.'
    : 'Open a workspace with Dashboard enabled to see it here.';
  showNotice(emptyState('folderOpen', title, detail, refreshButton()));
  return false;
}

onAction('click', { 'refresh-workspaces': () => location.reload() });
