import { onAction } from './actions.js';
import { fetchApi, redirectToLogin } from './api.js';
import { state } from './state.js';
import { dashboardPath } from './workspace-route.js';

export async function loadRuntime() {
  const res = await fetchApi('/runtime').catch(() => null);
  if (res?.status === 401) return 'sign-in';
  const data = res?.ok ? await res.json().catch(() => null) : null;
  if (!data) return 'unavailable';

  const { readOnly, workspacePath, capabilities, authEnabled } = data;
  state.runtime = { readOnly, workspacePath, capabilities };
  const workspace = document.getElementById('workspacePath');
  workspace.textContent = workspacePath;
  workspace.parentElement.title = workspacePath;
  document.getElementById('readOnlyMode').hidden = !readOnly;
  document.getElementById('logoutButton').hidden = !authEnabled;
  return 'ready';
}

onAction('click', {
  logout: async () => {
    try {
      await fetch(`${dashboardPath()}/logout`, { method: 'POST' });
    } finally {
      redirectToLogin();
    }
  }
});
