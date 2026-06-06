/*
 * Oratorio board desktop extension (DotCraft Desktop mainView).
 *
 * Reads the board over the app's published loopback surface endpoint with
 * host.network.getJson, and performs a small set of writes with
 * host.network.postJson (gated by the extension's surfaceWriteScopes; see
 * specs/extensions/plugin-architecture.md). Heavier work — reject, draft
 * publishing, comments, full review, settings — stays in Oratorio via handoff.
 *
 * Surface endpoint contract (served by Oratorio over the loopback apiBase):
 *   GET  {apiBase}/items?includeArchived=false&limit=100      -> { items: ItemSummary[] }
 *   GET  {apiBase}/items/id/{itemId}                          -> ItemDetail (runs[], counts)
 *   POST {apiBase}/items                                      -> create local task
 *   POST {apiBase}/items/id/{itemId}/dispatch                -> dispatch (To do -> In progress)
 *   POST {apiBase}/items/id/{itemId}/approve                 -> approve (In review -> Done)
 *   POST {apiBase}/items/id/{itemId}/request-changes {feedback}
 *   POST {apiBase}/items/id/{itemId}/cancel-run {reason}
 *   POST {apiBase}/items/id/{itemId}/reorder {beforeItemId}  -> same-column reorder
 */

const APP_ID = 'com.dotharness.oratorio'

const COLUMNS = [
  { id: 'todo', label: 'To do', desc: 'Ready to triage or dispatch.' },
  { id: 'in_progress', label: 'In progress', desc: 'Queued, running, or awaiting retry.' },
  { id: 'in_review', label: 'In review', desc: 'Agent work is ready for judgment.' },
  { id: 'done', label: 'Done', desc: 'Accepted outcomes.' }
]

const LABEL_PRESETS = ['frontend', 'backend', 'docs', 'bug', 'security']
const ASSIGNEE_PRESETS = ['codex', 'reviewer', 'operator']
const BRANCH_PRESETS = ['main', 'develop', 'release']

const ICONS = {
  'search': '<circle cx="11" cy="11" r="7"/><path d="m21 21-4.3-4.3"/>',
  'plus': '<path d="M12 5v14"/><path d="M5 12h14"/>',
  'list-filter': '<path d="M3 6h18"/><path d="M7 12h10"/><path d="M10 18h4"/>',
  'chevron-down': '<path d="m6 9 6 6 6-6"/>',
  'user-round': '<circle cx="12" cy="8" r="4"/><path d="M5 21a7 7 0 0 1 14 0"/>',
  'external-link': '<path d="M15 3h6v6"/><path d="M10 14 21 3"/><path d="M21 14v5a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5"/>',
  'x': '<path d="M18 6 6 18"/><path d="m6 6 12 12"/>',
  'folder': '<path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2Z"/>',
  'git-pull-request': '<circle cx="6" cy="6" r="2.5"/><circle cx="6" cy="18" r="2.5"/><path d="M6 8.5v7"/><path d="M18 9.5v6"/><circle cx="18" cy="6.5" r="2.5"/><path d="M13 6.5h2.2a2.3 2.3 0 0 1 2.3 2.3"/>',
  'circle-dot': '<circle cx="12" cy="12" r="9"/><circle cx="12" cy="12" r="2.6" fill="currentColor" stroke="none"/>',
  'file-text': '<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5"/><path d="M9 13h6"/><path d="M9 17h5"/>',
  'tag': '<path d="M3 8v3.6a2 2 0 0 0 .6 1.4l7.4 7.4a2 2 0 0 0 2.8 0l4.6-4.6a2 2 0 0 0 0-2.8L11 5.6A2 2 0 0 0 9.6 5H5a2 2 0 0 0-2 2Z"/><circle cx="7.4" cy="9.4" r="1.2" fill="currentColor" stroke="none"/>',
  'check': '<path d="M20 6 9 17l-5-5"/>',
  'alert-triangle': '<path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z"/><path d="M12 9v4"/><path d="M12 17h.01"/>',
  'clock-pending': '<circle cx="12" cy="12" r="9"/><path d="M12 8v4l2.5 1.6"/>',
  'loader': '<path d="M21 12a9 9 0 1 1-6.2-8.57"/>',
  'git-commit': '<circle cx="12" cy="12" r="3.4"/><path d="M3 12h5.6"/><path d="M15.4 12H21"/>',
  'archive': '<rect x="3" y="4" width="18" height="4" rx="1"/><path d="M5 8v11a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V8"/><path d="M10 12h4"/>',
  'git-branch': '<circle cx="6" cy="6" r="2.5"/><circle cx="6" cy="18" r="2.5"/><circle cx="18" cy="7" r="2.5"/><path d="M6 8.5v7"/><path d="M18 9.5a6.5 6.5 0 0 1-6.5 6.5H8.5"/>',
  'play': '<path d="M7 5v14l11-7z"/>',
  'x-circle': '<circle cx="12" cy="12" r="9"/><path d="m15 9-6 6"/><path d="m9 9 6 6"/>',
  'message-square': '<path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/>',
  'lock': '<rect x="5" y="11" width="14" height="9" rx="2"/><path d="M8 11V8a4 4 0 0 1 8 0v3"/>',
  'plug': '<path d="M12 22v-5"/><path d="M9 7V2"/><path d="M15 7V2"/><path d="M7 7h10v4a5 5 0 0 1-10 0Z"/>',
  'sliders': '<path d="M4 7h9"/><path d="M17 7h3"/><circle cx="15" cy="7" r="2"/><path d="M4 17h3"/><path d="M11 17h9"/><circle cx="9" cy="17" r="2"/>',
  'alert-circle': '<circle cx="12" cy="12" r="9"/><path d="M12 8v4"/><path d="M12 16h.01"/>',
  'inbox': '<path d="M22 12h-6l-2 3h-4l-2-3H2"/><path d="M5.5 6h13l3.5 6v6a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2v-6Z"/>'
}

const GITHUB_GLYPH = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 .5C5.65.5.5 5.65.5 12c0 5.08 3.29 9.39 7.86 10.91.58.1.79-.25.79-.56v-2c-3.2.7-3.87-1.54-3.87-1.54-.52-1.33-1.28-1.68-1.28-1.68-1.05-.72.08-.7.08-.7 1.16.08 1.77 1.19 1.77 1.19 1.03 1.77 2.7 1.26 3.36.96.1-.75.4-1.26.73-1.55-2.55-.29-5.24-1.28-5.24-5.69 0-1.26.45-2.29 1.18-3.1-.12-.29-.51-1.46.11-3.04 0 0 .97-.31 3.18 1.18a11.04 11.04 0 0 1 5.78 0c2.2-1.49 3.17-1.18 3.17-1.18.63 1.58.23 2.75.11 3.04.74.81 1.18 1.84 1.18 3.1 0 4.42-2.69 5.39-5.26 5.68.41.36.78 1.06.78 2.14v3.17c0 .31.21.67.8.56C20.22 21.39 23.5 17.08 23.5 12 23.5 5.65 18.35.5 12 .5Z"/></svg>'
const GITLAB_GLYPH = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m23.6 9.6-.03-.09L20.42.7a.83.83 0 0 0-1.55.05L16.74 7.2H7.27L5.13.75a.82.82 0 0 0-.78-.56.83.83 0 0 0-.78.56L.43 9.5l-.03.1a5.85 5.85 0 0 0 2.13 6.78l.01.01.03.02 5.27 3.94 2.61 1.97 1.58 1.2a.97.97 0 0 0 1.18 0l1.58-1.2 2.6-1.97 5.32-3.96.02-.02a5.86 5.86 0 0 0 2.12-6.77Z"/></svg>'

