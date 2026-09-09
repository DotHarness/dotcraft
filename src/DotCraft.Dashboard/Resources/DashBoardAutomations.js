var dashboardAutomations = (() => {
  const statusLabels = { active: 'Active', paused: 'Paused', completed: 'Completed' };
  const statusClasses = { active: 'badge-success', paused: 'badge-warning', completed: 'badge-info' };
  const escape = value => String(value ?? '').replace(/[&<>"']/g, char => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
  }[char]));
  const date = value => {
    if (!value) return '-';
    const parsed = new Date(value);
    return Number.isNaN(parsed.getTime()) ? '-' : parsed.toLocaleString();
  };
  function snapshot(data) {
    const automations = Array.isArray(data.automations) ? data.automations : [];
    const counts = Object.entries(data.countsByStatus || {}).filter(([, count]) => Number.isFinite(count));
    const statsHtml = counts.length ? counts.map(([status, count]) =>
      `<div class="stat-card"><div class="stat-label">${escape(statusLabels[status] || status)}</div><div class="stat-value accent">${count}</div></div>`
    ).join('') : '<div class="stat-card"><div class="stat-label">Automations</div><div class="stat-value accent">0</div></div>';
    const generatedLabel = data.generatedAt ? 'Data as of ' + date(data.generatedAt) : '';
    if (!automations.length) return { statsHtml, generatedLabel, tableHtml: '<div class="empty-state">No automations.</div>' };
    const rows = automations.map(automation => {
      const name = automation.name || automation.id || '-';
      const status = statusLabels[automation.status] || automation.status || '-';
      const execution = automation.executionMode === 'thread'
        ? (automation.targetThreadId
          ? `<a href="#" class="text-accent" data-automation-thread="${escape(automation.targetThreadId)}">Continue conversation</a>`
          : 'Conversation unavailable')
        : 'Independent run';
      const nextRun = automation.status === 'active' ? date(automation.nextRunAt) : '-';
      return `<tr><td title="${escape(name)}">${escape(name)}</td>` +
        `<td><span class="badge ${statusClasses[automation.status] || 'badge-info'}">${escape(status)}</span></td>` +
        `<td>${execution}</td><td>${escape(nextRun)}</td><td>${escape(date(automation.updatedAt || automation.createdAt))}</td></tr>`;
    }).join('');
    return { statsHtml, generatedLabel, tableHtml: '<table class="config-table"><thead><tr>' +
      '<th>Name</th><th>Status</th><th>Execution</th><th>Next run</th><th>Updated</th>' +
      '</tr></thead><tbody>' + rows + '</tbody></table>' };
  }
  return { snapshot };
})();

function renderAutomations(data) {
  _automationsLastData = data;
  const rendered = dashboardAutomations.snapshot(data);
  const stats = document.getElementById('automationsStats');
  const generated = document.getElementById('automationsGeneratedAt');
  const table = document.getElementById('automationsTable');
  if (stats) stats.innerHTML = rendered.statsHtml;
  if (generated) generated.textContent = rendered.generatedLabel;
  if (table) {
    table.innerHTML = rendered.tableHtml;
    table.onclick = event => {
      const link = event.target.closest?.('[data-automation-thread]');
      if (!link) return;
      event.preventDefault();
      openSession(link.dataset.automationThread);
    };
  }
}

/** Enables or disables the Automations nav tab based on /orchestrators/automations/state (must run after applySetupModeUi resets tab display). */
function applyAutomationsNavState() {
  const tab = document.getElementById('navTabAutomations');
  if (!tab) return;
  tab.disabled = !automationsAvailable;
  tab.title = automationsAvailable
    ? ''
    : 'Automations requires AppServer with the Automations module enabled.';
  if (!automationsAvailable && currentPanel === 'automations')
    switchPanel('dashboard');
}

async function probeAutomations() {
  if (!dashboardCapability('automations')) {
    automationsAvailable = false;
    applyAutomationsNavState();
    return;
  }
  try {
    const res = await fetch('/dashboard/api/orchestrators/automations/state');
    automationsAvailable = res.ok;
  } catch (_) {
    automationsAvailable = false;
  }
  applyAutomationsNavState();
}

async function refreshAutomations() {
  if (!automationsAvailable) return;
  try {
    const res = await fetch('/dashboard/api/orchestrators/automations/state');
    if (!res.ok) return;
    const data = await res.json();
    renderAutomations(data);
  } catch (e) {
    console.error('Automations refresh failed:', e);
  }
}

async function refreshAutomationsPanel() {
  if (!dashboardCapability('automations')) return;
  if (!automationsAvailable) return;
  try {
    await fetch('/dashboard/api/orchestrators/automations/refresh', { method: 'POST' });
  } catch (e) {
    console.error('Automations poll trigger failed:', e);
  }
  await refreshAutomations();
}

