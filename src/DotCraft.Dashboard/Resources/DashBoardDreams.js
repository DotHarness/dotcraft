let dreamsAvailable = false;
let dreamsStatus = null;
let dreamsRuns = [];
let selectedDreamRunId = null;
let selectedDreamRunDetails = null;
let dreamsFilter = 'all';
let dreamsListLoading = false;
let dreamsDetailLoading = false;
let dreamsDeleteBusy = null;

const DREAMS_ACTION_DONE = {
  apply: 'Changes applied to memory.',
  restore: 'Memory restored to how it was before this run.',
  discard: 'Run discarded. Memory is unchanged.',
  archive: 'Run archived.',
  cancel: 'Cancel requested.'
};

const DREAMS_CONFLICT_MESSAGES = {
  apply: 'Memory changed after this run started, so its result was not applied. Run Dreams again to consolidate the current memory.',
  restore: 'Memory changed after this run was applied, so it no longer matches the run\'s result and was not restored.'
};

function dreamState(run) {
  if (run?.status === 'succeeded') return run.reviewStatus || 'succeeded';
  return run?.status || 'unknown';
}

function dreamBadgeClass(value) {
  if (value === 'succeeded' || value === 'applied' || value === 'added') return 'badge-success';
  if (value === 'running' || value === 'pending' || value === 'updated') return 'badge-info';
  if (value === 'failed' || value === 'discarded' || value === 'deleted') return 'badge-warning';
  if (value === 'canceled' || value === 'archived') return 'badge-accent';
  return 'badge-info';
}

function dreamTriggerLabel(run) {
  const t = String(run?.trigger || '').toLowerCase();
  if (t === 'manual') return 'Manual';
  if (t === 'scheduled') return 'Scheduled';
  return t ? t.charAt(0).toUpperCase() + t.slice(1) : '';
}