const ORATORIO_LOGO =
  '<svg viewBox="225 190 640 640" fill="none" aria-hidden="true">'
  + '<defs>'
  + '<linearGradient id="ob-logo-blue" x1="279" y1="766" x2="736" y2="334" gradientUnits="userSpaceOnUse"><stop offset="0" stop-color="#1357ff"/><stop offset=".48" stop-color="#3f6cff"/><stop offset="1" stop-color="#7f99ff"/></linearGradient>'
  + '<linearGradient id="ob-logo-mark" x1="380" y1="696" x2="492" y2="557" gradientUnits="userSpaceOnUse"><stop offset="0" stop-color="#085cf6"/><stop offset=".55" stop-color="#1d6cff"/><stop offset="1" stop-color="#6f8dff"/></linearGradient>'
  + '<linearGradient id="ob-logo-yellow" x1="481" y1="174" x2="617" y2="713" gradientUnits="userSpaceOnUse"><stop offset="0" stop-color="#ffd51a"/><stop offset="1" stop-color="#f4ae00"/></linearGradient>'
  + '<filter id="ob-soft-shadow" x="-12%" y="-12%" width="124%" height="124%"><feDropShadow dx="0" dy="18" stdDeviation="24" flood-color="#001026" flood-opacity=".42"/></filter>'
  + '<filter id="ob-inner-lift" x="-8%" y="-8%" width="116%" height="116%"><feDropShadow dx="0" dy="10" stdDeviation="14" flood-color="#0b2f88" flood-opacity=".16"/></filter>'
  + '</defs>'
  + '<g transform="translate(128 128) scale(.75)">'
  + '<g filter="url(#ob-soft-shadow)"><rect x="201" y="365" width="622" height="513" rx="151" fill="#fff"/><rect x="145" y="472" width="136" height="256" rx="58" fill="#fff"/><rect x="743" y="472" width="136" height="256" rx="58" fill="#fff"/><rect x="438" y="270" width="148" height="182" rx="2" fill="#fff"/><circle cx="512" cy="229" r="116" fill="#fff"/></g>'
  + '<g filter="url(#ob-inner-lift)"><rect x="243" y="408" width="538" height="426" rx="113" fill="url(#ob-logo-blue)"/><rect x="188" y="514" width="90" height="171" rx="19" fill="url(#ob-logo-blue)"/><rect x="746" y="514" width="90" height="171" rx="19" fill="url(#ob-logo-blue)"/><rect x="479" y="337" width="66" height="119" rx="6" fill="url(#ob-logo-blue)"/></g>'
  + '<g><path d="M808 606 896 294" stroke="#fff" stroke-width="64" stroke-linecap="round"/><circle cx="896" cy="294" r="58" fill="#fff"/><path d="M808 606 896 294" stroke="#f7b500" stroke-width="30" stroke-linecap="round"/><circle cx="896" cy="294" r="32" fill="#f7b500"/></g>'
  + '<rect x="295" y="464" width="434" height="315" rx="78" fill="#fff"/>'
  + '<path d="M512 787 447 757a24 24 0 0 0-34 22v55a24 24 0 0 0 34 22l65-30 65 30a24 24 0 0 0 34-22v-55a24 24 0 0 0-34-22Z" fill="#05060a"/>'
  + '<circle cx="512" cy="229" r="73" fill="url(#ob-logo-yellow)"/>'
  + '<path d="M387 568 477 634 387 700" stroke="url(#ob-logo-mark)" stroke-width="38" stroke-linecap="round" stroke-linejoin="round"/>'
  + '<path d="M531 696h116" stroke="url(#ob-logo-yellow)" stroke-width="27" stroke-linecap="round"/>'
  + '</g></svg>'

// Animated wand glow overlaying the logo's wand tip (matches Oratorio's
// board-wand-overlay). Same base coords + transform as ORATORIO_LOGO so the
// pulsing core/halo/sparks register exactly over the static wand.
const ORATORIO_WAND =
  '<svg class="oratorio-wand-overlay" viewBox="225 190 640 640" aria-hidden="true" focusable="false">'
  + '<g transform="translate(128 128) scale(.75)">'
  + '<path class="oratorio-wand-aura" d="M808 606 896 294"/>'
  + '<ellipse class="oratorio-wand-orbit-halo" cx="852" cy="450" rx="106" ry="35" transform="rotate(-74 852 450)"/>'
  + '<circle class="oratorio-wand-expanding-halo" cx="896" cy="294" r="48"/>'
  + '<circle class="oratorio-wand-expanding-halo secondary" cx="896" cy="294" r="40"/>'
  + '<path class="oratorio-wand-body-halo" d="M808 606 896 294"/>'
  + '<path class="oratorio-wand-core" d="M808 606 896 294"/>'
  + '<circle class="oratorio-wand-tip-glow" cx="896" cy="294" r="42"/>'
  + '<g class="oratorio-wand-spark"><path d="M896 214v48"/><path d="M872 238h48"/></g>'
  + '<g class="oratorio-wand-spark secondary"><path d="M942 260v34"/><path d="M925 277h34"/></g>'
  + '</g></svg>'

