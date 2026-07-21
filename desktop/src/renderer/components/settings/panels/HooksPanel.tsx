import { useEffect, useMemo, useState, type CSSProperties, type ReactNode } from 'react'
import {
  Anchor,
  ChevronDown,
  ChevronRight,
  Copy,
  FileSearch,
  RefreshCw,
  Settings as SettingsIcon,
  ShieldAlert,
  ShieldCheck,
  ShieldQuestion
} from 'lucide-react'

import { useT } from '../../../contexts/LocaleContext'
import { useHooksStore, type HookMetadata, type HookSource, type HookTrustStatus } from '../../../stores/hooksStore'
import { usePluginStore, type PluginEntry } from '../../../stores/pluginStore'
import { addToast } from '../../../stores/toastStore'
import type { MessageKey } from '../../../../shared/locales'
import { PluginIcon, pluginTitle } from '../../plugins/PluginCatalogItem'
import { IconButton } from '../../ui/IconButton'
import { Button } from '../../ui/Button'
import { PillSwitch } from '../../ui/PillSwitch'
import { SettingsBreadcrumb } from '../SettingsBreadcrumb'
import { SettingsDescriptionWithLearnMore } from '../SettingsLearnMoreLink'
import { SettingsGroup, SettingsRow } from '../SettingsGroup'
import { SettingsPanelShell } from '../SettingsPanelShell'

type SourceKey = 'user' | 'workspace' | `plugin:${string}`

interface HookSourceSummary {
  key: SourceKey
  source: HookSource
  title: string
  subtitle?: string
  pluginId?: string
  count: number
  hooks: HookMetadata[]
}

const EVENT_ORDER = [
  'SessionStart',
  'UserPromptSubmit',
  'PrePrompt',
  'PreToolUse',
  'PermissionRequest',
  'PostToolUse',
  'PostToolUseFailure',
  'PreCompact',
  'PostCompact',
  'SubagentStart',
  'SubagentStop',
  'Stop',
  'StopFailure'
]