function dreamFriendlyLabel(run) {
  const trig = dreamTriggerLabel(run);
  const prefix = trig ? `${trig} run` : 'Dream run';
  const ts = run?.startedAt || run?.endedAt;
  if (!ts) return prefix;
  const d = new Date(ts);
  if (isNaN(d.getTime())) return prefix;
  const when = d.toLocaleString(undefined, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit', hour12: false });
  return `${prefix} · ${when}`;
}

function dreamStoreLabel(run) {
  return run?.scope ? 'Agent memory' : 'Workspace memory';
}

function dreamChangeSummary(run) {
  if (run?.status !== 'succeeded') return '';
  const changes = run.changes || {};
  const parts = [];
  if (changes.added?.length) parts.push(`${changes.added.length} added`);
  if (changes.updated?.length) parts.push(`${changes.updated.length} updated`);
  if (changes.deleted?.length) parts.push(`${changes.deleted.length} removed`);
  if (parts.length) return parts.join(' · ');
  return changes.indexChanged ? 'Index updated' : 'No changes';
}

function dreamDurationText(run) {
  if (!run?.startedAt || !run?.endedAt) return null;
  const ms = new Date(run.endedAt).getTime() - new Date(run.startedAt).getTime();
  if (!isFinite(ms) || ms < 0) return null;
  if (ms < 1000) return ms + 'ms';
  const s = ms / 1000;
  if (s < 60) return s.toFixed(1) + 's';
  const m = Math.floor(s / 60);
  const rem = Math.round(s % 60);
  return `${m}m ${rem}s`;
}

function fmtTokens(n) {
  if (n == null) return '-';
  if (n >= 1000000) return (n / 1000000).toFixed(1) + 'M';
  if (n >= 1000) return (n / 1000).toFixed(1) + 'K';
  return String(n);
}

function dreamUsageTotal(usage) {
  if (!usage) return null;
  const total = usage.totalTokens || 0;
  return total ? fmtTokens(total) : null;
}

function truncateMiddle(text, max = 30) {
  const s = String(text || '');
  if (s.length <= max) return s;
  const head = Math.ceil((max - 1) / 2);
  const tail = Math.floor((max - 1) / 2);
  return s.slice(0, head) + '…' + s.slice(s.length - tail);
}

function dreamCopyBtn(value) {
  if (!value) return '';
  return `<button class="dreams-copy" title="Copy" aria-label="Copy" onclick="event.stopPropagation();dreamCopy('${encodeURIComponent(value)}')">${iconSvg('copy', 13)}</button>`;
}

function dreamCopy(enc) {
  const value = decodeURIComponent(enc);
  const done = () => showToast('Copied to clipboard.', 'success', 1600);
  if (navigator.clipboard && navigator.clipboard.writeText) {
    navigator.clipboard.writeText(value).then(done).catch(() => showToast('Copy failed.', 'error'));
    return;
  }
  const ta = document.createElement('textarea');
  ta.value = value;
  document.body.appendChild(ta);
  ta.select();
  try { document.execCommand('copy'); done(); } catch { showToast('Copy failed.', 'error'); }
  ta.remove();
}

// Shared line-based diff so the +N/-M count and the rendered lines always agree.
// Presentation-focused: matches on line content (removed block first, then the new
// document with additions highlighted); it does not attempt full LCS alignment.
function computeLineDiff(oldText, newText) {
  const split = (t) => (String(t || '').trim() ? String(t).split(/\r?\n/) : []);
  const oldLines = split(oldText);
  const newLines = split(newText);
  const countOf = (arr) => {
    const m = new Map();
    for (const l of arr) m.set(l, (m.get(l) || 0) + 1);
    return m;
  };
  const rows = [];
  let additions = 0;
  let removals = 0;
  const newRemaining = countOf(newLines);
  for (const line of oldLines) {
    if ((newRemaining.get(line) || 0) > 0) {
      newRemaining.set(line, newRemaining.get(line) - 1);
    } else {
      rows.push({ type: 'del', text: line });
      removals++;
    }
  }
  const oldRemaining = countOf(oldLines);
  for (const line of newLines) {
    if ((oldRemaining.get(line) || 0) > 0) {
      oldRemaining.set(line, oldRemaining.get(line) - 1);
      rows.push({ type: 'ctx', text: line });
    } else {
      rows.push({ type: 'add', text: line });
      additions++;
    }
  }
  return { rows, additions, removals };
}

function renderLineDiff(diff) {
  if (!diff.rows.length || (diff.additions === 0 && diff.removals === 0)) {
    return '<span style="color:var(--text-muted)">No line changes.</span>';
  }
  return diff.rows.map(r => {
    const marker = r.type === 'add' ? '+' : r.type === 'del' ? '-' : ' ';
    return `<span class="dreams-diff-line ${r.type}">${escapeHtml(marker + r.text)}</span>`;
  }).join('');
}

function renderDreamFile(file, open) {
  const diff = computeLineDiff(file.baseline, file.candidate);
  const change = file.change || 'updated';
  return `
    <details class="dreams-file"${open ? ' open' : ''}>
      <summary>
        <span class="dreams-file-name">${escapeHtml(file.name || '')}</span>
        <span class="badge ${dreamBadgeClass(change)}">${escapeHtml(change)}</span>
        <span class="count"><span class="add">+${diff.additions}</span> <span class="del">-${diff.removals}</span></span>
      </summary>
      <pre class="dreams-code">${renderLineDiff(diff)}</pre>
    </details>`;
}

async function probeDreams() {
  if (!dashboardCapability('dreams')) {
    dreamsAvailable = false;
    applyDreamsNavState();
    return;
  }
  try {
    const res = await fetch('/dashboard/api/dreams/status');
    dreamsAvailable = res.ok;
    if (res.ok) dreamsStatus = await res.json();
  } catch {
    dreamsAvailable = false;
  }
  applyDreamsNavState();
}

function applyDreamsNavState() {
  const tab = document.getElementById('navTabDreams');
  if (!tab) return;
  tab.disabled = !dreamsAvailable;
  tab.title = dreamsAvailable ? 'Review Dreams runs and memory changes.' : 'Dreams endpoints are not available for this workspace.';
  if (!dreamsAvailable && currentPanel === 'dreams') switchPanel('dashboard');
}

// Relative "in 3h" text for the next scheduled eligibility check.
function nextRunText(iso) {
  if (!iso) return 'Not scheduled';
  const d = new Date(iso).getTime();
  if (isNaN(d)) return 'Not scheduled';
  const diff = d - Date.now();
  if (diff <= 0) return 'Due now';
  const mins = Math.round(diff / 60000);
  if (mins < 60) return 'in ' + mins + 'm';
  const hrs = Math.floor(mins / 60);
  if (hrs < 24) return 'in ' + hrs + 'h' + (mins % 60 ? ' ' + (mins % 60) + 'm' : '');
  const days = Math.floor(hrs / 24);
  return 'in ' + days + 'd' + (hrs % 24 ? ' ' + (hrs % 24) + 'h' : '');
}

// Compact interval label from a TimeSpan string ([d.]hh:mm:ss or hh:mm:ss with hours >= 24).
function formatInterval(s) {
  if (!s) return '';
  const m = /^(?:(\d+)\.)?(\d{2,}):(\d{2}):(\d{2})/.exec(String(s));
  if (!m) return String(s);
  const totalHours = parseInt(m[1] || '0', 10) * 24 + parseInt(m[2], 10);
  const days = Math.floor(totalHours / 24);
  const hrs = totalHours % 24;
  const mins = parseInt(m[3], 10);
  const parts = [];
  if (days) parts.push(days + 'd');
  if (hrs) parts.push(hrs + 'h');
  if (mins) parts.push(mins + 'm');
  return parts.length ? parts.join(' ') : '0m';
}

function updateDreamsRunNowBtn() {
  const btn = document.getElementById('dreamsRunNowBtn');
  if (!btn) return;
  const running = !!dreamsStatus?.running;
  const off = dreamsStatus?.enabled === false;
  btn.disabled = running || off;
  btn.title = running
    ? 'A Dreams run is already in progress.'
    : off ? 'Dreams is disabled for this workspace.' : 'Consolidate every memory store now.';
}

function renderDreamsStats() {
  const statsEl = document.getElementById('dreamsStats');
  if (!statsEl) return;
  const st = dreamsStatus || {};
  const lastRun = st.lastRun;
  let stateDot = 'off';
  let stateLabel = 'Disabled';
  if (st.running) { stateDot = 'running'; stateLabel = 'Running'; }
  else if (st.enabled) { stateDot = 'on'; stateLabel = 'Enabled'; }
  const pending = st.pendingCount || 0;
  const lastRunTs = lastRun?.endedAt || lastRun?.startedAt;
  const lastRunState = lastRun ? dreamState(lastRun) : null;
  const lastRunHtml = lastRun
    ? `<span class="badge ${dreamBadgeClass(lastRunState)}">${escapeHtml(lastRunState)}</span>`
    : '<span style="font-size:16px;color:var(--text-muted)">Never</span>';
  statsEl.innerHTML = `
    <div class="stat-card">
      <div class="stat-label">State</div>
      <div class="stat-value" style="font-size:17px"><span class="dreams-state-dot ${stateDot}"></span>${stateLabel}</div>
    </div>
    <div class="stat-card">
      <div class="stat-label">Pending</div>
      <div class="stat-value ${pending > 0 ? 'warning' : ''}">${formatNumber(pending)}</div>
      <div class="stat-sub">${pending === 1 ? 'run waits' : 'runs wait'} for review</div>
    </div>
    <div class="stat-card">
      <div class="stat-label">Auto Apply</div>
      <div class="stat-value ${st.autoApply ? 'success' : ''}">${st.autoApply ? 'On' : 'Off'}</div>
    </div>
    <div class="stat-card">
      <div class="stat-label">Next Run</div>
      <div class="stat-value" style="font-size:17px">${escapeHtml(st.enabled ? nextRunText(st.nextRunAt) : 'Not scheduled')}</div>
      ${st.interval ? `<div class="stat-sub">every ${escapeHtml(formatInterval(st.interval))}</div>` : ''}
    </div>
    <div class="stat-card">
      <div class="stat-label">Last Run</div>
      <div class="stat-value" style="font-size:17px">${lastRunHtml}</div>
      ${lastRunTs ? `<div class="stat-sub">${escapeHtml(formatRelative(lastRunTs))}</div>` : ''}
    </div>
  `;
  updateDreamsRunNowBtn();
}

function dreamsAnyRunRunning() {
  return !!dreamsStatus?.running || dreamsRuns.some(run => run.status === 'running');
}

function updateDreamsDeleteAllBtn() {
  const btn = document.getElementById('dreamsDeleteAllBtn');
  if (!btn) return;
  const running = dreamsAnyRunRunning();
  const busy = dreamsDeleteBusy !== null;
  btn.disabled = !dreamsRuns.length || running || busy;
  btn.textContent = dreamsDeleteBusy === 'all' ? 'Deleting…' : 'Delete all';
  btn.title = running
    ? 'Dream runs cannot be deleted while any run is in progress.'
    : !dreamsRuns.length ? 'There are no Dream runs to delete.'
    : busy ? 'A Dreams deletion is already in progress.'
    : 'Permanently delete every Dream run and its trace. Memory is not changed.';
}

const DREAMS_FILTERS = {
  pending: r => r.reviewStatus === 'pending',
  applied: r => r.reviewStatus === 'applied',
  archived: r => r.reviewStatus === 'archived',
  other: r => r.reviewStatus !== 'pending' && r.reviewStatus !== 'applied' && r.reviewStatus !== 'archived'
};

function filteredDreamRuns() {
  const filter = DREAMS_FILTERS[dreamsFilter];
  return filter ? dreamsRuns.filter(filter) : dreamsRuns;
}

function setDreamsFilter(id) {
  dreamsFilter = id;
  renderDreamsFilter();
  renderDreamsRunList();
}

function renderDreamsFilter() {
  const el = document.getElementById('dreamsFilter');
  if (!el) return;
  if (dreamsListLoading || !dreamsRuns.length) { el.innerHTML = ''; return; }
  const count = (id) => id === 'all' ? dreamsRuns.length : dreamsRuns.filter(DREAMS_FILTERS[id]).length;
  const chip = (id, label) => `<button class="dreams-chip ${dreamsFilter === id ? 'active' : ''}" onclick="setDreamsFilter('${id}')">${label}<span class="dreams-chip-count">${count(id)}</span></button>`;
  el.innerHTML = chip('all', 'All') + chip('pending', 'Pending') + chip('applied', 'Applied') + chip('archived', 'Archived') + chip('other', 'Other');
}

function dreamsRunSkeleton(n = 3) {
  let out = '';
  for (let i = 0; i < n; i++) {
    out += `<div class="dreams-skel-row">
      <div class="skeleton skeleton-text" style="width:42%"></div>
      <div class="skeleton skeleton-text" style="width:72%"></div>
      <div class="skeleton skeleton-text" style="width:56%;margin-bottom:0"></div>
    </div>`;
  }
  return out;
}

function renderDreamsRunList() {
  const listEl = document.getElementById('dreamsRunList');
  if (!listEl) return;
  renderDreamsFilter();
  updateDreamsDeleteAllBtn();
  if (dreamsListLoading) { listEl.innerHTML = dreamsRunSkeleton(); return; }
  if (!dreamsRuns.length) {
    listEl.innerHTML = '<div class="empty-state"><p>No Dreams runs yet.</p></div>';
    return;
  }
  const runs = filteredDreamRuns();
  if (!runs.length) {
    listEl.innerHTML = '<div class="empty-state"><p>No runs match this filter.</p></div>';
    return;
  }
  listEl.innerHTML = runs.map(run => {
    const selected = run.id === selectedDreamRunId;
    const runTime = run.endedAt || run.startedAt;
    const enc = encodeURIComponent(run.id);
    const badges = [`<span class="badge ${dreamBadgeClass(run.status)}">${escapeHtml(run.status)}</span>`];
    if (run.reviewStatus) badges.push(`<span class="badge ${dreamBadgeClass(run.reviewStatus)}">${escapeHtml(run.reviewStatus)}</span>`);
    if (run.autoApplied) badges.push('<span class="badge badge-success">auto</span>');
    const pendingDot = run.reviewStatus === 'pending' ? '<span class="dreams-pending-dot" title="Needs review"></span>' : '';
    const metrics = [dreamStoreLabel(run), dreamChangeSummary(run)].filter(Boolean);
    const trace = run.threadId
      ? `<button class="dreams-link" onclick="event.stopPropagation();openDreamTrace('${encodeURIComponent(run.threadId)}')">Trace</button>`
      : '';
    return `
      <article class="dreams-run-card ${selected ? 'active' : ''}" role="button" tabindex="0" aria-pressed="${selected}"
        onclick="selectDreamRun('${enc}')"
        onkeydown="if(event.target===this&&(event.key==='Enter'||event.key===' ')){event.preventDefault();selectDreamRun('${enc}');}">
        <div class="dreams-run-header">
          <div class="dreams-run-badges">${badges.join('')}</div>
          <div class="dreams-run-time">${runTime ? escapeHtml(formatRelative(runTime)) : 'Unknown'}</div>
        </div>
        <div class="dreams-run-title">${pendingDot}${escapeHtml(dreamFriendlyLabel(run))}</div>
        <div class="dreams-run-id" title="${escapeHtml(run.id)}">${escapeHtml(run.id)}</div>
        <div class="dreams-run-metrics">${metrics.map(m => `<span>${escapeHtml(m)}</span>`).join('')}${trace}</div>
      </article>`;
  }).join('');
}

function dreamMetric(label, valueHtml, extraClass) {
  return `<div><div class="dreams-metric-label">${escapeHtml(label)}</div><div class="dreams-metric-value${extraClass ? ' ' + extraClass : ''}">${valueHtml}</div></div>`;
}

// Detail head and body live in separate grid cells (see .dreams-layout) so the
// detail header aligns with the run-list header and both bodies start at the same Y.
function setDreamsDetail(headHtml, bodyHtml) {
  const head = document.getElementById('dreamsDetailHead');
  const body = document.getElementById('dreamsRunDetail');
  if (head) head.innerHTML = headHtml;
  if (body) body.innerHTML = bodyHtml;
}

function dreamsDetailHeadSkeleton() {
  return `<div class="skeleton skeleton-text" style="width:60%;height:18px;margin-bottom:6px"></div>
    <div class="skeleton skeleton-text" style="width:82%;height:12px;margin-bottom:0"></div>`;
}

function dreamsDetailBodySkeleton() {
  return `
    <div class="dreams-metrics">
      ${Array.from({ length: 6 }).map(() => `<div><div class="skeleton skeleton-text" style="width:60%;height:11px"></div><div class="skeleton skeleton-text" style="width:80%;height:18px;margin-bottom:0"></div></div>`).join('')}
    </div>
    <div class="skeleton skeleton-text" style="width:100%;height:120px;margin-bottom:0"></div>`;
}

function renderDreamsRunDetail() {
  const detailEl = document.getElementById('dreamsRunDetail');
  if (!detailEl) return;
  if (dreamsDetailLoading) { setDreamsDetail(dreamsDetailHeadSkeleton(), dreamsDetailBodySkeleton()); return; }
  const data = selectedDreamRunDetails?.run?.id === selectedDreamRunId ? selectedDreamRunDetails : null;
  const run = data?.run || dreamsRuns.find(r => r.id === selectedDreamRunId);
  if (!run) {
    setDreamsDetail('', '<div class="empty-state"><p>Select a Dreams run to review.</p></div>');
    return;
  }
  const files = Array.isArray(data?.files) ? data.files : [];
  const running = run.status === 'running';
  const pending = run.reviewStatus === 'pending';
  const applied = run.reviewStatus === 'applied';
  const canArchive = !running && run.reviewStatus !== 'archived';
  const deleteDisabled = dreamsAnyRunRunning() || dreamsDeleteBusy !== null;
  const enc = encodeURIComponent(run.id);

  const actions = [];
  if (run.threadId) actions.push(`<button class="btn" onclick="openDreamTrace('${encodeURIComponent(run.threadId)}')">Open trace</button>`);
  if (running) actions.push(`<button class="btn" onclick="dreamAction('${enc}','cancel')">Cancel run</button>`);
  if (pending) actions.push(`<button class="btn" onclick="dreamAction('${enc}','discard')" title="Leave memory unchanged and close this run.">Discard</button>`);
  if (applied) actions.push(`<button class="btn" onclick="confirmRestoreDreamRun('${enc}')" title="Put memory back the way it was before this run.">Restore</button>`);
  if (canArchive) actions.push(`<button class="btn" onclick="dreamAction('${enc}','archive')">Archive</button>`);
  actions.push(`<button class="btn btn-danger" onclick="confirmDeleteDreamRun('${enc}')" ${deleteDisabled ? 'disabled' : ''} title="${dreamsAnyRunRunning() ? 'Dream runs cannot be deleted while any run is in progress.' : dreamsDeleteBusy !== null ? 'A Dreams deletion is already in progress.' : 'Permanently delete this Dream run and its trace. Memory is not changed.'}">${dreamsDeleteBusy !== null ? 'Deleting…' : 'Delete'}</button>`);
  if (pending) actions.push(`<button class="btn btn-primary" onclick="dreamAction('${enc}','apply')" title="Replace this memory with the run's result.">Apply</button>`);

  const badges = [`<span class="badge ${dreamBadgeClass(run.status)}">${escapeHtml(run.status)}</span>`];
  if (run.reviewStatus) badges.push(`<span class="badge ${dreamBadgeClass(run.reviewStatus)}">${escapeHtml(run.reviewStatus)}</span>`);
  if (run.autoApplied) badges.push('<span class="badge badge-success">auto-applied</span>');

  const runTime = run.endedAt || run.startedAt;
  const duration = dreamDurationText(run);
  const usage = dreamUsageTotal(run.usage);
  const changeSummary = dreamChangeSummary(run);

  const metrics = [];
  metrics.push(dreamMetric('Store', `${escapeHtml(dreamStoreLabel(run))}${run.scope
    ? `<div class="dreams-scope"><span title="${escapeHtml(run.scope)}">${escapeHtml(truncateMiddle(run.scope, 24))}</span>${dreamCopyBtn(run.scope)}</div>`
    : ''}`, 'compact'));
  metrics.push(dreamMetric('Changes', changeSummary ? escapeHtml(changeSummary) : `<span class="muted">${running ? 'None yet' : 'None'}</span>`, 'compact'));
  metrics.push(dreamMetric('Threads', formatNumber(run.candidateThreadCount || 0)));
  metrics.push(dreamMetric('Evidence', `${formatNumber(run.evidenceReadCount || 0)} reads · ${formatNumber(run.evidenceSearchCount || 0)} searches`, 'compact'));
  if (duration) metrics.push(dreamMetric('Duration', escapeHtml(duration)));
  if (usage) metrics.push(dreamMetric('Tokens', escapeHtml(usage)));

  const banner = (run.errorType || run.message)
    ? `<div class="message ${run.status === 'failed' ? 'message-error' : 'message-warning'}" style="margin-bottom:14px">${escapeHtml([run.errorType, run.message].filter(Boolean).join(': '))}</div>`
    : '';

  const filesHtml = files.length
    ? files.map(file => renderDreamFile(file, files.length <= 3)).join('')
    : `<div class="dreams-kv" style="color:var(--text-muted);margin-top:8px">${running ? 'The run is still working.' : 'No memory files changed.'}</div>`;
  const evidenceLinks = (run.evidenceThreadIds || []).length
    ? run.evidenceThreadIds.map(id => `<li><button class="dreams-link" onclick="openSession('${encodeURIComponent(id)}')">${escapeHtml(id)}</button></li>`).join('')
    : '<li style="color:var(--text-muted)">No evidence threads recorded.</li>';
  const usageKv = run.usage
    ? `<div class="dreams-kv"><span class="k">Input:</span> ${formatNumber(run.usage.inputTokens || 0)} · <span class="k">Output:</span> ${formatNumber(run.usage.outputTokens || 0)} · <span class="k">Cached:</span> ${formatNumber(run.usage.cachedInputTokens || 0)} · <span class="k">Total:</span> ${formatNumber(run.usage.totalTokens || 0)}</div>`
    : '<div class="dreams-kv" style="color:var(--text-muted)">No token usage recorded.</div>';

  const headHtml = `
    <div class="dreams-detail-head">
      <div style="min-width:0">
        <div class="dreams-detail-title">${escapeHtml(dreamFriendlyLabel(run))}${badges.join('')}</div>
        <div class="dreams-detail-sub">
          <span>${escapeHtml(truncateMiddle(run.id, 40))}</span>${dreamCopyBtn(run.id)}
          ${runTime ? `· ${escapeHtml(formatRelative(runTime))}` : ''}${duration ? ` · ${escapeHtml(duration)}` : ''}
        </div>
      </div>
      <div class="dreams-actions">${actions.join('')}</div>
    </div>`;

  const bodyHtml = `
    ${banner}
    <div class="dreams-metrics">${metrics.join('')}</div>
    <details class="dreams-details" open>
      <summary>Summary</summary>
      <div class="dreams-summary">${run.summary ? escapeHtml(run.summary) : '<span style="color:var(--text-muted)">No summary recorded.</span>'}</div>
    </details>
    <details class="dreams-details" open>
      <summary>Changes <span class="count">${files.length} ${files.length === 1 ? 'file' : 'files'}</span></summary>
      ${filesHtml}
    </details>
    <details class="dreams-details">
      <summary>Input manifest</summary>
      ${run.inputManifestPath ? `<div class="dreams-kv" style="margin-top:8px;font-family:var(--font-mono)">${escapeHtml(run.inputManifestPath)}</div>` : ''}
      <pre class="dreams-code">${escapeHtml(data?.manifest || 'No manifest recorded.')}</pre>
    </details>
    <details class="dreams-details">
      <summary>Evidence &amp; trace</summary>
      <div style="margin-top:10px">
        <div class="dreams-metric-label">Evidence threads <span style="text-transform:none;color:var(--text-muted)">(${(run.evidenceThreadIds || []).length})</span></div>
        <ul class="dreams-evidence-list">${evidenceLinks}</ul>
        <div class="dreams-metric-label" style="margin-top:12px">Token usage</div>
        ${usageKv}
        <div class="dreams-metric-label" style="margin-top:12px">Thread / turn</div>
        <div class="dreams-kv" style="font-family:var(--font-mono)">${escapeHtml([run.threadId, run.turnId].filter(Boolean).join(' / ') || 'none')}</div>
      </div>
    </details>`;

  setDreamsDetail(headHtml, bodyHtml);
}

async function refreshDreams() {
  if (!dreamsAvailable) return;
  dreamsListLoading = !dreamsRuns.length;
  if (dreamsListLoading) renderDreamsRunList();
  dreamsStatus = await fetchJson('/dreams/status');
  const list = await fetchJson('/dreams/runs');
  dreamsRuns = Array.isArray(list?.runs) ? list.runs : [];
  dreamsListLoading = false;
  if (selectedDreamRunId && !dreamsRuns.some(run => run.id === selectedDreamRunId)) {
    selectedDreamRunId = null;
    selectedDreamRunDetails = null;
  }
  if (!selectedDreamRunId && dreamsRuns.length) {
    selectedDreamRunId = dreamsRuns.find(run => run.reviewStatus === 'pending')?.id || dreamsRuns[0].id;
  }
  renderDreamsStats();
  renderDreamsRunList();
  if (selectedDreamRunId) await loadDreamRun(selectedDreamRunId);
  else renderDreamsRunDetail();
}

async function loadDreamRun(runId) {
  dreamsDetailLoading = true;
  renderDreamsRunDetail();
  const data = await fetchJson('/dreams/runs/' + encodeURIComponent(runId));
  dreamsDetailLoading = false;
  if (data) {
    selectedDreamRunDetails = data;
    selectedDreamRunId = data.run?.id || runId;
  }
  renderDreamsStats();
  renderDreamsRunList();
  renderDreamsRunDetail();
}

function selectDreamRun(encodedRunId) {
  const runId = decodeURIComponent(encodedRunId);
  selectedDreamRunId = runId;
  history.replaceState(null, '', '#dreams/run/' + encodeURIComponent(runId));
  loadDreamRun(runId);
}

async function runDreamsNow() {
  if (!dashboardCapability('dreams')) return;
  if (!dreamsAvailable) return;
  if (dreamsStatus?.running) {
    showToast('A Dreams run is already in progress.', 'info');
    return;
  }
  try {
    const result = await postJson('/dreams/run');
    showToast(result?.run ? 'Dreams run requested.' : 'Dreams is disabled for this workspace.', result?.run ? 'success' : 'info');
    await refreshDreams();
  } catch (error) {
    showToast(error.message || 'Dreams run failed.', 'error');
  }
}

async function dreamAction(encodedRunId, action) {
  if (!dashboardCapability('dreams')) return;
  const runId = decodeURIComponent(encodedRunId);
  try {
    await postJson('/dreams/runs/' + encodeURIComponent(runId) + '/' + action);
    showToast(DREAMS_ACTION_DONE[action] || 'Done.', 'success');
  } catch (error) {
    if (error.status === 409 && DREAMS_CONFLICT_MESSAGES[action]) showToast(DREAMS_CONFLICT_MESSAGES[action], 'error', 10000);
    else showToast(error.message || 'Dreams action failed.', 'error');
  }
  await refreshDreams();
}

function confirmRestoreDreamRun(encodedRunId) {
  if (!dashboardCapability('dreams')) return;
  showConfirmModal(
    'Restore memory',
    'Put this memory back the way it was before the run? The run\'s changes are removed from memory and the run is marked discarded.',
    () => dreamAction(encodedRunId, 'restore'),
    'btn-primary',
    'Restore'
  );
}

function syncDreamsSelectionHash() {
  const nextHash = selectedDreamRunId
    ? '#dreams/run/' + encodeURIComponent(selectedDreamRunId)
    : '#dreams';
  history.replaceState(null, '', nextHash);
}

async function deleteDreamsJson(url) {
  const res = await fetch(API + url, { method: 'DELETE', credentials: 'same-origin' });
  const body = await res.json().catch(() => null);
  if (res.status === 401) throw new Error('Not authorized — sign in to the dashboard first.');
  if (!res.ok) throw new Error(body?.error || `Delete failed (HTTP ${res.status})`);
  return body || {};
}

function showDreamsDeleteToasts(result, successMessage) {
  showToast(successMessage, 'success');
  const failures = Array.isArray(result?.traceCleanupFailures) ? result.traceCleanupFailures : [];
  for (const failure of failures) {
    const target = failure?.threadId ? ` for thread ${failure.threadId}` : '';
    const detail = failure?.error ? `: ${failure.error}` : '.';
    showToast(`Run deleted, but its trace could not be removed${target}${detail}`, 'warning', 10000);
  }
  if (result?.partial && !failures.length) {
    showToast('Runs were deleted, but some traces could not be removed.', 'warning', 10000);
  }
}

function clearDeletedDreamSelection(result) {
  const deleted = Array.isArray(result?.deletedRunIds) ? result.deletedRunIds : [];
  if (selectedDreamRunId && deleted.includes(selectedDreamRunId)) {
    selectedDreamRunId = null;
    selectedDreamRunDetails = null;
  }
}

function confirmDeleteDreamRun(encodedRunId) {
  if (!dashboardCapability('dreams')) return;
  const runId = decodeURIComponent(encodedRunId);
  const run = dreamsRuns.find(item => item.id === runId);
  if (!run) {
    showToast('Dream run not found. Refreshing history.', 'warning');
    refreshDreams();
    return;
  }
  if (dreamsAnyRunRunning()) {
    showToast('Dream runs cannot be deleted while any run is in progress.', 'info');
    return;
  }
  if (dreamsDeleteBusy !== null) return;
  showConfirmModal(
    'Delete Dream run',
    `Permanently delete "${run.id}" with its run data, internal thread, and trace? Memory is not changed. This cannot be undone.`,
    () => deleteDreamRun(run.id),
    'btn-danger',
    'Delete'
  );
}

async function deleteDreamRun(runId) {
  if (dreamsAnyRunRunning()) {
    showToast('Dream runs cannot be deleted while any run is in progress.', 'info');
    return;
  }
  dreamsDeleteBusy = runId;
  renderDreamsRunList();
  renderDreamsRunDetail();
  try {
    const result = await deleteDreamsJson('/dreams/runs/' + encodeURIComponent(runId));
    clearDeletedDreamSelection(result);
    await refreshDreams();
    syncDreamsSelectionHash();
    showDreamsDeleteToasts(result, 'Dream run permanently deleted.');
  } catch (error) {
    showToast(error.message || 'Dream run deletion failed.', 'error');
  } finally {
    dreamsDeleteBusy = null;
    renderDreamsRunList();
    renderDreamsRunDetail();
  }
}

function confirmDeleteAllDreamRuns() {
  if (!dashboardCapability('dreams')) return;
  if (!dreamsRuns.length) return;
  if (dreamsAnyRunRunning()) {
    showToast('Dream runs cannot be deleted while any run is in progress.', 'info');
    return;
  }
  if (dreamsDeleteBusy !== null) return;
  showConfirmModal(
    'Delete all Dream runs',
    `Permanently delete all ${dreamsRuns.length} Dream runs with their run data, internal threads, and traces? Memory is not changed. This cannot be undone.`,
    deleteAllDreamRuns,
    'btn-danger',
    'Delete all'
  );
}

async function deleteAllDreamRuns() {
  if (dreamsAnyRunRunning()) {
    showToast('Dream runs cannot be deleted while any run is in progress.', 'info');
    return;
  }
  dreamsDeleteBusy = 'all';
  renderDreamsRunList();
  renderDreamsRunDetail();
  try {
    const result = await deleteDreamsJson('/dreams/runs');
    clearDeletedDreamSelection(result);
    await refreshDreams();
    syncDreamsSelectionHash();
    const count = result?.deletedCount ?? 0;
    showDreamsDeleteToasts(result, `Deleted ${count} Dream ${count === 1 ? 'run' : 'runs'}.`);
  } catch (error) {
    showToast(error.message || 'Dream run deletion failed.', 'error');
  } finally {
    dreamsDeleteBusy = null;
    renderDreamsRunList();
    renderDreamsRunDetail();
  }
}

function openDreamTrace(encodedThreadId) {
  openSession(decodeURIComponent(encodedThreadId));
}

function handleDashboardHash() {
  const hash = window.location.hash || '';
  const match = /^#dreams\/run\/(.+)$/.exec(hash);
  if (hash !== '#dreams' && !match) return;
  if (match) selectedDreamRunId = decodeURIComponent(match[1]);
  if (currentPanel !== 'dreams') switchPanel('dreams');
  else if (match) loadDreamRun(selectedDreamRunId);
}