export default function OratorioBoardView({ host }) {
  const React = host.react
  const { useCallback, useEffect, useMemo, useRef, useState } = React
  const h = React.createElement

  const ic = (name, cls) => h('svg', {
    className: 'ic' + (cls ? ' ' + cls : ''), viewBox: '0 0 24 24', 'aria-hidden': true,
    dangerouslySetInnerHTML: { __html: ICONS[name] || '' }
  })
  const raw = (html, props) => h('span', { ...(props || {}), dangerouslySetInnerHTML: { __html: html } })

  const [connection, setConnection] = useState(null)
  const [apiBase, setApiBase] = useState('')
  const [items, setItems] = useState([])
  const [detail, setDetail] = useState(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)
  const [actionBusy, setActionBusy] = useState(false)

  const [query, setQuery] = useState('')
  const [repoFilter, setRepoFilter] = useState('all')
  const [assigneeFilter, setAssigneeFilter] = useState('all')

  const [selectedId, setSelectedId] = useState(null)
  const [drawerOpen, setDrawerOpen] = useState(false)

  const [modal, setModal] = useState(null) // 'task' | 'cancel' | 'changes'

  const pendingRef = useRef(null) // deferred-commit write (blocks refresh until committed/undone)
  const shellRef = useRef(null)
  const dragRef = useRef(null)
  const [draggingId, setDraggingId] = useState(null)
  const [dragOverCol, setDragOverCol] = useState(null)
  const [dragPlaceholder, setDragPlaceholder] = useState(null) // { col, index }

  const connected = connection?.state === 'connected'
  const canRead = connected && !!apiBase

  // ── Reads ───────────────────────────────────────────────────────────────
  const refresh = useCallback(async (opts) => {
    if (pendingRef.current) return // don't clobber an optimistic, not-yet-committed move
    try {
      const status = await host.appBindings.getConnectionStatus(APP_ID)
      setConnection(status)
      const base = normalizeApiBase(status?.publicMetadata?.surfaceEndpoints?.apiBase)
      setApiBase(base)
      if (status?.state !== 'connected') { setItems([]); return }
      if (!base) {
        setItems([])
        setError('Oratorio is connected, but it has not published a board endpoint yet. Reconnect Oratorio from DotCraft.')
        return
      }
      const result = await host.network.getJson(base + '/items?includeArchived=false&limit=100', 12000)
      const next = Array.isArray(result?.items) ? result.items : Array.isArray(result?.tasks) ? result.tasks : []
      setItems(next.map(toCard))
      if (!opts?.keepError) setError(null)
    } catch (err) {
      setError(messageOf(err))
    } finally {
      setLoading(false)
    }
  }, [host])

  useEffect(() => {
    void refresh()
    const timer = window.setInterval(() => { void refresh({ keepError: true }) }, 30000)
    return () => window.clearInterval(timer)
  }, [refresh])

  // selected item detail (runs / counts / latest thread)
  const selected = useMemo(() => items.find((it) => it.id === selectedId) || null, [items, selectedId])
  useEffect(() => {
    if (!apiBase || !drawerOpen || !selected) { setDetail(null); return }
    let cancelled = false
    setDetail(null)
    host.network.getJson(apiBase + '/items/id/' + encodeURIComponent(selected.itemId), 12000)
      .then((d) => { if (!cancelled) setDetail(d) })
      .catch(() => { if (!cancelled) setDetail(null) })
    return () => { cancelled = true }
  }, [apiBase, host, drawerOpen, selected])

  useEffect(() => {
    const onKey = (e) => {
      if (e.key === 'Escape' && drawerOpen && !modal) setDrawerOpen(false)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [drawerOpen, modal])

  // ── Derived board ────────────────────────────────────────────────────────
  const repoOptions = useMemo(() => {
    const set = new Set()
    items.forEach((it) => { if (it.repo) set.add(it.repo) })
    return ['all', ...[...set].sort()]
  }, [items])
  const assigneeOptions = useMemo(() => {
    const set = new Set()
    items.forEach((it) => { if (it.assignee) set.add(it.assignee) })
    return ['all', ...[...set].sort()]
  }, [items])

  const visible = useMemo(() => {
    const q = query.trim().toLowerCase()
    return items.filter((it) => {
      if (it.archived) return false
      const qm = !q || [it.title, it.summary, it.repo, it.assignee, it.shortId].concat(it.labels || [])
        .some((v) => String(v || '').toLowerCase().includes(q))
      const rm = repoFilter === 'all' || it.repo === repoFilter
      const am = assigneeFilter === 'all' || it.assignee === assigneeFilter
      return qm && rm && am
    })
  }, [items, query, repoFilter, assigneeFilter])

  const byCol = useMemo(() => {
    const map = new Map(COLUMNS.map((c) => [c.id, []]))
    visible.forEach((it) => { if (map.has(it.col)) map.get(it.col).push(it) })
    COLUMNS.forEach((c) => map.get(c.id).sort((a, b) => (a.sort ?? 0) - (b.sort ?? 0)))
    return map
  }, [visible])

  useEffect(() => {
    if (drawerOpen && selectedId && !visible.some((it) => it.id === selectedId)) setDrawerOpen(false)
  }, [drawerOpen, selectedId, visible])

  // ── Toasts (DotCraft's native toast stack; the board does not roll its own) ──
  const showInfo = useCallback((message, isError) => {
    host.ui.showToast({ message, type: isError ? 'error' : 'info' })
  }, [host])

  // ── Handoffs / navigation ──────────────────────────────────────────────────
  const openOratorio = useCallback((path) => {
    host.appBindings.openApp(APP_ID, 'oratorio://open/' + path).catch((err) => showInfo(messageOf(err), true))
  }, [host, showInfo])
  const openThread = useCallback((threadId) => {
    if (threadId) host.navigation.openThread(threadId)
  }, [host])

  const connectOrReconnect = useCallback(async () => {
    setActionBusy(true)
    setError(null)
    try {
      const result = await host.appBindings.startConnection(APP_ID)
      if (result?.handoff?.uri) await host.appBindings.openApp(APP_ID, result.handoff.uri)
    } catch (err) {
      setError(messageOf(err))
    } finally {
      setActionBusy(false)
      window.setTimeout(() => { void refresh() }, 1200)
    }
  }, [host, refresh])

  // ── Writes ──────────────────────────────────────────────────────────────────
  const post = useCallback((path, body) => host.network.postJson(apiBase + path, body || {}, 15000), [host, apiBase])

  const clearPending = () => { pendingRef.current = null }

  // dispatch / approve: optimistic move now; commit when the undo window elapses,
  // or revert if the user clicks Undo. The undo window + action live in the host
  // toast (onExpire = commit, action = undo), so the board owns no toast timer.
  const deferredWrite = useCallback((item, kind) => {
    const cfg = {
      dispatch: { to: 'in_progress', ms: 8000, verb: 'Dispatching', patch: { state: 'dispatching', check: 'pending' }, path: '/dispatch' },
      approve: { to: 'done', ms: 5000, verb: 'Approved', patch: { state: 'approved', check: 'passing', archived: false }, path: '/approve' }
    }[kind]
    const snapshot = items
    setItems((cur) => moveLocally(cur, item.id, cfg.to, cfg.patch))
    if (kind === 'approve') celebrate(shellRef.current)
    pendingRef.current = { snapshot }
    host.ui.showToast({
      message: cfg.verb + ' “' + item.title + '”.',
      type: kind === 'approve' ? 'success' : 'info',
      durationMs: cfg.ms,
      action: {
        label: 'Undo',
        icon: 'undo',
        onClick: () => { setItems(snapshot); clearPending(); showInfo('Move undone.') }
      },
      onExpire: () => {
        clearPending()
        post('/items/id/' + encodeURIComponent(item.itemId) + cfg.path, {})
          .then(() => refresh())
          .catch((err) => { setItems(snapshot); showInfo(messageOf(err), true) })
      }
    })
  }, [items, host, post, refresh, showInfo])

  const decide = useCallback((item, kind) => {
    if (!item) return
    if (kind === 'dispatch' || kind === 'approve') { deferredWrite(item, kind); return }
    if (kind === 'cancel') { setSelectedId(item.id); setModal('cancel'); return }
    if (kind === 'request') { setSelectedId(item.id); setModal('changes'); return }
  }, [deferredWrite])

  const submitCancel = useCallback(async (reason) => {
    const item = selected
    setModal(null)
    if (!item) return
    try {
      await post('/items/id/' + encodeURIComponent(item.itemId) + '/cancel-run', { body: reason || '' })
      showInfo('Cancelled run for “' + item.title + '”.')
      await refresh()
    } catch (err) { showInfo(messageOf(err), true) }
  }, [selected, post, refresh, showInfo])

  const submitChanges = useCallback(async (feedback) => {
    const item = selected
    setModal(null)
    if (!item) return
    try {
      await post('/items/id/' + encodeURIComponent(item.itemId) + '/request-changes', { body: feedback || '' })
      showInfo('Requesting changes for “' + item.title + '”.')
      await refresh()
    } catch (err) { showInfo(messageOf(err), true) }
  }, [selected, post, refresh, showInfo])

  const submitCreate = useCallback(async (form) => {
    setModal(null)
    try {
      await post('/local-tasks', form)
      showInfo('Created “' + (form.title || 'task') + '” in To do.')
      await refresh()
    } catch (err) { showInfo(messageOf(err), true) }
  }, [post, refresh, showInfo])

  // ── Drag & drop ──────────────────────────────────────────────────────────────
  const onCardDragStart = (item) => (e) => {
    if (item.col === 'done') { e.preventDefault(); return }
    dragRef.current = { id: item.id, from: item.col }
    setDraggingId(item.id)
    try { e.dataTransfer.effectAllowed = 'move'; e.dataTransfer.setData('text/plain', item.id) } catch (_) {}
  }
  const onStackDragOver = (colId) => (e) => {
    if (!dragRef.current) return
    e.preventDefault()
    try { e.dataTransfer.dropEffect = 'move' } catch (_) {}
    setDragOverCol(colId)
    const stack = e.currentTarget
    const cards = [...stack.querySelectorAll('.oratorio-card:not(.is-dragging)')]
    let index = cards.length
    for (let i = 0; i < cards.length; i++) {
      const r = cards[i].getBoundingClientRect()
      if (e.clientY < r.top + r.height / 2) { index = i; break }
    }
    setDragPlaceholder({ col: colId, index })
  }
  const onStackDrop = (colId) => (e) => {
    if (!dragRef.current) return
    e.preventDefault()
    const drag = dragRef.current
    const placeholder = dragPlaceholder
    cleanupDrag()
    const item = items.find((it) => it.id === drag.id)
    if (!item) return
    const outcome = resolveDrag(drag.from, colId, item)
    if (outcome.kind === 'invalid') { showInfo(outcome.message, true); return }
    if (outcome.kind === 'reorder') {
      // Within-column ordering (boardSortOrder) is owned by Oratorio. Reflect the
      // drag locally for immediate feedback, then persist the new relative order.
      const beforeId = beforeItemIdFor(colId, placeholder?.index, drag.id, byCol)
      const snapshot = items
      setItems((cur) => reorderLocally(cur, drag.id, beforeId))
      post('/items/id/' + encodeURIComponent(item.itemId) + '/reorder', { beforeItemId: beforeId })
        .then(() => refresh())
        .catch((err) => { setItems(snapshot); showInfo(messageOf(err), true) })
      return
    }
    decide(item, outcome.kind)
  }
  const cleanupDrag = () => { dragRef.current = null; setDraggingId(null); setDragOverCol(null); setDragPlaceholder(null) }
  const onCardDragEnd = () => cleanupDrag()

  // ── Card / drawer interactions ───────────────────────────────────────────────
  const openDrawer = (id) => {
    if (drawerOpen && selectedId === id) { setDrawerOpen(false); return }
    setSelectedId(id); setDrawerOpen(true)
  }

  // ── Render ────────────────────────────────────────────────────────────────────
  if (loading) {
    return h('main', { className: 'oratorio-board' },
      surfaceState(h, ic, { glyph: 'loader', title: 'Loading Oratorio', body: 'Reading the app connection and board endpoint.' })
    )
  }

  return h('main', { className: 'oratorio-board' },
    h('header', { className: 'oratorio-board-header' },
      h('div', { className: 'oratorio-brand' },
        h('span', { className: 'oratorio-brand-mark' },
          raw(ORATORIO_LOGO, { className: 'oratorio-brand-logo', 'aria-hidden': true }),
          raw(ORATORIO_WAND, { className: 'oratorio-brand-wand', 'aria-hidden': true })
        ),
        h('span', { className: 'oratorio-brand-name' }, 'Oratorio')
      )
    ),
    h('section', { className: 'oratorio-toolbar', 'aria-label': 'Board filters' },
      h('label', { className: 'oratorio-search' },
        ic('search'),
        h('input', { type: 'search', value: query, placeholder: 'Search tasks', autoComplete: 'off', disabled: !canRead, onChange: (e) => setQuery(e.target.value) })
      ),
      h(Select, { host, ariaLabel: 'Repository', value: repoFilter, disabled: !canRead, onChange: setRepoFilter,
        options: repoOptions.map((v) => ({ value: v, label: v === 'all' ? 'All repositories' : v })) }),
      h(Select, { host, ariaLabel: 'Assignee', value: assigneeFilter, disabled: !canRead, onChange: setAssigneeFilter,
        options: assigneeOptions.map((v) => ({ value: v, label: v === 'all' ? 'All assignees' : v })) }),
      h('div', { className: 'oratorio-toolbar-actions' },
        h('button', { type: 'button', className: 'oratorio-btn-primary', disabled: !canRead, onClick: () => setModal('task') }, ic('plus'), 'New task')
      )
    ),
    h('section', { className: 'oratorio-shell', ref: shellRef },
      renderBoardBody(),
      h('aside', { className: 'oratorio-drawer' + (drawerOpen ? ' is-open' : ''), 'aria-label': 'Task quick view' },
        drawerOpen && selected ? renderDrawer() : null
      ),
      modal === 'task' && h(TaskModal, { host, onClose: () => setModal(null), onSubmit: submitCreate }),
      modal === 'cancel' && h(ReasonModal, { host, kind: 'cancel', name: selected?.title || 'this task', onClose: () => setModal(null), onSubmit: submitCancel }),
      modal === 'changes' && h(ReasonModal, { host, kind: 'changes', name: selected?.title || 'this task', onClose: () => setModal(null), onSubmit: submitChanges })
    )
  )

  function renderBoardBody() {
    if (!connected) {
      return h('div', { className: 'oratorio-columns' }, surfaceState(h, ic, {
        glyph: 'plug', title: 'Connect Oratorio to show the board here',
        body: 'DotCraft hosts the Oratorio board once the app connection is authorized. Consent still happens in Oratorio.',
        actions: [['Connect Oratorio', connectOrReconnect, true], ['Open Oratorio', () => openOratorio('board'), false]],
        busy: actionBusy
      }))
    }
    if (!apiBase) {
      return h('div', { className: 'oratorio-columns' }, surfaceState(h, ic, {
        glyph: 'sliders', title: 'Finish connecting Oratorio',
        body: 'Reconnect Oratorio so it can publish its local board endpoint to DotCraft.',
        actions: [['Reconnect', connectOrReconnect, true], ['Open Oratorio', () => openOratorio('board'), false]],
        busy: actionBusy
      }))
    }
    if (error && items.length === 0) {
      return h('div', { className: 'oratorio-columns' }, surfaceState(h, ic, {
        glyph: 'alert-circle', title: "Couldn't load the board",
        body: error, actions: [['Try again', () => { setLoading(true); refresh() }, true], ['Open Oratorio', () => openOratorio('board'), false]]
      }))
    }
    if (visible.length === 0) {
      return h('div', { className: 'oratorio-columns' }, COLUMNS.map((c) => renderColumn(c, [])))
    }
    return h('div', { className: 'oratorio-columns', 'aria-live': 'polite' }, COLUMNS.map((c) => renderColumn(c, byCol.get(c.id))))
  }

  function renderColumn(c, list) {
    const charged = c.id !== 'todo' && list.length > 0 ? ' is-charged' : ''
    const overCls = dragOverCol === c.id ? ' is-drag-over' : ''
    const children = []
    list.forEach((it, idx) => {
      if (dragPlaceholder && dragPlaceholder.col === c.id && dragPlaceholder.index === idx) {
        children.push(h('div', { className: 'oratorio-drop-placeholder', key: 'ph' }))
      }
      children.push(renderCard(it))
    })
    if (dragPlaceholder && dragPlaceholder.col === c.id && dragPlaceholder.index >= list.length) {
      children.push(h('div', { className: 'oratorio-drop-placeholder', key: 'ph' }))
    }
    if (list.length === 0 && !(dragPlaceholder && dragPlaceholder.col === c.id)) {
      children.push(h('p', { className: 'oratorio-col-empty', key: 'empty' }, colEmptyText(c.id)))
    }
    return h('section', { className: 'oratorio-column', key: c.id, 'aria-label': c.label },
      h('header', { className: 'oratorio-column-head' },
        h('div', null,
          h('div', { className: 'oratorio-column-title' }, c.label),
          h('div', { className: 'oratorio-column-desc' }, c.desc)
        ),
        h('span', { className: 'oratorio-count' + charged }, String(list.length))
      ),
      h('div', {
        className: 'oratorio-stack' + overCls, 'data-col': c.id,
        onDragOver: onStackDragOver(c.id),
        onDragLeave: (e) => { if (!e.currentTarget.contains(e.relatedTarget)) setDragOverCol(null) },
        onDrop: onStackDrop(c.id)
      }, children)
    )
  }

  function renderCard(it) {
    const locked = it.col === 'done'
    const cls = 'oratorio-card' + accentClass(it)
      + (drawerOpen && it.id === selectedId ? ' is-selected' : '')
      + (draggingId === it.id ? ' is-dragging' : '')
      + (locked ? ' is-locked' : '')
    return h('div', {
      key: it.id, className: cls, role: 'button', tabIndex: 0,
      draggable: !locked,
      'aria-pressed': drawerOpen && it.id === selectedId,
      onClick: () => openDrawer(it.id),
      onKeyDown: (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); openDrawer(it.id) } },
      onDragStart: onCardDragStart(it),
      onDragEnd: onCardDragEnd
    },
      locked && h('span', { className: 'oratorio-lock-tag' }, ic('lock'), 'Locked'),
      h('div', { className: 'oratorio-topline' }, sourceChip(h, ic, raw, it), kindChip(h, ic, it)),
      h('div', { className: 'oratorio-title-line' },
        h('span', { className: 'oratorio-title' }, it.title),
        h('span', { className: 'oratorio-dot ' + microDotClass(it) })
      ),
      h('div', { className: 'oratorio-preview' }, it.summary || ''),
      h('div', { className: 'oratorio-meta' }, it.meta || ''),
      h('div', { className: 'oratorio-footer' },
        [lifecyclePill(h, ic, it), statePill(h, ic, it), checkPill(h, ic, it)]
          .filter(Boolean)
          .map((el, i) => React.cloneElement(el, { key: 'p' + i }))
          .concat(labelChips(h, ic, it))
      )
    )
  }

  function renderDrawer() {
    const it = selected
    const runs = Array.isArray(detail?.runs) ? detail.runs : []
    const latestThread = latestRunThreadId(detail)
    const counts = artifactCounts(detail)
    return [
      h('div', { className: 'oratorio-drawer-head', key: 'head' },
        h('div', { className: 'oratorio-drawer-kicker-row' },
          h('span', { className: 'oratorio-kicker' }, 'Quick view'),
          h('button', { type: 'button', className: 'oratorio-icon-btn', 'aria-label': 'Close', onClick: () => setDrawerOpen(false) }, ic('x'))
        ),
        h('h2', { className: 'oratorio-drawer-title' }, it.title),
        h('div', { className: 'oratorio-drawer-chips' }, sourceChip(h, ic, raw, it), kindChip(h, ic, it))
      ),
      h('div', { className: 'oratorio-drawer-body', key: 'body' },
        h('p', { className: 'oratorio-drawer-summary' }, it.summary || 'No agent summary yet.'),
        h('div', { className: 'oratorio-facts' },
          fact(h, 'State', stateLabel(it.state)),
          fact(h, 'Check', checkLabel(it.check)),
          fact(h, 'Assignee', it.assignee || 'unassigned'),
          fact(h, 'Branch', it.branch || '—'),
          fact(h, 'Round', '#' + (it.round || 1)),
          fact(h, 'Updated', it.meta || '—')
        ),
        h('div', { className: 'oratorio-counts' },
          countCell(h, counts.drafts, 'Drafts'), countCell(h, counts.comments, 'Comments'), countCell(h, counts.writes, 'Writes')
        )
      ),
      h('div', { className: 'oratorio-drawer-actions', key: 'actions' }, drawerActionEls(h, ic, it, latestThread))
    ]
  }

  function drawerActionEls(h2, ic2, it, threadId) {
    const openOraBtn = (label) => h2('button', { type: 'button', key: 'open', className: 'oratorio-btn', onClick: () => openOratorio('task/' + encodeURIComponent(it.shortId || it.itemId)) }, ic2('external-link'), label)
    const threadBtn = (primary) => h2('button', { type: 'button', key: 'thread', className: primary ? 'oratorio-btn-primary' : 'oratorio-btn', disabled: !threadId, onClick: () => openThread(threadId) }, ic2('message-square'), 'Open thread')
    if (it.col === 'todo') return [h2('button', { type: 'button', className: 'oratorio-btn-primary', key: 'd', onClick: () => decide(it, 'dispatch') }, ic2('play'), 'Dispatch'), openOraBtn('Open in Oratorio')]
    if (it.col === 'in_progress') {
      const active = it.state === 'running' || it.state === 'dispatching'
      return [threadBtn(true), active
        ? h2('button', { type: 'button', className: 'oratorio-btn', key: 'c', onClick: () => decide(it, 'cancel') }, ic2('x-circle'), 'Cancel run')
        : openOraBtn('Open in Oratorio')]
    }
    if (it.col === 'in_review') return [
      h2('button', { type: 'button', className: 'oratorio-btn-primary', key: 'a', onClick: () => decide(it, 'approve') }, ic2('check'), 'Approve'),
      h2('button', { type: 'button', className: 'oratorio-btn', key: 'r', onClick: () => decide(it, 'request') }, ic2('message-square'), 'Request changes'),
      openOraBtn('Open in Oratorio')
    ]
    return [openOraBtn('View in Oratorio'), threadBtn(false)]
  }

}

