import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties, type JSX } from 'react'
import { Ban, Check } from 'lucide-react'

import { useT } from '../../../contexts/LocaleContext'
import type { MessageKey } from '../../../../shared/locales'
import { normalizeWorkspaceConfigChangedPayload } from '../../../utils/workspaceConfigChanged'
import { SettingsPanelShell } from '../SettingsPanelShell'
import { SettingsGroup, SettingsRow } from '../SettingsGroup'
import { settingsDescriptionStyle, settingsPlaceholderStyle } from '../settingsTypography'
import { SegmentedControl } from '../ui/SegmentedControl'
import { SettingsSelect } from '../ui/SettingsSelect'
import { PillSwitch } from '../../ui/PillSwitch'
import { Button } from '../../ui/Button'

// ─── Wire types (mirror src/DotCraft.Core/Protocol/AppServer/Wire/SourceControlWire.cs) ───

type SourceControlProvider = 'none' | 'git' | 'perforce'
type SourceControlConnectionMode = 'p4config' | 'manual'

interface PerforceConnectionWire {
  port: string
  client: string
  user: string
  charset: string
  p4ConfigName: string
  p4ExecutablePath: string
  timeoutSeconds: number
  online: boolean
  autoOffline: boolean
}

interface SourceControlSnapshot {
  provider: SourceControlProvider
  effectiveProvider: SourceControlProvider
  connectionMode?: SourceControlConnectionMode
  status: string
  workspacePath: string
  perforce?: PerforceConnectionWire
  capabilities: {
    gitCommit: boolean
    perforceBinding: boolean
    perforceChangelist: boolean
    perforceShelve: boolean
    perforceSubmit: boolean
  }
}

interface SourceControlDiagnosticItem {
  code: string
  fallbackText: string
}

interface SourceControlTestResult {
  status: string
  code: string
  summary: string
  fallbackText: string
  identity: {
    serverAddress?: string
    user?: string
    client?: string
    charset?: string
    connectionMode?: string
  }
  workspace: {
    workspacePath?: string
    clientRoot?: string
    altRoots: string[]
    mappingOk?: boolean
  }
  authentication: {
    ticketStatus?: string
    loginRequired: boolean
    expiresMessage?: string
  }
  diagnostics: {
    p4Version?: string
    timeoutSeconds: number
    warningCount: number
    errorCode?: string
  }
  warnings: SourceControlDiagnosticItem[]
  errors: SourceControlDiagnosticItem[]
}

// Canonical client-side P4CHARSET values (fixed by the p4 client, independent of the server).
// Empty string ("Not set") is offered separately so we keep the "don't pass -C" default.
const P4_CHARSET_VALUES = [
  'none',
  'auto',
  'utf8',
  'utf8-bom',
  'utf8unchecked',
  'utf8unchecked-bom',
  'utf16',
  'utf16-nobom',
  'utf16le',
  'utf16le-bom',
  'utf16be',
  'utf16be-bom',
  'utf32',
  'utf32-nobom',
  'utf32le',
  'utf32le-bom',
  'utf32be',
  'utf32be-bom',
  'iso8859-1',
  'iso8859-5',
  'iso8859-7',
  'iso8859-15',
  'eucjp',
  'shiftjis',
  'winansi',
  'macosroman',
  'koi8-r',
  'cp949',
  'cp936',
  'cp950',
  'cp1251',
  'cp1253'
] as const

const DEFAULT_PERFORCE: PerforceConnectionWire = {
  port: '',
  client: '',
  user: '',
  charset: '',
  p4ConfigName: '',
  p4ExecutablePath: '',
  timeoutSeconds: 30,
  online: true,
  autoOffline: true
}

const PERFORCE_CONNECTION_KEYS: Array<keyof PerforceConnectionWire> = [
  'port',
  'client',
  'user',
  'charset',
  'p4ConfigName',
  'p4ExecutablePath',
  'timeoutSeconds'
]

function scRequest<T>(method: string, params: unknown, timeoutMs = 20_000): Promise<T> {
  return window.api.appServer.sendRequest(method, params, timeoutMs) as Promise<T>
}

function testRequestTimeoutMs(timeoutSeconds: number): number {
  const effectiveTimeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 30
  return Math.max(20_000, effectiveTimeoutSeconds * 1000 + 5_000)
}

