import { useId, useState, type FocusEvent, type JSX, type KeyboardEvent, type ReactNode } from 'react'
import type { DesktopPluginSettingField } from '@dotcraft/plugin'
import { useT } from '../../../contexts/LocaleContext'
import { SettingsGroup, SettingsRow } from '../../settings/SettingsGroup'
import { settingsHintStyle } from '../../settings/settingsTypography'
import {
  EditableKeyValueList,
  EditableValueList,
  normalizeKeyValueRows,
  normalizeValueRows,
  rowsToRecord,
  rowsToValues
} from '../../settings/ui/EditableList'
import { Input, Textarea } from '../../ui/Input'
import { PillSwitch } from '../../ui/PillSwitch'
import { Select } from '../../ui/Select'
import {
  configFieldLabel,
  formatConfigInput,
  parseConfigInput,
  storedKey,
  type PluginConfigInputError
} from './pluginConfigModel'
import { FailedNotice } from './SettingsStates'
import type { PluginConfigState } from './usePluginConfig'
import styles from './PluginSettings.module.css'

type Translate = ReturnType<typeof useT>

const BLOCK_TYPES = new Set(['text', 'textarea', 'json', 'stringList', 'keyValueMap'])

export function PluginConfigSettings({ state }: { state: PluginConfigState }): JSX.Element {
  const t = useT()
  const { snapshot } = state
  if (!snapshot) {
    return (
      <SettingsGroup>
        <SettingsRow>
          <FailedNotice loading={state.loading} onRetry={state.retry} />
        </SettingsRow>
      </SettingsGroup>
    )
  }
  const writable = snapshot.writableScopes.includes('personal')
  return (
    <>
      {(state.failed || !writable) && (
        <div className={styles.notice}>
          {state.failed
            ? <FailedNotice loading={state.loading} onRetry={state.retry} />
            : <div style={settingsHintStyle(false)} role="alert">{t('plugins.settings.personalUnavailable')}</div>}
        </div>
      )}
      <SettingsGroup>
        {snapshot.schema.fields.map((field) => (
          <ConfigFieldRow
            key={field.key}
            field={field}
            state={state}
            disabled={!writable || state.failed || state.pendingChange != null}
          />
        ))}
      </SettingsGroup>
    </>
  )
}

function ConfigFieldRow({ field, state, disabled }: {
  field: DesktopPluginSettingField
  state: PluginConfigState
  disabled: boolean
}): JSX.Element {
  const t = useT()
  const controlId = useId()
  const snapshot = state.snapshot!
  const setByWorkspace = storedKey(snapshot.workspace, field.key) !== undefined
  const pending = state.pendingChange?.key === field.key
  const value = pending && state.pendingChange?.value !== undefined
    ? state.pendingChange.value
    : snapshot.value[storedKey(snapshot.value, field.key) ?? field.key]
  const label = configFieldLabel(field)
  const description = setByWorkspace
    ? <>{field.description && <span className={styles.fieldDescription}>{field.description}</span>}{t('plugins.settings.setByWorkspace')}</>
    : field.description
  const block = BLOCK_TYPES.has(field.type)
  const list = field.type === 'stringList' || field.type === 'keyValueMap'
  const control = (
    <ConfigControl
      id={controlId}
      label={label}
      field={field}
      value={value}
      disabled={disabled || setByWorkspace}
      onChange={(next) => state.set(field.key, next)}
    />
  )
  return (
    <SettingsRow
      label={label}
      description={description}
      htmlFor={list ? undefined : controlId}
      orientation={block ? 'block' : 'inline'}
      control={control}
    />
  )
}

interface ControlProps {
  id: string
  label: string
  field: DesktopPluginSettingField
  value: unknown
  disabled: boolean
  onChange: (value: unknown) => void
}

function ConfigControl(props: ControlProps): JSX.Element {
  const { id, label, field, value, disabled, onChange } = props
  // Remount on a new stored value so a draft follows what was saved.
  const key = `${JSON.stringify(value) ?? ''}:${disabled}`
  switch (field.type) {
    case 'bool':
      return <PillSwitch checked={value === true} disabled={disabled} aria-label={label} onChange={onChange} />
    case 'select':
      return (
        <Select
          id={id}
          value={(value as string | undefined) ?? ''}
          ariaLabel={label}
          disabled={disabled}
          options={(field.options ?? []).map((option) => ({ value: option, label: option }))}
          onValueChange={(next) => { if (next !== value) onChange(next) }}
        />
      )
    case 'stringList':
      return <StringListControl key={key} {...props} />
    case 'keyValueMap':
      return <KeyValueMapControl key={key} {...props} />
    default:
      return <TextControl key={key} {...props} />
  }
}