// ── Custom select (mirrors host .dc-settings-select; no react-dom portal) ──────
function Select({ host, value, options, onChange, ariaLabel, disabled }) {
  const React = host.react
  const { useState, useRef, useEffect, useCallback } = React
  const h = React.createElement
  const [open, setOpen] = useState(false)
  const [pos, setPos] = useState(null)
  const triggerRef = useRef(null)
  const menuRef = useRef(null)
  const selected = options.find((o) => o.value === value) || options[0]

  const place = useCallback(() => {
    const t = triggerRef.current
    if (!t) return
    const r = t.getBoundingClientRect()
    const width = Math.max(r.width, 160)
    const top = Math.min(window.innerHeight - 8, r.bottom + 6)
    const left = Math.min(Math.max(8, r.left), Math.max(8, window.innerWidth - width - 8))
    setPos({ top, left, width })
  }, [])

  useEffect(() => {
    if (!open) return
    place()
    const onDown = (e) => {
      if (triggerRef.current?.contains(e.target) || menuRef.current?.contains(e.target)) return
      setOpen(false)
    }
    const onKey = (e) => { if (e.key === 'Escape') setOpen(false) }
    document.addEventListener('mousedown', onDown)
    window.addEventListener('keydown', onKey)
    window.addEventListener('resize', place)
    window.addEventListener('scroll', place, true)
    return () => {
      document.removeEventListener('mousedown', onDown)
      window.removeEventListener('keydown', onKey)
      window.removeEventListener('resize', place)
      window.removeEventListener('scroll', place, true)
    }
  }, [open, place])

  return h(React.Fragment, null,
    h('button', {
      ref: triggerRef, type: 'button', className: 'dc-settings-select', role: 'combobox',
      'aria-haspopup': 'listbox', 'aria-expanded': open, 'aria-label': ariaLabel,
      'data-open': open || undefined, 'data-disabled': disabled || undefined, disabled,
      onClick: () => setOpen((o) => !o)
    },
      h('span', { className: 'dc-settings-select__value' }, selected?.label ?? value),
      h('svg', { className: 'ic dc-settings-select__chevron', viewBox: '0 0 24 24', 'aria-hidden': true, dangerouslySetInnerHTML: { __html: '<path d="m6 9 6 6 6-6"/>' } })
    ),
    open && pos && h('div', {
      ref: menuRef, className: 'dc-settings-select-menu', role: 'listbox', 'aria-label': ariaLabel,
      style: { position: 'fixed', top: pos.top, left: pos.left, width: pos.width, maxHeight: 280 }
    },
      options.map((o) => h('div', {
        key: o.value, role: 'option', className: 'dc-settings-select-option',
        'aria-selected': o.value === value, 'data-active': o.value === value || undefined,
        onMouseDown: (e) => e.preventDefault(),
        onClick: () => { onChange(o.value); setOpen(false) }
      },
        h('span', { className: 'dc-settings-select-option__copy' },
          h('span', { className: 'dc-settings-select-option__label' }, o.label)
        ),
        o.value === value && h('span', { className: 'dc-settings-select-option__check' },
          h('svg', { className: 'ic', viewBox: '0 0 24 24', 'aria-hidden': true, dangerouslySetInnerHTML: { __html: '<path d="M20 6 9 17l-5-5"/>' } })
        )
      ))
    )
  )
}