export function HooksPanel(): JSX.Element {
  const t = useT()
  const hooks = useHooksStore((s) => s.hooks)
  const warnings = useHooksStore((s) => s.warnings)
  const errors = useHooksStore((s) => s.errors)
  const loading = useHooksStore((s) => s.loading)
  const updatingKey = useHooksStore((s) => s.updatingKey)
  const updatingPluginId = useHooksStore((s) => s.updatingPluginId)
  const error = useHooksStore((s) => s.error)
  const fetchHooks = useHooksStore((s) => s.fetchHooks)
  const setHookState = useHooksStore((s) => s.setHookState)
  const trustPluginHooks = useHooksStore((s) => s.trustPluginHooks)
  const plugins = usePluginStore((s) => s.plugins)
  const fetchPlugins = usePluginStore((s) => s.fetchPlugins)
  const [selectedSourceKey, setSelectedSourceKey] = useState<SourceKey | null>(null)
  const [expandedKeys, setExpandedKeys] = useState<Set<string>>(() => new Set())

  useEffect(() => {
    void fetchHooks()
    void fetchPlugins()
  }, [fetchHooks, fetchPlugins])

  const pluginById = useMemo(() => {
    const map = new Map<string, (typeof plugins)[number]>()
    for (const plugin of plugins) map.set(plugin.id, plugin)
    return map
  }, [plugins])

  const summaries = useMemo(
    () => buildSourceSummaries(hooks, pluginById, t),
    [hooks, pluginById, t]
  )
  const selectedSummary = selectedSourceKey
    ? summaries.find((summary) => summary.key === selectedSourceKey) ?? null
    : null
  const configSummaries = summaries.filter((summary) => summary.source !== 'plugin' && summary.count > 0)
  const pluginSummaries = summaries.filter((summary) => summary.source === 'plugin')

  async function toggleHook(hook: HookMetadata, enabled: boolean): Promise<void> {
    try {
      await setHookState(hook.key, { enabled })
    } catch (err) {
      addToast(t('settings.hooks.updateFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    }
  }

  async function trustHook(hook: HookMetadata): Promise<void> {
    try {
      await setHookState(hook.key, { trustedHash: hook.currentHash })
    } catch (err) {
      addToast(t('settings.hooks.updateFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    }
  }

  async function trustPlugin(summary: HookSourceSummary): Promise<void> {
    if (!summary.pluginId) return
    try {
      await trustPluginHooks(summary.pluginId)
    } catch (err) {
      addToast(t('settings.hooks.updateFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    }
  }

  const action = (
    <IconButton
      icon={<RefreshCw size={15} aria-hidden />}
      label={t('settings.hooks.refresh')}
      tooltipLabel={t('settings.hooks.refresh')}
      disabled={loading}
      onClick={() => {
        void fetchHooks()
        void fetchPlugins()
      }}
    />
  )

  const description = (
    <SettingsDescriptionWithLearnMore topic="hooks" aboutKey="settings.hooks.title">
      {t('settings.hooks.description')}
    </SettingsDescriptionWithLearnMore>
  )

  if (selectedSummary) {
    return (
      <SettingsPanelShell
        title={selectedSummary.title}
        description={sourceDescription(selectedSummary, t)}
        action={action}
        breadcrumb={
          <SettingsBreadcrumb
            parentLabel={t('settings.hooks.title')}
            currentLabel={selectedSummary.title}
            onBack={() => setSelectedSourceKey(null)}
          />
        }
      >
        <SourceDetail
          summary={selectedSummary}
          updatingKey={updatingKey}
          updatingPluginId={updatingPluginId}
          expandedKeys={expandedKeys}
          onToggleExpanded={(key) => {
            setExpandedKeys((current) => {
              const next = new Set(current)
              if (next.has(key)) next.delete(key)
              else next.add(key)
              return next
            })
          }}
          onToggleHook={(hook, enabled) => void toggleHook(hook, enabled)}
          onTrustHook={(hook) => void trustHook(hook)}
          onTrustPlugin={(summary) => void trustPlugin(summary)}
        />
      </SettingsPanelShell>
    )
  }

  return (
    <SettingsPanelShell title={t('settings.hooks.title')} description={description} action={action}>
      {error && <StatusText tone="error">{error}</StatusText>}
      {errors.length > 0 && (
        <StatusText tone="error">
          {t('settings.hooks.errors', { count: String(errors.length) })}
        </StatusText>
      )}
      {warnings.length > 0 && (
        <StatusText tone="warning">
          {t('settings.hooks.warnings', { count: String(warnings.length) })}
        </StatusText>
      )}
      {configSummaries.length > 0 && (
        <SettingsGroup title={t('settings.hooks.fromConfig')}>
          {configSummaries.map((summary) => (
            <SourceRow
              key={summary.key}
              summary={summary}
              icon={<SettingsIcon size={17} aria-hidden />}
              onOpen={() => setSelectedSourceKey(summary.key)}
            />
          ))}
        </SettingsGroup>
      )}
      <SettingsGroup title={t('settings.hooks.fromPlugins')}>
        {pluginSummaries.length === 0 ? (
          <SettingsRow>
            <div style={emptyTextStyle}>{loading ? t('settings.loading') : t('settings.hooks.noPluginHooks')}</div>
          </SettingsRow>
        ) : (
          pluginSummaries.map((summary) => {
            const plugin = summary.pluginId ? pluginById.get(summary.pluginId) : null
            return (
              <SourceRow
                key={summary.key}
                summary={summary}
                icon={plugin ? <PluginIcon plugin={plugin} size={30} /> : <Anchor size={17} aria-hidden />}
                onOpen={() => setSelectedSourceKey(summary.key)}
              />
            )
          })
        )}
      </SettingsGroup>
    </SettingsPanelShell>
  )
}

function SourceDetail({
  summary,
  updatingKey,
  updatingPluginId,
  expandedKeys,
  onToggleExpanded,
  onToggleHook,
  onTrustHook,
  onTrustPlugin
}: {
  summary: HookSourceSummary
  updatingKey: string | null
  updatingPluginId: string | null
  expandedKeys: Set<string>
  onToggleExpanded: (key: string) => void
  onToggleHook: (hook: HookMetadata, enabled: boolean) => void
  onTrustHook: (hook: HookMetadata) => void
  onTrustPlugin: (summary: HookSourceSummary) => void
}): JSX.Element {
  const t = useT()
  const groups = groupHooksByEvent(summary.hooks)
  const pluginReadOnly = summary.source === 'plugin'

  if (summary.hooks.length === 0) {
    return (
      <SettingsGroup>
        <SettingsRow>
          <div style={emptyTextStyle}>{t('settings.hooks.noHooks')}</div>
        </SettingsRow>
      </SettingsGroup>
    )
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
      {pluginReadOnly && (
        <PluginTrustOverview
          summary={summary}
          updating={summary.pluginId != null && updatingPluginId === summary.pluginId}
          onTrustPlugin={() => onTrustPlugin(summary)}
        />
      )}
      {groups.map((group) => (
        <SettingsGroup
          key={group.eventName}
          title={eventLabel(group.eventName, t)}
          description={eventDescription(group.eventName, t)}
        >
          {group.hooks.map((hook, index) => (
            <HookRow
              key={hook.key}
              hook={hook}
              index={index}
              expanded={expandedKeys.has(hook.key)}
              updating={updatingKey === hook.key}
              readOnlyActions={pluginReadOnly}
              onToggleExpanded={() => onToggleExpanded(hook.key)}
              onToggleHook={(enabled) => onToggleHook(hook, enabled)}
              onTrustHook={() => onTrustHook(hook)}
            />
          ))}
        </SettingsGroup>
      ))}
    </div>
  )
}

function PluginTrustOverview({
  summary,
  updating,
  onTrustPlugin
}: {
  summary: HookSourceSummary
  updating: boolean
  onTrustPlugin: () => void
}): JSX.Element {
  const t = useT()
  const trustStatus = pluginTrustStatus(summary.hooks)
  const needsTrust = trustStatus === 'untrusted' || trustStatus === 'modified'
  const control = needsTrust ? (
    <Button variant="primary" onClick={onTrustPlugin} disabled={updating}>
      {t('settings.hooks.pluginTrust.action')}
    </Button>
  ) : (
    <span style={trustBadgeStyle('trusted')}>
      {trustIcon('trusted')}
      {trustLabel('trusted', t)}
    </span>
  )

  return (
    <SettingsGroup>
      <SettingsRow
        label={t('settings.hooks.pluginTrust.title')}
        description={pluginTrustDescription(trustStatus, t)}
        control={control}
      />
    </SettingsGroup>
  )
}

function SourceRow({
  summary,
  icon,
  onOpen
}: {
  summary: HookSourceSummary
  icon: ReactNode
  onOpen: () => void
}): JSX.Element {
  const t = useT()
  return (
    <SettingsRow>
      <button type="button" onClick={onOpen} style={sourceRowButtonStyle}>
        <span style={sourceIconStyle}>{icon}</span>
        <span style={sourceTextStyle}>
          <strong style={sourceTitleStyle}>{summary.title}</strong>
          <span style={sourceSubtitleStyle}>
            {summary.subtitle ?? t('settings.hooks.hookCount', { count: String(summary.count) })}
          </span>
        </span>
        <ChevronRight size={17} aria-hidden style={{ color: 'var(--text-dimmed)', flexShrink: 0 }} />
      </button>
    </SettingsRow>
  )
}

function HookRow({
  hook,
  index,
  expanded,
  updating,
  readOnlyActions,
  onToggleExpanded,
  onToggleHook,
  onTrustHook
}: {
  hook: HookMetadata
  index: number
  expanded: boolean
  updating: boolean
  readOnlyActions: boolean
  onToggleExpanded: () => void
  onToggleHook: (enabled: boolean) => void
  onTrustHook: () => void
}): JSX.Element {
  const t = useT()
  const trusted = hook.trustStatus === 'trusted' || hook.trustStatus === 'managed'
  return (
    <SettingsRow orientation="block" style={{ gap: 0 }}>
      <div style={hookHeaderStyle}>
        <button type="button" onClick={onToggleExpanded} style={expandButtonStyle}>
          {expanded ? <ChevronDown size={16} aria-hidden /> : <ChevronRight size={16} aria-hidden />}
          <span>{t('settings.hooks.hookOrdinal', { number: String(index + 1) })}</span>
        </button>
        <span style={trustBadgeStyle(hook.trustStatus)}>
          {trustIcon(hook.trustStatus)}
          {trustLabel(hook.trustStatus, t)}
        </span>
        <div style={{ flex: 1 }} />
        {!readOnlyActions && hook.command && (
          <IconButton
            icon={<Copy size={14} aria-hidden />}
            label={t('settings.hooks.copyCommand')}
            tooltipLabel={t('settings.hooks.copyCommand')}
            size={28}
            onClick={() => {
              void navigator.clipboard?.writeText(hook.command ?? '')
              addToast(t('toast.copied'), 'success')
            }}
          />
        )}
        {!readOnlyActions && hook.sourcePath && (
          <IconButton
            icon={<FileSearch size={14} aria-hidden />}
            label={t('settings.hooks.openSource')}
            tooltipLabel={t('settings.hooks.openSource')}
            size={28}
            onClick={() => void window.api.shell.showItemInFolder(hook.sourcePath ?? '')}
          />
        )}
        {!readOnlyActions && (
          <PillSwitch
            checked={hook.enabled}
            onChange={onToggleHook}
            disabled={updating}
            aria-label={t('settings.hooks.toggleHook')}
          />
        )}
      </div>
      {expanded && (
        <div style={hookDetailStyle}>
          <DetailGridRow label={t('settings.hooks.field.handler')} value={hook.handlerType || 'command'} />
          {hook.matcher && <DetailGridRow label={t('settings.hooks.field.matcher')} value={hook.matcher} />}
          {hook.condition && <DetailGridRow label={t('settings.hooks.field.condition')} value={hook.condition} />}
          {hook.command && <DetailGridRow label={t('settings.hooks.field.command')} value={hook.command} mono />}
          {hook.shell && <DetailGridRow label={t('settings.hooks.field.shell')} value={hook.shell} />}
          <DetailGridRow label={t('settings.hooks.field.execution')} value={executionLabel(hook, t)} />
          <DetailGridRow
            label={t('settings.hooks.field.timeout')}
            value={hook.timeoutSec == null
              ? t('settings.hooks.timeoutDefault')
              : t('settings.hooks.timeoutSeconds', { seconds: String(hook.timeoutSec) })}
          />
          {hook.sourcePath && <DetailGridRow label={t('settings.hooks.field.source')} value={hook.sourcePath} mono />}
          {hook.pluginId && <DetailGridRow label={t('settings.hooks.field.plugin')} value={hook.pluginId} />}
          {hook.rewakeMessage && <DetailGridRow label={t('settings.hooks.field.rewakeMessage')} value={hook.rewakeMessage} />}
          {hook.rewakeSummary && <DetailGridRow label={t('settings.hooks.field.rewakeSummary')} value={hook.rewakeSummary} />}
          {hook.statusMessage && <DetailGridRow label={t('settings.hooks.field.status')} value={hook.statusMessage} />}
          {!readOnlyActions && !trusted && (
            <div style={trustActionRowStyle}>
              <Button variant="primary" onClick={onTrustHook} disabled={updating}>
                {hook.trustStatus === 'modified' ? t('settings.hooks.trustAgain') : t('settings.hooks.trust')}
              </Button>
            </div>
          )}
        </div>
      )}
    </SettingsRow>
  )
}

function pluginTrustStatus(hooks: HookMetadata[]): HookTrustStatus {
  if (hooks.some((hook) => hook.trustStatus === 'modified')) return 'modified'
  if (hooks.some((hook) => hook.trustStatus === 'untrusted')) return 'untrusted'
  return 'trusted'
}

function pluginTrustDescription(
  status: HookTrustStatus,
  t: (key: MessageKey | string) => string
): string {
  switch (status) {
    case 'modified':
      return t('settings.hooks.pluginTrust.modifiedDescription')
    case 'untrusted':
      return t('settings.hooks.pluginTrust.untrustedDescription')
    case 'managed':
    case 'trusted':
      return t('settings.hooks.pluginTrust.trustedDescription')
  }
}

function executionLabel(
  hook: HookMetadata,
  t: (key: MessageKey | string, vars?: Record<string, string | number>) => string
): string {
  const mode = hook.executionMode === 'async' ? t('settings.hooks.execution.async') : t('settings.hooks.execution.sync')
  return hook.asyncRewake ? `${mode} · ${t('settings.hooks.execution.rewake')}` : mode
}

function DetailGridRow({
  label,
  value,
  mono = false
}: {
  label: string
  value: string
  mono?: boolean
}): JSX.Element {
  return (
    <div style={detailGridRowStyle}>
      <div style={detailLabelStyle}>{label}</div>
      <div style={mono ? detailMonoValueStyle : detailValueStyle}>{value}</div>
    </div>
  )
}

function StatusText({ tone, children }: { tone: 'error' | 'warning'; children: ReactNode }): JSX.Element {
  return (
    <div style={{
      fontSize: 12,
      color: tone === 'error' ? 'var(--error, #f85149)' : 'var(--warning, #d29922)'
    }}>
      {children}
    </div>
  )
}

function buildSourceSummaries(
  hooks: HookMetadata[],
  pluginById: Map<string, PluginEntry>,
  t: (key: MessageKey | string, vars?: Record<string, string | number>) => string
): HookSourceSummary[] {
  const userHooks = hooks.filter((hook) => hook.source === 'user')
  const workspaceHooks = hooks.filter((hook) => hook.source === 'workspace')
  const summaries: HookSourceSummary[] = [
    {
      key: 'user',
      source: 'user',
      title: t('settings.hooks.userConfig'),
      subtitle: t('settings.hooks.hookCount', { count: String(userHooks.length) }),
      count: userHooks.length,
      hooks: userHooks
    },
    {
      key: 'workspace',
      source: 'workspace',
      title: t('settings.hooks.workspaceConfig'),
      subtitle: t('settings.hooks.hookCount', { count: String(workspaceHooks.length) }),
      count: workspaceHooks.length,
      hooks: workspaceHooks
    }
  ]

  const pluginHooks = new Map<string, HookMetadata[]>()
  for (const hook of hooks) {
    if (hook.source !== 'plugin' || !hook.pluginId) continue
    const list = pluginHooks.get(hook.pluginId) ?? []
    list.push(hook)
    pluginHooks.set(hook.pluginId, list)
  }

  for (const [pluginId, list] of [...pluginHooks.entries()].sort(([a], [b]) => a.localeCompare(b))) {
    const plugin = pluginById.get(pluginId)
    summaries.push({
      key: `plugin:${pluginId}`,
      source: 'plugin',
      title: plugin ? pluginTitle(plugin) : pluginId,
      subtitle: t('settings.hooks.hookCount', { count: String(list.length) }),
      pluginId,
      count: list.length,
      hooks: list
    })
  }

  return summaries
}

function groupHooksByEvent(hooks: HookMetadata[]): Array<{ eventName: string; hooks: HookMetadata[] }> {
  const byEvent = new Map<string, HookMetadata[]>()
  for (const hook of hooks) {
    const list = byEvent.get(hook.eventName) ?? []
    list.push(hook)
    byEvent.set(hook.eventName, list)
  }
  return [...byEvent.entries()]
    .sort(([a], [b]) => eventOrder(a) - eventOrder(b) || a.localeCompare(b))
    .map(([eventName, list]) => ({ eventName, hooks: list }))
}

function eventOrder(eventName: string): number {
  const index = EVENT_ORDER.indexOf(eventName)
  return index < 0 ? EVENT_ORDER.length : index
}

function eventLabel(eventName: string, t: (key: MessageKey | string) => string): string {
  return eventText(eventName, 'label', t)
}

function eventDescription(eventName: string, t: (key: MessageKey | string) => string): string {
  return eventText(eventName, 'description', t)
}

function eventText(eventName: string, kind: 'label' | 'description', t: (key: MessageKey | string) => string): string {
  const normalized = eventName.replace(/[^A-Za-z0-9]/g, '')
  const key = `settings.hooks.event.${normalized}.${kind}`
  const value = t(key)
  return value === key && kind === 'label' ? eventName : value
}

function sourceDescription(summary: HookSourceSummary, t: (key: MessageKey | string, vars?: Record<string, string | number>) => string): string {
  if (summary.source === 'plugin') {
    return t('settings.hooks.pluginSourceDescription', { count: String(summary.count) })
  }
  return summary.key === 'user'
    ? t('settings.hooks.userSourceDescription')
    : t('settings.hooks.workspaceSourceDescription')
}

function trustLabel(status: HookTrustStatus, t: (key: MessageKey) => string): string {
  switch (status) {
    case 'managed':
    case 'trusted':
      return t('settings.hooks.trusted')
    case 'modified':
      return t('settings.hooks.modified')
    case 'untrusted':
      return t('settings.hooks.untrusted')
  }
}

function trustIcon(status: HookTrustStatus): ReactNode {
  switch (status) {
    case 'managed':
    case 'trusted':
      return <ShieldCheck size={13} aria-hidden />
    case 'modified':
      return <ShieldAlert size={13} aria-hidden />
    case 'untrusted':
      return <ShieldQuestion size={13} aria-hidden />
  }
}

const sourceRowButtonStyle: CSSProperties = {
  width: '100%',
  display: 'flex',
  alignItems: 'center',
  gap: 12,
  padding: 0,
  border: 'none',
  background: 'transparent',
  color: 'inherit',
  textAlign: 'left',
  cursor: 'pointer'
}

const sourceIconStyle: CSSProperties = {
  width: 32,
  height: 32,
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  flexShrink: 0,
  color: 'var(--text-secondary)'
}

const sourceTextStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  minWidth: 0,
  flex: 1,
  gap: 3
}

const sourceTitleStyle: CSSProperties = {
  fontSize: 13,
  color: 'var(--text-primary)',
  lineHeight: 1.35
}

const sourceSubtitleStyle: CSSProperties = {
  fontSize: 12,
  color: 'var(--text-dimmed)',
  lineHeight: 1.35
}

const emptyTextStyle: CSSProperties = {
  width: '100%',
  color: 'var(--text-dimmed)',
  fontSize: 12
}

const hookHeaderStyle: CSSProperties = {
  width: '100%',
  display: 'flex',
  alignItems: 'center',
  gap: 8,
  minWidth: 0
}

const expandButtonStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: 6,
  border: 'none',
  background: 'transparent',
  color: 'var(--text-primary)',
  fontSize: 13,
  fontWeight: 600,
  padding: 0,
  cursor: 'pointer',
  minWidth: 86
}

function trustBadgeStyle(status: HookTrustStatus): CSSProperties {
  const color = status === 'trusted' || status === 'managed'
    ? 'var(--success, #3fb950)'
    : status === 'modified'
      ? 'var(--warning, #d29922)'
      : 'var(--text-dimmed)'
  return {
    display: 'inline-flex',
    alignItems: 'center',
    gap: 4,
    minHeight: 22,
    padding: '0 7px',
    borderRadius: 6,
    background: 'color-mix(in srgb, currentColor 9%, transparent)',
    color,
    fontSize: 11,
    fontWeight: 600,
    flexShrink: 0
  }
}

const hookDetailStyle: CSSProperties = {
  width: '100%',
  marginTop: 12,
  paddingTop: 12,
  borderTop: '1px solid var(--border-default)',
  display: 'grid',
  gap: 8
}

const detailGridRowStyle: CSSProperties = {
  display: 'grid',
  gridTemplateColumns: '88px minmax(0, 1fr)',
  gap: 12,
  alignItems: 'start'
}

const detailLabelStyle: CSSProperties = {
  color: 'var(--text-dimmed)',
  fontSize: 12,
  lineHeight: 1.45
}

const detailValueStyle: CSSProperties = {
  color: 'var(--text-primary)',
  fontSize: 12,
  lineHeight: 1.45,
  overflowWrap: 'anywhere'
}

const detailMonoValueStyle: CSSProperties = {
  ...detailValueStyle,
  fontFamily: 'var(--font-mono)',
  fontSize: 11.5
}

const trustActionRowStyle: CSSProperties = {
  display: 'flex',
  justifyContent: 'flex-start',
  paddingTop: 4
}