function TextControl({ id, label, field, value, disabled, onChange }: ControlProps): JSX.Element {
  const t = useT()
  const stored = formatConfigInput(field, value)
  const [draft, setDraft] = useState(stored)
  const [error, setError] = useState<PluginConfigInputError | null>(null)
  const numeric = field.type === 'number'
  const multiline = field.type === 'textarea' || field.type === 'json'

  const commit = (): void => {
    if (draft === stored) return
    const parsed = parseConfigInput(field, draft)
    if ('error' in parsed) {
      setError(parsed.error)
      return
    }
    setError(null)
    onChange(parsed.value)
  }

  const errorText = error ? inputErrorText(t, error, field) : null
  const shared = {
    id,
    'aria-label': label,
    value: draft,
    disabled,
    invalid: errorText != null,
    spellCheck: false,
    'aria-describedby': errorText ? `${id}-error` : undefined,
    onBlur: commit
  }
  return (
    <span className={numeric ? styles.numberField : styles.textField}>
      {multiline
        ? (
          <Textarea
            {...shared}
            mono={field.type === 'json'}
            rows={field.type === 'json' ? 6 : 4}
            onChange={(event) => {
              setError(null)
              setDraft(event.currentTarget.value)
            }}
          />
        )
        : (
          <Input
            {...shared}
            className={numeric ? styles.numberInput : undefined}
            type={numeric ? 'number' : 'text'}
            inputMode={numeric ? 'decimal' : undefined}
            autoComplete="off"
            min={field.min}
            max={field.max}
            step="any"
            onChange={(event) => {
              setError(null)
              setDraft(event.currentTarget.value)
            }}
            onKeyDown={(event: KeyboardEvent<HTMLInputElement>) => {
              if (event.key !== 'Enter') return
              event.preventDefault()
              commit()
            }}
          />
        )}
      {errorText && <span id={`${id}-error`} className={styles.inputError}>{errorText}</span>}
    </span>
  )
}

function inputErrorText(t: Translate, error: PluginConfigInputError, field: DesktopPluginSettingField): string {
  if (error === 'json') return t('plugins.settings.jsonRequired')
  if (error === 'range' && field.min !== undefined && field.max !== undefined) {
    return t('plugins.settings.rangeRequired', { min: field.min, max: field.max })
  }
  if (error === 'range' && field.min !== undefined) return t('plugins.settings.minRequired', { min: field.min })
  if (error === 'range' && field.max !== undefined) return t('plugins.settings.maxRequired', { max: field.max })
  return t('plugins.settings.numberRequired')
}

function CommitOnLeave({ label, onLeave, children }: {
  label: string
  onLeave: () => void
  children: ReactNode
}): JSX.Element {
  const onBlur = (event: FocusEvent<HTMLDivElement>): void => {
    if (!event.currentTarget.contains(event.relatedTarget)) onLeave()
  }
  return <div role="group" aria-label={label} className={styles.listField} onBlur={onBlur}>{children}</div>
}

function StringListControl({ label, value, disabled, onChange }: ControlProps): JSX.Element {
  const t = useT()
  const stored = (value as string[] | undefined) ?? []
  const [initial] = useState(() => normalizeValueRows(stored))
  const [rows, setRows] = useState(initial)
  const commit = (): void => {
    if (rows === initial) return
    const next = rowsToValues(rows)
    if (JSON.stringify(next) !== JSON.stringify(stored)) onChange(next)
  }
  return (
    <CommitOnLeave label={label} onLeave={commit}>
      <fieldset disabled={disabled} className={styles.fieldset}>
        <EditableValueList rows={rows} setRows={setRows} placeholder={t('plugins.settings.valuePlaceholder')} />
      </fieldset>
    </CommitOnLeave>
  )
}

function KeyValueMapControl({ label, value, disabled, onChange }: ControlProps): JSX.Element {
  const t = useT()
  const stored = (value as Record<string, string> | undefined) ?? {}
  const [initial] = useState(() => normalizeKeyValueRows(stored))
  const [rows, setRows] = useState(initial)
  const commit = (): void => {
    if (rows === initial) return
    const next = rowsToRecord(rows)
    if (JSON.stringify(next) !== JSON.stringify(stored)) onChange(next)
  }
  return (
    <CommitOnLeave label={label} onLeave={commit}>
      <fieldset disabled={disabled} className={styles.fieldset}>
        <EditableKeyValueList
          rows={rows}
          setRows={setRows}
          keyPlaceholder={t('plugins.settings.keyPlaceholder')}
          valuePlaceholder={t('plugins.settings.valuePlaceholder')}
        />
      </fieldset>
    </CommitOnLeave>
  )
}