// ── New task modal ──────────────────────────────────────────────────────────────
function TaskModal({ host, onClose, onSubmit }) {
  const React = host.react
  const { useState } = React
  const h = React.createElement
  const ic = (name) => h('svg', { className: 'ic', viewBox: '0 0 24 24', 'aria-hidden': true, dangerouslySetInnerHTML: { __html: ICONS[name] || '' } })
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [source, setSource] = useState('')
  const [labels, setLabels] = useState('')
  const [assignee, setAssignee] = useState('')
  const [branch, setBranch] = useState('')

  const addLabel = (l) => setLabels((cur) => {
    const parts = cur.split(',').map((s) => s.trim()).filter(Boolean)
    if (!parts.includes(l)) parts.push(l)
    return parts.join(', ')
  })

  const submit = (e) => {
    e.preventDefault()
    onSubmit({
      title: title.trim() || 'Untitled task',
      description: description.trim(),
      repository: source.trim() || null,
      labels: labels.split(',').map((s) => s.trim()).filter(Boolean),
      assignee: assignee.trim() || null,
      branch: branch.trim() || null
    })
  }

  const presetRow = (list, onPick, icon) => h('div', { className: 'oratorio-chip-picker' },
    list.map((v) => h('button', { key: v, type: 'button', className: 'oratorio-preset', onClick: () => onPick(v) }, ic(icon), v)))

  return h('div', { className: 'oratorio-modal-backdrop is-open', onMouseDown: (e) => { if (e.target === e.currentTarget) onClose() } },
    h('form', { className: 'oratorio-modal modal-wide', onSubmit: submit, onMouseDown: (e) => e.stopPropagation() },
      h('div', { className: 'oratorio-modal-head' },
        h('div', null, h('span', { className: 'oratorio-modal-eyebrow' }, 'Local task'), h('h2', null, 'New task')),
        h('button', { type: 'button', className: 'oratorio-icon-btn', 'aria-label': 'Close', onClick: onClose }, ic('x'))
      ),
      h('div', { className: 'oratorio-form' },
        h('label', { className: 'oratorio-field full' }, h('span', null, 'Title'),
          h('input', { value: title, required: true, autoFocus: true, onChange: (e) => setTitle(e.target.value) })),
        h('label', { className: 'oratorio-field full' }, h('span', null, 'Description'),
          h('textarea', { value: description, rows: 3, onChange: (e) => setDescription(e.target.value) })),
        h('label', { className: 'oratorio-field' }, h('span', null, 'Source project'),
          h('input', { value: source, placeholder: 'GitHub/GitLab project or canonical key', onChange: (e) => setSource(e.target.value) })),
        h('label', { className: 'oratorio-field' }, h('span', null, 'Labels'),
          h('input', { value: labels, placeholder: 'bug, docs, frontend', onChange: (e) => setLabels(e.target.value) })),
        h('div', { className: 'oratorio-chip-picker full' }, LABEL_PRESETS.map((v) =>
          h('button', { key: v, type: 'button', className: 'oratorio-preset', onClick: () => addLabel(v) }, ic('tag'), v))),
        h('div', { className: 'oratorio-routing' },
          h('label', { className: 'oratorio-field' }, h('span', null, 'Assignee'),
            h('input', { value: assignee, placeholder: 'Pick or leave blank', onChange: (e) => setAssignee(e.target.value) }),
            presetRow(ASSIGNEE_PRESETS, setAssignee, 'user-round')),
          h('label', { className: 'oratorio-field' },
            h('span', { className: 'oratorio-field-head' }, 'Base branch', h('small', null, 'used as run base ref')),
            h('input', { value: branch, placeholder: 'main', onChange: (e) => setBranch(e.target.value) }),
            presetRow(BRANCH_PRESETS, setBranch, 'git-branch'))
        )
      ),
      h('div', { className: 'oratorio-modal-actions' },
        h('button', { type: 'button', className: 'oratorio-btn', onClick: onClose }, 'Cancel'),
        h('button', { type: 'submit', className: 'oratorio-btn-primary' }, ic('plus'), 'Create task')
      )
    )
  )
}

