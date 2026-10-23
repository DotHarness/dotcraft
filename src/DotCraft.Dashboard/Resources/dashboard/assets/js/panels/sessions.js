import { onAction } from '../core/actions.js';
import { requestJson } from '../core/api.js';
import { confirmDialog } from '../core/dialog.js';
import { sessionTitle } from '../core/format.js';
import { button, emptyState, loadingPanel, panelHeader } from '../core/html.js';
import { panelElement, registerPanel } from '../core/navigation.js';
import { capability, state } from '../core/state.js';
import { showToast } from '../core/toast.js';
import { findSession, loadSessions, sessionTable } from './session-list.js';
import { ALL_THREADS, activeThread, closeThread, loadThread, openThread } from './trace-view.js';

export { handleLiveTraceEvent } from './trace-view.js';

const panel = () => panelElement('sessions');

function renderList() {
  const sessions = state.sessions;
  const canDelete = capability('sessionDeletion');
  panel().innerHTML = `${panelHeader({
    title: 'Sessions',
    actions: canDelete && sessions.length
      ? button('Delete all', { variant: 'danger', size: 'sm', iconName: 'trash2', attributes: { 'data-action': 'delete-all-sessions' } })
      : ''
  })}
  <div class="dash-panel-body">${sessions.length
    ? sessionTable(sessions, { listId: 'sessions', canDelete, allThreadsKey: ALL_THREADS })
    : emptyState('messagesSquare', 'No sessions recorded yet')}</div>`;
}

async function refreshList() {
  await loadSessions();
  if (!activeThread()) renderList();
}

async function afterDelete(message) {
  showToast(message, 'success');
  await refreshList();
}

async function deleteSession(key) {
  const session = findSession(key);
  const cascade = session?.deletionScope === 'threadCascade';
  const confirmed = await confirmDialog({
    title: cascade ? 'Delete this conversation?' : 'Delete these traces?',
    message: cascade
      ? `“${sessionTitle(session)}” and every trace recorded for it are deleted for good.`
      : 'The traces for this thread are deleted for good. The conversation itself stays.',
    confirmLabel: 'Delete',
    danger: true
  });
  if (!confirmed) return;
  try {
    await requestJson('/sessions/' + encodeURIComponent(key), 'DELETE');
    await afterDelete('Deleted.');
  } catch (error) {
    if (error.status === 404) {
      showToast('This thread was already removed.', 'warning');
      await refreshList();
    } else {
      showToast(error.message, 'error');
    }
  }
}

async function deleteAllSessions() {
  const confirmed = await confirmDialog({
    title: 'Delete all sessions and traces?',
    message: 'Every conversation with traces, and every remaining trace, is deleted for good.',
    confirmLabel: 'Delete all',
    danger: true
  });
  if (!confirmed) return;
  try {
    await requestJson('/sessions', 'DELETE');
    await afterDelete('All sessions and traces deleted.');
  } catch (error) {
    showToast(error.message, 'error');
  }
}

registerPanel('sessions', async () => {
  if (activeThread()) {
    loadThread();
    return;
  }
  if (!state.sessions.length) panel().innerHTML = loadingPanel();
  await refreshList();
}, closeThread);

onAction('click', {
  'delete-session': el => deleteSession(el.dataset.thread),
  'delete-all-sessions': () => deleteAllSessions(),
  'open-thread': el => openThread(el.dataset.thread),
  'close-thread': () => {
    closeThread();
    renderList();
  }
});
