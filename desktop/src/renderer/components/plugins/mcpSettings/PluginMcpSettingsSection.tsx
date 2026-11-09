import { useId, useState, type JSX, type KeyboardEvent } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { callDesktopPluginMcpTool } from '../../../plugins/desktopPluginMcp'
import type { PluginEntry } from '../../../stores/pluginStore'
import { addToast } from '../../../stores/toastStore'
import { SettingsGroup, SettingsRow } from '../../settings/SettingsGroup'
import {
  SETTINGS_SURFACE_CLASS,
  settingsErrorTextStyle,
  settingsHintStyle
} from '../../settings/settingsTypography'
import { Button } from '../../ui/Button'
import { Input } from '../../ui/Input'
import { PillSwitch } from '../../ui/PillSwitch'
import { Select } from '../../ui/Select'
import { Skeleton, SkeletonRow } from '../../ui/Skeleton'
import {
  firstTextContent,
  hasTool,
  parseSettingsInput,
  settingsGroups,
  toolRequiresArguments,
  type McpSettingsGroup,
  type McpSettingsInputError,
  type McpSettingsLayoutItem,
  type McpSettingsProperty,
  type McpSettingsValue
} from './mcpSettingsModel'
import {
  useMcpServerSettings,
  usePluginSettingsServers,
  type McpServerSettingsState,
  type PluginSettingsServer
} from './useMcpServerSettings'
import styles from './PluginMcpSettingsSection.module.css'

type Translate = ReturnType<typeof useT>

export function PluginMcpSettingsSection({ plugin }: { plugin: PluginEntry }): JSX.Element | null {
  const t = useT()
  const headingId = useId()
  const servers = usePluginSettingsServers(plugin.id, plugin.installed && plugin.enabled)
  if (servers.length === 0) return null
  return (
    <section
      className={`${styles.section} ${SETTINGS_SURFACE_CLASS}`}
      aria-labelledby={headingId}
      data-plugin-mcp-settings
    >
      <h2 id={headingId} className={styles.heading}>{t('plugins.detail.settings')}</h2>
      {servers.map((server) => (
        <McpServerSettings
          key={server.status.name}
          server={server}
          serverName={servers.length > 1 ? server.status.declaredName || server.status.name : undefined}
        />
      ))}
    </section>
  )
}

function McpServerSettings({ server, serverName }: {
  server: PluginSettingsServer
  serverName?: string
}): JSX.Element {
  const t = useT()
  const state = useMcpServerSettings(server)
  const canUpdate = hasTool(server.status, server.tools.updateTool)
  const disabled = state.pendingChange != null || state.failed || !canUpdate
  const groups = state.definition ? settingsGroups(state.definition) : []
  const groupTitle = (group: McpSettingsGroup): string | undefined => {
    const title = group.other ? t('plugins.settings.otherSettings') : group.title
    return serverName && title ? `${serverName} · ${title}` : serverName ?? title
  }

  if (!state.definition && state.failed) {
    return (
      <SettingsGroup title={serverName}>
        <SettingsRow>
          <FailedNotice loading={state.loading} onRetry={state.retry} />
        </SettingsRow>
      </SettingsGroup>
    )
  }
  if (!state.definition) return <SettingsSkeleton title={serverName} />

  return (
    <>
      {(state.failed || !canUpdate) && (
        <div className={styles.notice}>
          {state.failed
            ? <FailedNotice loading={state.loading} onRetry={state.retry} />
            : <div style={settingsHintStyle(false)} role="alert">{t('plugins.settings.updateUnavailable')}</div>}
        </div>
      )}
      {groups.map((group, index) => (
        <SettingsGroup key={`${group.title ?? ''}:${index}`} title={groupTitle(group)}>
          {group.items.map((item) => (
            <SettingsItem
              key={item.kind === 'tool' ? `tool:${item.tool}` : `property:${item.property}`}
              item={item}
              server={server}
              state={state}
              disabled={disabled}
            />
          ))}
        </SettingsGroup>
      ))}
    </>
  )
}

const SKELETON_ROWS: Array<[string, string]> = [['28%', '46%'], ['22%', '58%'], ['34%', '40%']]

function SettingsSkeleton({ title }: { title?: string }): JSX.Element {
  const t = useT()
  return (
    <div role="status" aria-busy="true" aria-label={t('plugins.settings.loading')}>
      <SettingsGroup title={title}>
        {SKELETON_ROWS.map(([label, hint]) => (
          <SettingsRow key={label}>
            <SkeletonRow lines={[label, hint]} style={{ flex: 1 }} />
            <Skeleton width={96} height={28} radius={8} />
          </SettingsRow>
        ))}
      </SettingsGroup>
    </div>
  )
}

function FailedNotice({ loading, onRetry }: { loading: boolean; onRetry: () => void }): JSX.Element {
  const t = useT()
  return (
    <div className={styles.failed} role="alert">
      <span style={settingsErrorTextStyle(false)}>{t('plugins.settings.failed')}</span>
      <Button size="sm" disabled={loading} onClick={onRetry}>{t('plugins.settings.retry')}</Button>
    </div>
  )
}