// ── Cancel-run / request-changes modal ────────────────────────────────────────────
function ReasonModal({ host, kind, name, onClose, onSubmit }) {
  const React = host.react
  const { useState } = React
  const h = React.createElement
  const ic = (n) => h('svg', { className: 'ic', viewBox: '0 0 24 24', 'aria-hidden': true, dangerouslySetInnerHTML: { __html: ICONS[n] || '' } })
  const [text, setText] = useState('')
  const isCancel = kind === 'cancel'
  const submit = (e) => { e.preventDefault(); onSubmit(text.trim()) }
  return h('div', { className: 'oratorio-modal-backdrop is-open', onMouseDown: (e) => { if (e.target === e.currentTarget) onClose() } },
    h('form', { className: 'oratorio-modal', onSubmit: submit, onMouseDown: (e) => e.stopPropagation() },
      h('div', { className: 'oratorio-modal-head' },
        h('h2', null, isCancel ? 'Cancel run' : 'Request changes'),
        h('button', { type: 'button', className: 'oratorio-icon-btn', 'aria-label': 'Close', onClick: onClose }, ic('x'))
      ),
      h('p', null, isCancel
        ? 'Cancel the active run for “' + name + '” and move it back to To do.'
        : 'Send “' + name + '” back to In progress with feedback for the next round.'),
      h('label', null, isCancel ? 'Reason' : 'Feedback',
        isCancel
          ? h('input', { value: text, placeholder: 'Optional note', onChange: (e) => setText(e.target.value) })
          : h('textarea', { value: text, rows: 3, required: true, placeholder: 'What should the next round address?', onChange: (e) => setText(e.target.value) })
      ),
      h('div', { className: 'oratorio-modal-actions' },
        h('button', { type: 'button', className: 'oratorio-btn', onClick: onClose }, isCancel ? 'Keep running' : 'Cancel'),
        h('button', { type: 'submit', className: 'oratorio-btn-primary' }, isCancel ? 'Cancel run' : 'Request changes')
      )
    )
  )
}

