import { onAction } from './actions.js';
import { fetchJson } from './api.js';
import { capability, state } from './state.js';

export async function loadRuntime() {
  const data = await fetchJson('/runtime');
  if (data) {
    state.runtime = {
      readOnly: data.readOnly === true,
      workspacePath: data.workspacePath || '',
      capabilities: {
        settings: data.capabilities?.settings !== false,
        dreams: data.capabilities?.dreams !== false,
        automations: data.capabilities?.automations !== false,
        sessionDeletion: data.capabilities?.sessionDeletion !== false
      }
    };
  }
  const workspace = document.getElementById('workspacePath');
  workspace.textContent = state.runtime.workspacePath;
  workspace.parentElement.title = state.runtime.workspacePath;
  document.getElementById('readOnlyMode').hidden = !state.runtime.readOnly;
}

export async function showLogoutWhenAuthEnabled() {
  if (!capability('settings')) return;
  const res = await fetch('/dashboard/api/config/edit', { credentials: 'same-origin' }).catch(() => null);
  if (res?.status === 401) {
    window.location.href = '/dashboard/login';
    return;
  }
  const data = res?.ok ? await res.json().catch(() => null) : null;
  document.getElementById('logoutButton').hidden = !data?.authEnabled;
}

onAction('click', {
  logout: async () => {
    try {
      await fetch('/dashboard/logout', { method: 'POST', credentials: 'same-origin' });
    } finally {
      window.location.href = '/dashboard/login';
    }
  }
});