function SettingsItem({ item, server, state, disabled }: {
  item: McpSettingsLayoutItem
  server: PluginSettingsServer
  state: McpServerSettingsState
  disabled: boolean
}): JSX.Element | null {
  const controlId = useId()
  if (item.kind === 'tool') return <SettingsAction item={item} server={server} />
  const definition = state.definition!
  const property = definition.properties[item.property]
  if (!property) return null
  const value = state.pendingChange?.name === item.property ? state.pendingChange.value : definition.values[item.property]
  const label = property.title || item.property
  const textField = property.type === 'string' && !property.enum
  return (
    <SettingsRow
      label={label}
      description={property.description}
      htmlFor={controlId}
      orientation={textField ? 'block' : 'inline'}
      control={(
        <SettingsControl
          id={controlId}
          label={label}
          property={property}
          value={value}
          disabled={disabled}
          onChange={(next) => state.update(item.property, next)}
        />
      )}
    />
  )
}

function SettingsControl({ id, label, property, value, disabled, onChange }: {
  id: string
  label: string
  property: McpSettingsProperty
  value: McpSettingsValue
  disabled: boolean
  onChange: (value: McpSettingsValue) => void
}): JSX.Element {
  if (typeof value === 'boolean') {
    return <PillSwitch checked={value} disabled={disabled} aria-label={label} onChange={onChange} />
  }
  if (property.enum && typeof value === 'string') {
    return (
      <Select
        id={id}
        value={value}
        ariaLabel={label}
        disabled={disabled}
        options={property.enum.map((option) => ({ value: option, label: option }))}
        onValueChange={(next) => { if (next !== value) onChange(next) }}
      />
    )
  }
  return (
    <SettingsTextInput
      // Remount on a new server value so the draft follows what the server stored.
      key={`${String(value)}:${disabled}`}
      id={id}
      label={label}
      property={property}
      value={value}
      disabled={disabled}
      onChange={onChange}
    />
  )
}

function SettingsTextInput({ id, label, property, value, disabled, onChange }: {
  id: string
  label: string
  property: McpSettingsProperty
  value: McpSettingsValue
  disabled: boolean
  onChange: (value: McpSettingsValue) => void
}): JSX.Element {
  const t = useT()
  const [draft, setDraft] = useState(String(value))
  const [error, setError] = useState<McpSettingsInputError | null>(null)
  const numeric = property.type === 'number' || property.type === 'integer'
  const secret = property.writeOnly === true

  const commit = (): void => {
    if (draft === String(value)) return
    const parsed = parseSettingsInput(property, draft)
    if ('error' in parsed) {
      setError(parsed.error)
      return
    }
    setError(null)
    onChange(parsed.value)
  }

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>): void => {
    if (event.key !== 'Enter') return
    event.preventDefault()
    commit()
  }

  const errorText = error ? inputErrorText(t, error, property) : null
  return (
    <span className={numeric ? styles.numberField : styles.textField}>
      <Input
        id={id}
        className={numeric ? styles.numberInput : undefined}
        type={secret ? 'password' : numeric ? 'number' : 'text'}
        inputMode={numeric ? 'decimal' : undefined}
        autoComplete={secret ? 'new-password' : 'off'}
        spellCheck={false}
        aria-label={label}
        value={draft}
        min={property.minimum}
        max={property.maximum}
        step="any"
        placeholder={secret ? t('plugins.settings.secretPlaceholder') : undefined}
        disabled={disabled}
        invalid={errorText != null}
        aria-describedby={errorText ? `${id}-error` : undefined}
        onChange={(event) => {
          setError(null)
          setDraft(event.currentTarget.value)
        }}
        onBlur={commit}
        onKeyDown={onKeyDown}
      />
      {errorText && <span id={`${id}-error`} className={styles.inputError}>{errorText}</span>}
    </span>
  )
}

function inputErrorText(t: Translate, error: McpSettingsInputError, property: McpSettingsProperty): string {
  if (error === 'integer') return t('plugins.settings.integerRequired')
  if (error === 'range' && property.minimum !== undefined && property.maximum !== undefined) {
    return t('plugins.settings.rangeRequired', { min: property.minimum, max: property.maximum })
  }
  return t('plugins.settings.constraintRequired')
}

function SettingsAction({ item, server }: {
  item: Extract<McpSettingsLayoutItem, { kind: 'tool' }>
  server: PluginSettingsServer
}): JSX.Element {
  const t = useT()
  const [running, setRunning] = useState(false)
  const available = hasTool(server.status, item.tool) && !toolRequiresArguments(server.status, item.tool)
  const title = item.title || item.tool

  const run = (): void => {
    setRunning(true)
    void callDesktopPluginMcpTool({ server: server.status.name, tool: item.tool, arguments: {} })
      .then((result) => {
        const text = firstTextContent(result.content)
        if (result.isError) addToast(text || t('plugins.settings.actionFailed'), 'error')
        else addToast(text || t('plugins.settings.actionDone'), 'success')
      })
      .catch(() => addToast(t('plugins.settings.actionFailed'), 'error'))
      .finally(() => setRunning(false))
  }

  return (
    <SettingsRow
      label={title}
      description={item.description}
      control={<Button disabled={!available} loading={running} onClick={run}>{title}</Button>}
    />
  )
}