// ── Pure render helpers ──────────────────────────────────────────────────────────
function sourceChip(h, ic, raw, it) {
  let icon, label, local = ''
  if (it.source === 'github') { icon = raw(GITHUB_GLYPH, { className: 'brand-glyph' }); label = it.repo || 'GitHub' }
  else if (it.source === 'gitlab') { icon = raw(GITLAB_GLYPH, { className: 'brand-glyph' }); label = it.repo || 'GitLab' }
  else { icon = ic('folder'); label = 'Local'; local = ' is-local' }
  return h('span', { className: 'oratorio-chip chip-source' + local }, icon, h('span', null, label))
}
function kindChip(h, ic, it) {
  const num = it.number || ''
  const looksLikeNumber = /^#?\d+$/.test(num)
  const withHash = num.charAt(0) === '#' ? num : '#' + num
  if (it.kind === 'pullRequest') return h('span', { className: 'oratorio-chip chip-kind' }, ic('git-pull-request'), h('span', null, looksLikeNumber ? withHash : 'PR'))
  if (it.kind === 'issue') return h('span', { className: 'oratorio-chip chip-kind' }, ic('circle-dot'), h('span', null, looksLikeNumber ? withHash : 'Issue'))
  return h('span', { className: 'oratorio-chip chip-kind' }, ic('file-text'), h('span', null, 'Task'))
}
function microDotClass(it) {
  if (it.state === 'dispatching' || it.state === 'running') return 'running'
  if (it.state === 'awaitingReview' || it.check === 'attention') return 'awaiting'
  if (it.state === 'failed' || it.check === 'failing') return 'failed'
  if (it.state === 'approved') return 'approved'
  return ''
}
function accentClass(it) {
  if (it.state === 'running' || it.state === 'dispatching') return ' accent-running'
  if (it.state === 'awaitingReview') return ' accent-awaiting'
  if (it.state === 'failed') return ' accent-failed'
  return ''
}
function statePill(h, ic, it) {
  if (it.state === 'discovered' || it.state === 'awaitingReview' || it.state === 'approved') return null
  if (it.state === 'running') return h('span', { className: 'oratorio-spinner', title: 'Running' }, ic('loader'))
  const map = { dispatching: ['state-dispatching', 'Dispatching'], failed: ['state-failed', 'Failed'], rejected: ['state-rejected', 'Rejected'], archived: ['state-archived', 'Archived'] }
  const m = map[it.state]
  return m ? h('span', { className: 'oratorio-pill ' + m[0] }, h('span', null, m[1])) : null
}
function checkPill(h, ic, it) {
  if (it.check === 'notConfigured' || !it.check) return null
  if (it.check === 'pending' && (it.state === 'dispatching' || it.state === 'running')) return null
  if (it.check === 'passing') return h('span', { className: 'oratorio-pill check-passing' }, ic('check'), h('span', null, 'Passing'))
  if (it.check === 'failing' || it.check === 'attention') return h('span', { className: 'oratorio-pill check-attention' }, ic('alert-triangle'), h('span', null, 'Attention'))
  if (it.check === 'pending') return h('span', { className: 'oratorio-pill check-pending' }, ic('clock-pending'), h('span', null, 'Running'))
  return null
}
function lifecyclePill(h, ic, it) {
  if (it.sourceState === 'merged') return h('span', { className: 'oratorio-pill lifecycle merged' }, ic('git-commit'), h('span', null, 'Merged'))
  if (it.sourceState === 'closed') return h('span', { className: 'oratorio-pill lifecycle' }, ic('archive'), h('span', null, 'Closed'))
  return null
}
function labelChips(h, ic, it) {
  const labels = it.labels || []
  if (!labels.length) return []
  const visible = labels.slice(0, 2)
  const hidden = labels.length - visible.length
  const out = visible.map((l) => h('span', { className: 'oratorio-label-chip', key: l }, ic('tag'), h('span', null, l)))
  if (hidden > 0) out.push(h('span', { className: 'oratorio-label-more', key: '+more', title: labels.join(', ') }, '+' + hidden))
  return out
}
function fact(h, k, v) { return h('div', { className: 'oratorio-fact' }, h('span', null, k), h('strong', null, v)) }
function countCell(h, n, label) { return h('span', null, h('strong', null, String(n)), label) }

function surfaceState(h, ic, o) {
  return h('div', { className: 'oratorio-surface-state' },
    h('div', { className: 'oratorio-surface-inner' },
      h('div', { className: 'oratorio-surface-glyph' }, ic(o.glyph)),
      h('h2', null, o.title),
      h('p', null, o.body),
      o.actions && h('div', { className: 'oratorio-surface-actions' },
        o.actions.map(([label, onClick, primary], i) =>
          h('button', { key: i, type: 'button', className: primary ? 'oratorio-btn-primary' : 'oratorio-btn', disabled: o.busy, onClick },
            primary ? ic('external-link') : null, label)))
    )
  )
}

