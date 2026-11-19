import { onAction } from './actions.js';
import { confirmDialog } from './dialog.js';
import { button, emptyState, errorNotice } from './html.js';
import { selectHtml } from './menu.js';
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

export async function initWorkspacePicker(hasUnsavedSettings) {
  if (!document.body.hasAttribute('data-dashboard-shared')) return true;

  const selectedId = currentWorkspaceId();
  const slot = document.getElementById('workspaceSelector');
  slot.hidden = false;
  document.getElementById('workspaceLabel').hidden = true;
  const response = await fetch('/dashboard/api/workspaces').catch(() => null);
  const data = response?.ok ? await response.json().catch(() => null) : null;
  if (!Array.isArray(data?.workspaces)) {
    showNotice(errorNotice('Workspaces couldn’t be loaded', '', refreshButton()));
    return false;
  }

  const { workspaces } = data;
  const available = workspaces.filter(workspace => workspace.state === 'available');
  const hasAvailable = available.length > 0;
  const selected = workspaces.find(workspace => workspace.id === selectedId);
  const current = selected?.state === 'available' ? selected : null;

  slot.innerHTML = selectHtml({
    value: current?.id ?? '',
    ariaLabel: 'Workspace',
    appearance: 'frameless',
    className: 'dash-workspace__select',
    iconName: 'folderOpen',
    placeholder: 'Select a workspace',
    menuWidth: 240,
    disabled: !hasAvailable,
    options: available.map(workspace => ({ value: workspace.id, label: workspace.name }))
  });
  const trigger = slot.querySelector('[role="combobox"]');
  trigger.addEventListener('change', async () => {
    const destination = trigger.value;
    trigger.value = current?.id ?? '';
    trigger.toggleAttribute('data-placeholder', !current);
    trigger.querySelector('.dc-settings-select__value').textContent = current?.name ?? 'Select a workspace';
    if (hasUnsavedSettings() && !(await confirmDialog({
      title: 'Discard unsaved changes?',
      message: 'Switching workspaces discards your unsaved settings.',
      confirmLabel: 'Discard and switch'
    }))) return;
    location.assign(`${dashboardPath(destination)}/`);
  });

  if (selected?.state === 'available') return true;

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