// Map any persisted/legacy provider value (incl. the removed "auto") to a known one.
function normalizeProvider(p: string): SourceControlProvider {
  return p === 'none' || p === 'perforce' ? p : 'git'
}

interface SourceControlPanelProps {
  workspacePath?: string
}

export function SourceControlPanel({ workspacePath }: SourceControlPanelProps): JSX.Element {
  const t = useT()

  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [snapshot, setSnapshot] = useState<SourceControlSnapshot | null>(null)

  const [provider, setProvider] = useState<SourceControlProvider>('git')
  const [connectionMode, setConnectionMode] = useState<SourceControlConnectionMode>('p4config')
  const [form, setForm] = useState<PerforceConnectionWire>(DEFAULT_PERFORCE)

  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [savedTick, setSavedTick] = useState(false)
  const [testing, setTesting] = useState(false)
  const [testResult, setTestResult] = useState<SourceControlTestResult | null>(null)
  const [showDetails, setShowDetails] = useState(false)
  const dirtyRef = useRef(false)

  const applySnapshot = useCallback((snap: SourceControlSnapshot) => {
    setSnapshot(snap)
    setProvider(normalizeProvider(snap.provider))
    setConnectionMode(snap.connectionMode ?? 'p4config')
    setForm(snap.perforce ? { ...DEFAULT_PERFORCE, ...snap.perforce } : DEFAULT_PERFORCE)
  }, [])

  const reload = useCallback(async () => {
    setLoading(true)
    setLoadError(null)
    setTestResult(null)
    setSaveError(null)
    try {
      const snap = await scRequest<SourceControlSnapshot>('sourceControl/get', {})
      applySnapshot(snap)
    } catch (err) {
      setLoadError(err instanceof Error ? err.message : String(err))
    } finally {
      setLoading(false)
    }
    // workspacePath is only used to re-trigger a reload when the foreground workspace changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [applySnapshot, workspacePath])

  useEffect(() => {
    void reload()
  }, [reload])

  const refreshSilently = useCallback(async () => {
    try {
      const snap = await scRequest<SourceControlSnapshot>('sourceControl/get', {})
      applySnapshot(snap)
    } catch {
      // Ignore background refresh failures; the next explicit load surfaces errors.
    }
  }, [applySnapshot])

  // Live-refresh when another surface changes this workspace's source control binding.
  useEffect(() => {
    const unsubscribe = window.api.appServer.onNotification((payload) => {
      const event = normalizeWorkspaceConfigChangedPayload(payload as { method: string; params: unknown })
      if (event?.regions.includes('sourceControl') && !dirtyRef.current) {
        void refreshSilently()
      }
    })
    return unsubscribe
  }, [refreshSilently])

  const updateForm = useCallback(<K extends keyof PerforceConnectionWire>(
    key: K,
    value: PerforceConnectionWire[K],
    options: { invalidateTest?: boolean } = {}
  ) => {
    setForm((prev) => ({ ...prev, [key]: value }))
    setSavedTick(false)
    if (options.invalidateTest ?? true) {
      // Editing the connection invalidates any prior test result.
      setTestResult(null)
    }
  }, [])

  const dirty = useMemo(() => {
    if (!snapshot) return false
    if (provider !== snapshot.provider) return true
    if (provider === 'perforce') {
      if (connectionMode !== (snapshot.connectionMode ?? 'p4config')) return true
      const base = snapshot.perforce ?? DEFAULT_PERFORCE
      return (Object.keys(DEFAULT_PERFORCE) as (keyof PerforceConnectionWire)[]).some((k) => form[k] !== base[k])
    }
    return false
  }, [snapshot, provider, connectionMode, form])

  useEffect(() => {
    dirtyRef.current = dirty
  }, [dirty])

  const isPerforce = provider === 'perforce'
  const testConnected = testResult?.status === 'connected'
  const connectionFieldsChanged = useMemo(() => {
    if (!snapshot) return true
    if (provider !== snapshot.provider) return true
    if (provider !== 'perforce') return false
    if (connectionMode !== (snapshot.connectionMode ?? 'p4config')) return true
    const base = snapshot.perforce ?? DEFAULT_PERFORCE
    return PERFORCE_CONNECTION_KEYS.some((k) => form[k] !== base[k])
  }, [snapshot, provider, connectionMode, form])
  const canPreserveOnlineWithoutRetest =
    isPerforce
    && !connectionFieldsChanged
    && snapshot?.perforce?.online === true
  const perforceOnlineForSave = isPerforce
    ? (testConnected ? form.online : canPreserveOnlineWithoutRetest ? form.online : false)
    : false

  const handleTest = useCallback(async () => {
    setTesting(true)
    setTestResult(null)
    try {
      const result = await scRequest<SourceControlTestResult>(
        'sourceControl/test',
        {
          provider: 'perforce',
          connectionMode,
          perforce: form
        },
        testRequestTimeoutMs(form.timeoutSeconds)
      )
      setTestResult(result)
      if (result.status === 'connected') {
        setForm((prev) => ({ ...prev, online: true }))
        setSavedTick(false)
      }
      setShowDetails(result.status !== 'connected')
    } catch (err) {
      setTestResult({
        status: 'error',
        code: 'Unknown',
        summary: err instanceof Error ? err.message : String(err),
        fallbackText: err instanceof Error ? err.message : String(err),
        identity: {},
        workspace: { altRoots: [] },
        authentication: { loginRequired: false },
        diagnostics: { timeoutSeconds: form.timeoutSeconds, warningCount: 0 },
        warnings: [],
        errors: []
      })
      setShowDetails(true)
    } finally {
      setTesting(false)
    }
  }, [connectionMode, form])

  const handleSave = useCallback(async () => {
    setSaving(true)
    setSaveError(null)
    try {
      const perforceForSave = isPerforce
        ? { ...form, online: perforceOnlineForSave }
        : undefined
      const snap = await scRequest<SourceControlSnapshot>('sourceControl/update', {
        provider,
        connectionMode: isPerforce ? connectionMode : undefined,
        // Password is never sent to update — it is transient to Test Connection only.
        perforce: perforceForSave
      })
      applySnapshot(snap)
      setSavedTick(true)
    } catch (err) {
      setSaveError(err instanceof Error ? err.message : String(err))
    } finally {
      setSaving(false)
    }
  }, [provider, connectionMode, form, isPerforce, perforceOnlineForSave, applySnapshot])

  const handleDiscard = useCallback(() => {
    if (snapshot) applySnapshot(snapshot)
    setTestResult(null)
    setSaveError(null)
    setSavedTick(false)
  }, [snapshot, applySnapshot])

  const localizeCode = useCallback(
    (code: string, fallbackText: string): string => {
      const key = `settings.sourceControl.error.${code}` as MessageKey
      const localized = t(key)
      return localized === key ? fallbackText || code : localized
    },
    [t]
  )

  const charsetOptions = useMemo(() => {
    const opts: Array<{ value: string; label: string }> = [
      { value: '', label: t('settings.sourceControl.perforce.charsetDefault') },
      ...P4_CHARSET_VALUES.map((c) => ({ value: c, label: c }))
    ]
    // Preserve an unknown stored value (e.g. imported config) so it is never silently lost.
    if (form.charset && !(P4_CHARSET_VALUES as readonly string[]).includes(form.charset)) {
      opts.push({ value: form.charset, label: form.charset })
    }
    return opts
  }, [t, form.charset])
  const willSavePerforceOffline = isPerforce && !perforceOnlineForSave

  return (
    <SettingsPanelShell
      title={t('settings.sourceControl.title')}
      description={t('settings.sourceControl.description')}
    >
      {loading && (
        <SettingsGroup flush>
          <div style={settingsPlaceholderStyle()}>{t('settings.sourceControl.loading')}</div>
        </SettingsGroup>
      )}

      {!loading && loadError && (
        <SettingsGroup flush>
          <div style={noticeStyle('error')}>{t('settings.sourceControl.loadError', { error: loadError })}</div>
        </SettingsGroup>
      )}

      {!loading && !loadError && (
        <>
          <SettingsGroup title={t('settings.sourceControl.provider.label')}>
            <SettingsRow>
              <ProviderCards
                value={provider}
                onChange={(value) => {
                  setProvider(value)
                  setSavedTick(false)
                  setTestResult(null)
                }}
                t={t}
              />
            </SettingsRow>
            {provider === 'git' && (
              <SettingsRow>
                <div style={settingsDescriptionStyle(false)}>
                  {t('settings.sourceControl.git.description')}
                </div>
              </SettingsRow>
            )}
            {provider === 'none' && (
              <SettingsRow>
                <div style={settingsDescriptionStyle(false)}>
                  {t('settings.sourceControl.none.description')}
                </div>
              </SettingsRow>
            )}
          </SettingsGroup>

          {isPerforce && (
            <SettingsGroup title={t('settings.sourceControl.perforce.connectionTitle')}>
              <SettingsRow
                label={t('settings.sourceControl.connectionMode.label')}
                description={t('settings.sourceControl.connectionMode.hint')}
                control={
                  <SegmentedControl
                    ariaLabel={t('settings.sourceControl.connectionMode.label')}
                    value={connectionMode}
                    options={[
                      { value: 'p4config' as const, label: t('settings.sourceControl.connectionMode.p4config') },
                      { value: 'manual' as const, label: t('settings.sourceControl.connectionMode.manual') }
                    ]}
                    onChange={(value) => {
                      setConnectionMode(value)
                      setSavedTick(false)
                      setTestResult(null)
                    }}
                  />
                }
              />

              <TextField
                id="sc-p4-port"
                label={t('settings.sourceControl.perforce.port')}
                description={t('settings.sourceControl.perforce.portHint')}
                value={form.port}
                placeholder="ssl:perforce.example.com:1666"
                onChange={(v) => updateForm('port', v)}
              />
              <TextField
                id="sc-p4-client"
                label={t('settings.sourceControl.perforce.client')}
                description={t('settings.sourceControl.perforce.clientHint')}
                value={form.client}
                onChange={(v) => updateForm('client', v)}
              />
              <TextField
                id="sc-p4-user"
                label={t('settings.sourceControl.perforce.user')}
                value={form.user}
                onChange={(v) => updateForm('user', v)}
              />
              <SettingsRow
                label={t('settings.sourceControl.perforce.charset')}
                htmlFor="sc-p4-charset"
                orientation="block"
                control={
                  <SettingsSelect
                    id="sc-p4-charset"
                    ariaLabel={t('settings.sourceControl.perforce.charset')}
                    value={form.charset}
                    options={charsetOptions}
                    onValueChange={(v) => updateForm('charset', v)}
                    style={{ width: '100%' }}
                  />
                }
              />
              {connectionMode === 'p4config' && (
                <TextField
                  id="sc-p4-config"
                  label={t('settings.sourceControl.perforce.p4ConfigName')}
                  description={t('settings.sourceControl.perforce.p4ConfigNameHint')}
                  value={form.p4ConfigName}
                  placeholder=".p4config"
                  onChange={(v) => updateForm('p4ConfigName', v)}
                />
              )}
              <TextField
                id="sc-p4-exe"
                label={t('settings.sourceControl.perforce.executable')}
                description={t('settings.sourceControl.perforce.executableHint')}
                value={form.p4ExecutablePath}
                onChange={(v) => updateForm('p4ExecutablePath', v)}
              />
              <SettingsRow
                label={t('settings.sourceControl.perforce.timeout')}
                control={
                  <input
                    type="text"
                    inputMode="numeric"
                    aria-label={t('settings.sourceControl.perforce.timeout')}
                    value={form.timeoutSeconds === 0 ? '' : String(form.timeoutSeconds)}
                    onChange={(e) => {
                      const digits = e.target.value.replace(/[^0-9]/g, '').slice(0, 5)
                      updateForm('timeoutSeconds', digits === '' ? 0 : Math.min(3600, parseInt(digits, 10)))
                    }}
                    onBlur={() => {
                      if (form.timeoutSeconds < 1) updateForm('timeoutSeconds', 30)
                    }}
                    style={{ ...inputStyle, width: '96px' }}
                  />
                }
              />
              <SettingsRow
                label={t('settings.sourceControl.perforce.online')}
                description={t('settings.sourceControl.perforce.onlineHint')}
                control={
                  <PillSwitch
                    checked={form.online}
                    onChange={(v) => updateForm('online', v, { invalidateTest: false })}
                    aria-label={t('settings.sourceControl.perforce.online')}
                  />
                }
              />
              <SettingsRow
                label={t('settings.sourceControl.perforce.autoOffline')}
                description={t('settings.sourceControl.perforce.autoOfflineHint')}
                control={
                  <PillSwitch
                    checked={form.autoOffline}
                    onChange={(v) => updateForm('autoOffline', v, { invalidateTest: false })}
                    aria-label={t('settings.sourceControl.perforce.autoOffline')}
                  />
                }
              />
              {willSavePerforceOffline && (
                <SettingsRow>
                  <div style={noticeStyle('info')}>
                    {t('settings.sourceControl.perforce.offlineUntilVerified')}
                  </div>
                </SettingsRow>
              )}
            </SettingsGroup>
          )}

          {isPerforce && (
            <SettingsGroup title={t('settings.sourceControl.test.title')}>
              <SettingsRow>
                <Button
                  onClick={() => void handleTest()}
                  disabled={testing}
                >
                  {testing ? t('settings.sourceControl.action.testing') : t('settings.sourceControl.action.test')}
                </Button>
              </SettingsRow>
              {testResult && (
                <SettingsRow>
                  <TestResultView
                    result={testResult}
                    showDetails={showDetails}
                    onToggleDetails={() => setShowDetails((s) => !s)}
                    localizeCode={localizeCode}
                    t={t}
                  />
                </SettingsRow>
              )}
            </SettingsGroup>
          )}

          <div style={footerStyle}>
            {saveError && <div style={{ ...noticeStyle('error'), flex: 1 }}>{saveError}</div>}
            {savedTick && !dirty && (
              <span style={{ fontSize: '12px', color: 'var(--success, #34c759)' }}>
                {t('settings.sourceControl.action.saved')}
              </span>
            )}
            <Button onClick={handleDiscard} disabled={!dirty || saving}>
              {t('settings.sourceControl.action.discard')}
            </Button>
            <Button variant="primary" onClick={() => void handleSave()} disabled={saving}>
              {isPerforce && testConnected
                ? t('settings.sourceControl.action.save')
                : t('settings.sourceControl.action.saveGeneric')}
            </Button>
          </div>
        </>
      )}
    </SettingsPanelShell>
  )
}

// ─── Subcomponents ───

function ProviderCards({
  value,
  onChange,
  t
}: {
  value: SourceControlProvider
  onChange: (value: SourceControlProvider) => void
  t: (key: MessageKey, vars?: Record<string, string | number>) => string
}): JSX.Element {
  const options: Array<{ value: SourceControlProvider; label: string; icon: JSX.Element }> = [
    { value: 'git', label: t('settings.sourceControl.provider.git'), icon: <GitIcon /> },
    { value: 'perforce', label: t('settings.sourceControl.provider.perforce'), icon: <PerforceIcon /> },
    { value: 'none', label: t('settings.sourceControl.provider.none'), icon: <Ban size={24} color="var(--text-dimmed)" aria-hidden /> }
  ]
  return (
    <div role="radiogroup" aria-label={t('settings.sourceControl.provider.label')} style={providerCardsStyle}>
      {options.map((opt) => {
        const selected = opt.value === value
        return (
          <button
            key={opt.value}
            type="button"
            role="radio"
            aria-checked={selected}
            aria-label={opt.label}
            onClick={() => onChange(opt.value)}
            style={providerCardStyle(selected)}
          >
            {selected && (
              <span style={cardCheckStyle}>
                <Check size={14} strokeWidth={2.6} aria-hidden />
              </span>
            )}
            <span style={{ width: 28, height: 28, display: 'grid', placeItems: 'center', opacity: selected ? 1 : 0.82 }}>
              {opt.icon}
            </span>
            <span style={{ fontSize: '13px', fontWeight: 600 }}>{opt.label}</span>
          </button>
        )
      })}
    </div>
  )
}

function GitIcon(): JSX.Element {
  return (
    <svg viewBox="0 0 24 24" width="26" height="26" aria-hidden="true">
      <path fill="#F05032" d="M13.09 23.549a1.54 1.54 0 0 1-2.18 0L.451 13.089a1.54 1.54 0 0 1 0-2.179l7.191-7.19 2.733 2.733a1.85 1.85 0 0 0 .964 2.326v6.66a1.849 1.849 0 1 0 1.54 0V8.957l2.508 2.508a1.85 1.85 0 1 0 1.09-1.09l-2.634-2.634a1.85 1.85 0 0 0-2.378-2.377L8.73 2.63 10.91.451a1.54 1.54 0 0 1 2.179 0l10.459 10.46a1.54 1.54 0 0 1 0 2.179z" />
    </svg>
  )
}

function PerforceIcon(): JSX.Element {
  return (
    <svg viewBox="0 0 176.53 144.22" width="26" height="26" aria-hidden="true">
      <path fill="#4c00ff" d="M122.2,50.37l-16.76,9.68,4.24,2.45c5.01,2.89,5.55,7.69,5.55,9.62s-.54,6.73-5.55,9.62l-75.84,43.79c-5.01,2.89-9.44.96-11.11,0-1.67-.96-5.55-3.83-5.55-9.62V28.32c0-5.79,3.88-8.66,5.55-9.62,1.67-.96,6.09-2.89,11.11,0l20.09,11.6,17.17-9.91L42.42,3.83C33.56-1.28,22.99-1.28,14.14,3.83,5.29,8.95,0,18.1,0,28.32v87.58c0,10.22,5.29,19.38,14.14,24.49,4.43,2.56,9.28,3.83,14.14,3.83s9.71-1.28,14.14-3.83l75.84-43.79c8.85-5.11,14.14-14.27,14.14-24.49,0-8.6-3.75-16.43-10.2-21.74Z" />
      <path fill="#4c00ff" d="M54.33,93.86l16.76-9.68-4.24-2.45c-5.01-2.89-5.55-7.69-5.55-9.62s.54-6.73,5.55-9.62l75.84-43.79c5.01-2.89,9.44-.96,11.11,0,1.67.96,5.55,3.83,5.55,9.62v87.58c0,5.79-3.88,8.66-5.55,9.62-1.67.96-6.09,2.89-11.11,0l-20.09-11.6-17.17,9.91,28.68,16.56c8.85,5.11,19.43,5.11,28.28,0,8.85-5.11,14.14-14.27,14.14-24.49V28.32c0-10.22-5.29-19.38-14.14-24.49-4.43-2.56-9.28-3.83-14.14-3.83s-9.71,1.28-14.14,3.83L58.27,47.62c-8.85,5.11-14.14,14.27-14.14,24.49,0,8.6,3.75,16.43,10.2,21.74Z" />
    </svg>
  )
}

function TextField({
  id,
  label,
  description,
  value,
  placeholder,
  onChange
}: {
  id: string
  label: string
  description?: string
  value: string
  placeholder?: string
  onChange: (value: string) => void
}): JSX.Element {
  return (
    <SettingsRow
      label={label}
      description={description}
      htmlFor={id}
      orientation="block"
      control={
        <input
          id={id}
          type="text"
          value={value}
          placeholder={placeholder}
          onChange={(e) => onChange(e.target.value)}
          autoComplete="off"
          spellCheck={false}
          style={inputStyle}
        />
      }
    />
  )
}

function TestResultView({
  result,
  showDetails,
  onToggleDetails,
  localizeCode,
  t
}: {
  result: SourceControlTestResult
  showDetails: boolean
  onToggleDetails: () => void
  localizeCode: (code: string, fallbackText: string) => string
  t: (key: MessageKey, vars?: Record<string, string | number>) => string
}): JSX.Element {
  const tone = result.status === 'connected' ? 'success' : result.status === 'loginRequired' ? 'warning' : 'error'
  const headline = result.status === 'connected'
    ? (result.summary || localizeCode(result.code, result.fallbackText))
    : localizeCode(result.code, result.fallbackText || result.summary)
  // The headline already conveys the primary code; only surface errors that add something new
  // so a single-error failure does not print the same sentence twice.
  const extraErrors = result.errors.filter((e) => e.code !== result.code)
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', width: '100%' }}>
      <div style={noticeStyle(tone)}>{headline}</div>

      {extraErrors.length > 0 && (
        <ul style={diagListStyle}>
          {extraErrors.map((e, i) => (
            <li key={`e-${i}`} style={{ color: 'var(--error, #ff453a)' }}>{localizeCode(e.code, e.fallbackText)}</li>
          ))}
        </ul>
      )}
      {result.warnings.length > 0 && (
        <ul style={diagListStyle}>
          {result.warnings.map((w, i) => (
            <li key={`w-${i}`} style={{ color: 'var(--warning, #ff9500)' }}>{localizeCode(w.code, w.fallbackText)}</li>
          ))}
        </ul>
      )}

      <Button variant="ghost" size="sm" onClick={onToggleDetails} style={{ alignSelf: 'flex-start' }}>
        {showDetails ? t('settings.sourceControl.test.hideDetails') : t('settings.sourceControl.test.showDetails')}
      </Button>

      {showDetails && (
        <dl style={detailGridStyle}>
          <DetailRow label={t('settings.sourceControl.test.server')} value={result.identity.serverAddress} />
          <DetailRow label={t('settings.sourceControl.test.user')} value={result.identity.user} />
          <DetailRow label={t('settings.sourceControl.test.client')} value={result.identity.client} />
          <DetailRow label={t('settings.sourceControl.test.clientRoot')} value={result.workspace.clientRoot} />
          <DetailRow label={t('settings.sourceControl.test.ticket')} value={result.authentication.ticketStatus} />
          <DetailRow label={t('settings.sourceControl.test.p4Version')} value={result.diagnostics.p4Version} />
        </dl>
      )}
    </div>
  )
}

function DetailRow({ label, value }: { label: string; value?: string }): JSX.Element | null {
  if (!value) return null
  return (
    <>
      <dt style={{ color: 'var(--text-dimmed)' }}>{label}</dt>
      <dd style={{ margin: 0, color: 'var(--text-secondary)', wordBreak: 'break-all' }}>{value}</dd>
    </>
  )
}

// ─── Styles ───

const providerCardsStyle: CSSProperties = {
  display: 'grid',
  gridTemplateColumns: 'repeat(3, 1fr)',
  gap: '10px',
  width: '100%'
}

function providerCardStyle(selected: boolean): CSSProperties {
  return {
    position: 'relative',
    appearance: 'none',
    cursor: 'pointer',
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: '9px',
    padding: '16px 10px 13px',
    borderRadius: '12px',
    border: `1.5px solid ${selected ? 'var(--accent)' : 'var(--border-default)'}`,
    background: selected ? 'color-mix(in srgb, var(--accent) 12%, var(--bg-primary))' : 'var(--bg-primary)',
    color: selected ? 'var(--text-primary)' : 'var(--text-secondary)',
    transition: 'border-color 130ms ease, background 130ms ease, color 130ms ease'
  }
}

const cardCheckStyle: CSSProperties = {
  position: 'absolute',
  top: '8px',
  right: '8px',
  color: 'var(--accent)',
  display: 'inline-flex'
}

const inputStyle: CSSProperties = {
  width: '100%',
  boxSizing: 'border-box',
  padding: '8px 10px',
  fontSize: 'var(--type-ui-size)',
  borderRadius: '8px',
  border: '1px solid var(--border-default)',
  background: 'var(--bg-primary)',
  color: 'var(--text-primary)',
  outline: 'none'
}

function noticeStyle(tone: 'error' | 'info' | 'warning' | 'success'): CSSProperties {
  const palette =
    tone === 'error'
      ? { bg: 'rgba(255, 69, 58, 0.12)', fg: 'var(--error, #ff453a)' }
      : tone === 'warning'
        ? { bg: 'rgba(255, 149, 0, 0.12)', fg: 'var(--warning, #ff9500)' }
        : tone === 'success'
          ? { bg: 'rgba(34, 197, 94, 0.14)', fg: 'var(--success, #22c55e)' }
          : { bg: 'var(--bg-tertiary)', fg: 'var(--text-secondary)' }
  return {
    padding: '10px 12px',
    borderRadius: '10px',
    fontSize: '12px',
    background: palette.bg,
    color: palette.fg,
    lineHeight: 1.5
  }
}

const footerStyle: CSSProperties = {
  display: 'flex',
  justifyContent: 'flex-end',
  alignItems: 'center',
  gap: '10px',
  flexWrap: 'wrap'
}

const diagListStyle: CSSProperties = {
  margin: 0,
  paddingLeft: '18px',
  fontSize: '12px',
  lineHeight: 1.6
}

const detailGridStyle: CSSProperties = {
  display: 'grid',
  gridTemplateColumns: 'max-content 1fr',
  gap: '4px 12px',
  margin: 0,
  fontSize: '12px'
}