// ── Data mapping ──────────────────────────────────────────────────────────────────
function toCard(item) {
  return {
    id: item.itemId || item.shortId || item.externalId,
    itemId: item.itemId,
    shortId: item.shortId,
    source: item.source || 'local',
    repo: item.repository || null,
    kind: item.kind || 'localTask',
    number: externalNumber(item.externalId),
    title: item.title || 'Untitled task',
    summary: item.latestSummary || 'No agent summary is available yet.',
    state: item.state || 'discovered',
    col: normalizeTaskStatus(item.taskStatus, item.state),
    check: item.checkState || 'notConfigured',
    labels: Array.isArray(item.labels) ? item.labels : [],
    assignee: item.assignee || null,
    branch: item.branch || null,
    sourceState: item.sourceState || null,
    round: item.currentRound || 1,
    sort: item.boardSortOrder ?? 0,
    archived: item.state === 'archived',
    meta: metaLabel(item)
  }
}
// Card meta line, matching Oratorio: "<source meta> · updated <when>".
function metaLabel(item) {
  const meta = sourceMetaLabel(item)
  const updated = relativeTime(item.updatedAt || item.sourceUpdatedAt)
  return updated ? meta + ' · updated ' + updated : meta
}
// Mirrors Oratorio sourceMetaLabel: local → label count; remote → lifecycle +
// draft + short sha + synced timestamp. Keeps the embedded board 1:1 with Oratorio.
function sourceMetaLabel(item) {
  const source = item.source || 'local'
  if (source === 'local') {
    const n = Array.isArray(item.labels) ? item.labels.length : 0
    return n ? n + (n === 1 ? ' label' : ' labels') : 'local task'
  }
  const lifecycle = item.sourceState === 'merged' ? 'Merged · ' : item.sourceState === 'closed' ? 'Closed · ' : ''
  const draft = item.isDraft ? 'Draft · ' : ''
  const sha = item.headSha ? shortSha(item.headSha) + ' · ' : ''
  const synced = item.lastSourceSyncAt ? 'synced ' + relativeTime(item.lastSourceSyncAt) : 'not synced'
  return lifecycle + draft + sha + synced
}
function shortSha(value) { return value && value.length > 7 ? value.slice(0, 7) : (value || '') }
// Trailing-digit issue/PR number ("issue:owner/repo#31" -> "#31"); else the raw id.
function externalNumber(externalId) {
  if (!externalId) return ''
  const m = String(externalId).match(/(\d+)$/)
  return m ? '#' + m[1] : String(externalId)
}
function relativeTime(iso) {
  if (!iso) return ''
  const t = Date.parse(iso)
  if (!t) return ''
  const secs = Math.max(0, Math.round((Date.now() - t) / 1000))
  if (secs < 60) return 'just now'
  const mins = Math.round(secs / 60)
  if (mins < 60) return mins + ' min ago'
  const hrs = Math.round(mins / 60)
  if (hrs < 24) return hrs + ' hr ago'
  return new Date(t).toLocaleDateString()
}
function normalizeTaskStatus(taskStatus, state) {
  if (taskStatus === 'todo' || taskStatus === 'in_progress' || taskStatus === 'in_review' || taskStatus === 'done') return taskStatus
  if (state === 'discovered') return 'todo'
  if (state === 'awaitingReview') return 'in_review'
  if (state === 'approved' || state === 'archived') return 'done'
  return 'in_progress'
}
function stateLabel(s) {
  return { discovered: 'Discovered', dispatching: 'Dispatching', running: 'Running', awaitingReview: 'Awaiting review', approved: 'Approved', rejected: 'Rejected', failed: 'Failed', archived: 'Archived' }[s] || s || 'Unknown'
}
function checkLabel(c) {
  return { passing: 'Passing', attention: 'Attention', failing: 'Attention', pending: 'Running', notConfigured: 'Not configured' }[c] || c || 'Unknown'
}
function latestRunThreadId(detail) {
  const runs = Array.isArray(detail?.runs) ? [...detail.runs] : []
  runs.sort((a, b) => Date.parse(b.startedAt || b.completedAt || '') - Date.parse(a.startedAt || a.completedAt || ''))
  return runs.find((r) => typeof r.threadId === 'string' && r.threadId.trim())?.threadId || null
}
function artifactCounts(detail) {
  return {
    drafts: count(detail?.drafts) + count(detail?.reviewDrafts) + count(detail?.implementationDrafts),
    comments: count(detail?.comments),
    writes: count(detail?.sourceWrites) + count(detail?.deliveries)
  }
}
function count(v) { return Array.isArray(v) ? v.length : (typeof v === 'number' ? v : 0) }

// ── Drag resolution (mirrors Oratorio dragMatrix.ts) ─────────────────────────────
function resolveDrag(from, to, item) {
  if (from === to) return { kind: 'reorder' }
  if (from === 'done') return { kind: 'invalid', message: 'Done tasks cannot be reopened by dragging.' }
  if (from === 'todo' && to === 'in_progress') return { kind: 'dispatch' }
  if (from === 'in_progress' && to === 'todo') {
    if (item.state === 'dispatching' || item.state === 'running') return { kind: 'cancel' }
    return { kind: 'invalid', message: 'Only dispatching or running tasks can be cancelled by dragging.' }
  }
  if (from === 'in_review' && to === 'done') return { kind: 'approve' }
  if (from === 'in_review' && to === 'in_progress') return { kind: 'request' }
  return { kind: 'invalid', message: 'Cannot move from ' + colLabel(from) + ' to ' + colLabel(to) + '.' }
}
function colLabel(id) { return (COLUMNS.find((c) => c.id === id) || {}).label || id }
function colEmptyText(id) {
  return { todo: 'No tasks to triage.', in_progress: 'Nothing running right now.', in_review: 'No work waiting on you.', done: 'No accepted outcomes yet.' }[id] || 'No tasks here.'
}
function beforeItemIdFor(colId, index, draggedId, byCol) {
  const list = (byCol.get(colId) || []).filter((it) => it.id !== draggedId)
  const target = list[index]
  return target ? target.itemId : null
}
function moveLocally(items, id, toCol, patch) {
  return items.map((it) => it.id === id ? { ...it, col: toCol, ...patch } : it)
}
function reorderLocally(items, id, beforeId) {
  const moving = items.find((it) => it.id === id)
  if (!moving) return items
  const colItems = items.filter((it) => it.col === moving.col && it.id !== id)
  const idx = beforeId ? colItems.findIndex((it) => it.itemId === beforeId) : -1
  if (idx < 0) colItems.push(moving)
  else colItems.splice(idx, 0, moving)
  const sortById = new Map()
  colItems.forEach((it, i) => sortById.set(it.id, i * 10))
  return items.map((it) => sortById.has(it.id) ? { ...it, sort: sortById.get(it.id) } : it)
}

function celebrate(shell) {
  if (!shell || (window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches)) return
  const r = shell.getBoundingClientRect()
  const burst = document.createElement('div')
  burst.className = 'oratorio-burst'
  burst.style.left = (r.width * 0.72) + 'px'
  burst.style.top = (r.height * 0.5) + 'px'
  for (let i = 0; i < 14; i++) {
    const p = document.createElement('i')
    const ang = (Math.PI * 2 * i) / 14
    const dist = 40 + (i % 3) * 16
    p.style.setProperty('--bx', Math.cos(ang) * dist + 'px')
    p.style.setProperty('--by', Math.sin(ang) * dist + 'px')
    p.style.animationDelay = (i % 5) * 18 + 'ms'
    burst.appendChild(p)
  }
  shell.appendChild(burst)
  window.setTimeout(() => burst.remove(), 1100)
}

function normalizeApiBase(value) {
  return typeof value === 'string' && value.trim() ? value.trim().replace(/\/+$/, '') : ''
}
function messageOf(err) {
  const raw = (err instanceof Error ? err.message : String(err == null ? '' : err)).trim()
  // Electron reports IPC failures as "Error invoking remote method '<channel>': <Type>: <msg>".
  // Strip that wrapper (and a leading error-type) so the user sees the cause, not the plumbing.
  const cleaned = raw
    .replace(/^Error invoking remote method '[^']*':\s*/i, '')
    .replace(/^[A-Za-z]+Error:\s*/, '')
    .trim()
  const low = cleaned.toLowerCase()
  if (cleaned === '' || low === 'fetch failed' || low.includes('econnrefused')
    || low.includes('failed to fetch') || low.includes('network request failed')) {
    return "Oratorio isn't responding. Make sure the Oratorio app is running, then try again."
  }
  if (low.includes('abort') || low.includes('timed out') || low.includes('timeout')) {
    return 'Oratorio took too long to respond. Try again in a moment.'
  }
  return cleaned
}
