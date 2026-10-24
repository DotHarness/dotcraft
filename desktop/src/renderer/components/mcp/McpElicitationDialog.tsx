import { useEffect, useMemo, useRef, useState } from 'react'
import { translate } from '../../../shared/locales'
import { useLocale } from '../../contexts/LocaleContext'
import { Button, ButtonLabel } from '../ui/Button'
import { Checkbox } from '../ui/Checkbox'
import { Input } from '../ui/Input'
import { ModalDialog, ModalFooter } from '../ui/ModalDialog'
import { Select } from '../ui/Select'
import styles from './McpElicitationDialog.module.css'

export interface McpElicitationRequest {
  bridgeId: string
  serverName: string
  mode: 'form' | 'url'
  message?: string
  url?: string
  requestedSchema?: unknown
}

interface FormField {
  name: string
  type: 'string' | 'number' | 'integer' | 'boolean' | 'array'
  title: string
  description?: string
  required: boolean
  options?: Array<{ value: string; label: string }>
  defaultValue?: FormValue
  minLength?: number
  maxLength?: number
  minimum?: number
  maximum?: number
  minItems?: number
  maxItems?: number
  pattern?: string
  format?: 'email' | 'uri' | 'date' | 'date-time'
}

type FormValue = string | number | boolean | string[]

export interface McpElicitationResponse {
  action: 'accept' | 'decline' | 'cancel'
  content?: Record<string, FormValue>
}

interface Props {
  request: McpElicitationRequest
  onRespond: (response: McpElicitationResponse) => void
}

export function McpElicitationDialog({ request, onRespond }: Props): JSX.Element {
  const locale = useLocale()
  const cancelRef = useRef<HTMLButtonElement>(null)
  const fields = useMemo(() => parseSchema(request.requestedSchema), [request.requestedSchema])
  const safeUrl = request.mode === 'url' ? normalizeSafeUrl(request.url) : null
  const [values, setValues] = useState<Record<string, FormValue>>(() => initialValues(fields))

  useEffect(() => {
    cancelRef.current?.focus()
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') onRespond({ action: 'cancel' })
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [onRespond])

  const valid = request.mode === 'url'
    ? safeUrl != null
    : fields != null && validateValues(fields, values)

  const unsupported = request.mode === 'url' ? safeUrl == null : fields == null

  return (
    <ModalDialog
      titleId="mcp-elicitation-title"
      width={440}
      scrimStyle={{ zIndex: 10020 }}
      bodyClassName={styles.body}
      header={(
        <h2 id="mcp-elicitation-title" className={styles.title}>
          {translate(locale, 'mcp.elicitation.title')}
        </h2>
      )}
      error={unsupported && translate(locale, 'mcp.elicitation.unsupported')}
      footer={(
        <ModalFooter>
          <Button ref={cancelRef} variant="ghost" onClick={() => onRespond({ action: 'cancel' })}>
            {translate(locale, 'mcp.elicitation.cancel')}
          </Button>
          <Button variant="secondary" onClick={() => onRespond({ action: 'decline' })}>
            {translate(locale, 'mcp.elicitation.decline')}
          </Button>
          <Button
            variant="primary"
            disabled={!valid}
            onClick={() => onRespond({
              action: 'accept',
              ...(request.mode === 'form' && fields != null ? { content: acceptedContent(fields, values) } : {})
            })}
          >
            {translate(locale, 'mcp.elicitation.accept')}
          </Button>
        </ModalFooter>
      )}
    >
      <p className={styles.message}>
        {request.message || translate(locale, 'mcp.elicitation.message', { server: request.serverName })}
      </p>
      {safeUrl != null ? (
        <Button
          variant="outline"
          className={styles.urlButton}
          onClick={() => void window.api.shell.openExternal(safeUrl)}
        >
          <span className={styles.urlLabel}>
            <ButtonLabel>{translate(locale, 'mcp.elicitation.openUrl')}</ButtonLabel>
          </span>
        </Button>
      ) : request.mode === 'form' && fields != null && (
        <div className={styles.fields}>
          {fields.map((field) => (
            <div key={field.name} className={styles.field}>
              <span>{field.title}{field.required ? ' *' : ''}</span>
              {field.type === 'boolean' ? (
                <Checkbox
                  checked={Boolean(values[field.name])}
                  ariaLabel={field.title}
                  onChange={(checked) => setValues((current) => ({ ...current, [field.name]: checked }))}
                />
              ) : field.type === 'array' && field.options ? (
                <span className={styles.options}>
                  {field.options.map((option) => {
                    const selected = Array.isArray(values[field.name]) ? values[field.name] as string[] : []
                    return (
                      <Checkbox
                        key={option.value}
                        checked={selected.includes(option.value)}
                        label={option.label}
                        onChange={(checked) => setValues((current) => ({
                          ...current,
                          [field.name]: checked
                            ? [...selected, option.value]
                            : selected.filter((value) => value !== option.value)
                        }))}
                      />
                    )
                  })}
                </span>
              ) : field.options ? (
                <Select
                  value={String(values[field.name] ?? '')}
                  ariaLabel={field.title}
                  onValueChange={(nextValue) => setValues((current) => ({
                    ...current,
                    [field.name]: coerceValue(field, nextValue)
                  }))}
                  options={[
                    ...(!field.required ? [{ value: '', label: '' }] : []),
                    ...field.options
                  ]}
                />
              ) : (
                <Input
                  type={field.type === 'string' ? 'text' : 'number'}
                  className={field.type === 'string' ? undefined : 'dc-plain-number'}
                  aria-label={field.title}
                  value={String(values[field.name] ?? '')}
                  minLength={field.minLength}
                  maxLength={field.maxLength}
                  min={field.minimum}
                  max={field.maximum}
                  required={field.required}
                  onChange={(event) => setValues((current) => ({ ...current, [field.name]: coerceValue(field, event.target.value) }))}
                />
              )}
              {field.description && <span className={styles.description}>{field.description}</span>}
            </div>
          ))}
        </div>
      )}
    </ModalDialog>
  )
}

function parseSchema(value: unknown): FormField[] | null {
  if (!isRecord(value) || !hasOnlyKeys(value, ['type', 'properties', 'required']) || value.type !== 'object' || !isRecord(value.properties)) return null
  if (value.required != null && (!Array.isArray(value.required) || value.required.some((item) => typeof item !== 'string'))) return null
  const required = new Set((value.required as string[] | undefined) ?? [])
  const properties = value.properties
  if ([...required].some((name) => !(name in properties))) return null
  const fields: FormField[] = []
  for (const [name, raw] of Object.entries(properties)) {
    if (!isRecord(raw) || !['string', 'number', 'integer', 'boolean', 'array'].includes(String(raw.type))) return null
    const type = raw.type as FormField['type']
    const common = ['type', 'title', 'description', 'default']
    let options: FormField['options']
    if (type === 'string') {
      if (!hasOnlyKeys(raw, [...common, 'minLength', 'maxLength', 'pattern', 'format', 'enum', 'enumNames', 'oneOf'])) return null
      options = parseSingleSelectOptions(raw)
      if ((raw.enum != null || raw.enumNames != null || raw.oneOf != null) && options == null) return null
      if (raw.format != null && !['email', 'uri', 'date', 'date-time'].includes(String(raw.format))) return null
      if (raw.pattern != null) {
        if (typeof raw.pattern !== 'string') return null
        try { void new RegExp(raw.pattern) } catch { return null }
      }
    } else if (type === 'number' || type === 'integer') {
      if (!hasOnlyKeys(raw, [...common, 'minimum', 'maximum'])) return null
    } else if (type === 'boolean') {
      if (!hasOnlyKeys(raw, common)) return null
    } else {
      if (!hasOnlyKeys(raw, [...common, 'minItems', 'maxItems', 'items'])) return null
      options = parseMultiSelectOptions(raw.items)
      if (options == null) return null
    }
    if (!validConstraints(raw, type, options)) return null
    fields.push({
      name,
      type,
      title: typeof raw.title === 'string' ? raw.title : name,
      description: typeof raw.description === 'string' ? raw.description : undefined,
      required: required.has(name),
      options,
      defaultValue: isFormValue(raw.default) ? raw.default : undefined,
      minLength: typeof raw.minLength === 'number' ? raw.minLength : undefined,
      maxLength: typeof raw.maxLength === 'number' ? raw.maxLength : undefined,
      minimum: typeof raw.minimum === 'number' ? raw.minimum : undefined,
      maximum: typeof raw.maximum === 'number' ? raw.maximum : undefined,
      minItems: typeof raw.minItems === 'number' ? raw.minItems : undefined,
      maxItems: typeof raw.maxItems === 'number' ? raw.maxItems : undefined,
      pattern: typeof raw.pattern === 'string' ? raw.pattern : undefined,
      format: typeof raw.format === 'string' ? raw.format as FormField['format'] : undefined
    })
  }
  return fields
}

function initialValues(fields: FormField[] | null): Record<string, FormValue> {
  if (fields == null) return {}
  return Object.fromEntries(fields.map((field) => [field.name, field.defaultValue ?? (field.type === 'boolean' ? false : field.type === 'array' ? [] : '')]))
}

function validateValues(fields: FormField[], values: Record<string, FormValue>): boolean {
  return fields.every((field) => {
    const value = values[field.name]
    if (field.required && (value === '' || value == null || Array.isArray(value) && value.length === 0)) return false
    if (!field.required && value === '') return true
    if (field.type === 'string' && typeof value !== 'string') return false
    if ((field.type === 'number' || field.type === 'integer') && typeof value !== 'number') return false
    if (field.type === 'boolean' && typeof value !== 'boolean') return false
    if (field.type === 'array' && !Array.isArray(value)) return false
    if (field.type === 'integer' && typeof value === 'number' && !Number.isInteger(value)) return false
    if (typeof value === 'string' && field.minLength != null && value.length < field.minLength) return false
    if (typeof value === 'string' && field.maxLength != null && value.length > field.maxLength) return false
    if (typeof value === 'string' && field.pattern != null && !new RegExp(field.pattern).test(value)) return false
    if (typeof value === 'string' && !matchesFormat(field.format, value)) return false
    if (typeof value === 'number' && field.minimum != null && value < field.minimum) return false
    if (typeof value === 'number' && field.maximum != null && value > field.maximum) return false
    if (Array.isArray(value) && field.minItems != null && value.length < field.minItems) return false
    if (Array.isArray(value) && field.maxItems != null && value.length > field.maxItems) return false
    if (field.options && (Array.isArray(value)
      ? value.some((item) => !field.options!.some((option) => option.value === item))
      : typeof value === 'string' && !field.options.some((option) => option.value === value))) return false
    return true
  })
}

function acceptedContent(fields: FormField[], values: Record<string, FormValue>): Record<string, FormValue> {
  return Object.fromEntries(fields.flatMap((field) => {
    const value = values[field.name]
    return !field.required && value === '' ? [] : [[field.name, value]]
  }))
}

function parseSingleSelectOptions(raw: Record<string, unknown>): FormField['options'] {
  if (raw.enum != null && raw.oneOf != null) return undefined
  if (raw.enum != null) {
    if (!isUniqueStringArray(raw.enum)) return undefined
    if (raw.enumNames != null && (!isUniqueStringArray(raw.enumNames) || raw.enumNames.length !== raw.enum.length)) return undefined
    return raw.enum.map((value, index) => ({
      value,
      label: Array.isArray(raw.enumNames) ? String(raw.enumNames[index]) : value
    }))
  }
  if (raw.enumNames != null) return undefined
  if (raw.oneOf != null) return parseTitledOptions(raw.oneOf)
  return undefined
}

function parseMultiSelectOptions(raw: unknown): FormField['options'] {
  if (!isRecord(raw)) return undefined
  if (hasOnlyKeys(raw, ['type', 'enum']) && raw.type === 'string' && isUniqueStringArray(raw.enum)) {
    return raw.enum.map((value) => ({ value, label: value }))
  }
  if (hasOnlyKeys(raw, ['anyOf'])) return parseTitledOptions(raw.anyOf)
  return undefined
}

function parseTitledOptions(raw: unknown): FormField['options'] {
  if (!Array.isArray(raw) || raw.length === 0) return undefined
  const options: NonNullable<FormField['options']> = []
  for (const item of raw) {
    if (!isRecord(item) || !hasOnlyKeys(item, ['const', 'title']) || typeof item.const !== 'string' || typeof item.title !== 'string' || !item.title) return undefined
    options.push({ value: item.const, label: item.title })
  }
  return new Set(options.map((option) => option.value)).size === options.length ? options : undefined
}

function validConstraints(raw: Record<string, unknown>, type: FormField['type'], options: FormField['options']): boolean {
  for (const key of ['minLength', 'maxLength', 'minItems', 'maxItems']) {
    if (raw[key] != null && (!Number.isInteger(raw[key]) || Number(raw[key]) < 0)) return false
  }
  for (const key of ['minimum', 'maximum']) {
    if (raw[key] != null && (typeof raw[key] !== 'number' || !Number.isFinite(raw[key]))) return false
  }
  if (typeof raw.minLength === 'number' && typeof raw.maxLength === 'number' && raw.minLength > raw.maxLength) return false
  if (typeof raw.minItems === 'number' && typeof raw.maxItems === 'number' && raw.minItems > raw.maxItems) return false
  if (typeof raw.minimum === 'number' && typeof raw.maximum === 'number' && raw.minimum > raw.maximum) return false
  if (raw.default == null) return true
  if (type === 'string') return typeof raw.default === 'string' && (!options || options.some((option) => option.value === raw.default))
  if (type === 'number') return typeof raw.default === 'number' && Number.isFinite(raw.default)
  if (type === 'integer') return typeof raw.default === 'number' && Number.isInteger(raw.default)
  if (type === 'boolean') return typeof raw.default === 'boolean'
  return isUniqueStringArray(raw.default) && raw.default.every((value) => options?.some((option) => option.value === value))
}

function matchesFormat(format: FormField['format'], value: string): boolean {
  if (format == null) return true
  if (format === 'email') return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value)
  if (format === 'uri') {
    try { return Boolean(new URL(value)) } catch { return false }
  }
  if (format === 'date') return /^\d{4}-\d{2}-\d{2}$/.test(value) && !Number.isNaN(Date.parse(`${value}T00:00:00Z`))
  return !Number.isNaN(Date.parse(value))
}

function coerceValue(field: FormField, value: string): FormValue {
  if (field.type === 'string' || value === '') return value
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : value
}

function normalizeSafeUrl(value: string | undefined): string | null {
  try {
    if (!value) return null
    const url = new URL(value)
    const loopback = url.hostname === '127.0.0.1' || url.hostname === 'localhost' || url.hostname === '::1'
    return url.protocol === 'https:' || (url.protocol === 'http:' && loopback) ? url.href : null
  } catch {
    return null
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value != null && !Array.isArray(value)
}

function hasOnlyKeys(value: Record<string, unknown>, allowed: string[]): boolean {
  const keySet = new Set(allowed)
  return Object.keys(value).every((key) => keySet.has(key))
}

function isUniqueStringArray(value: unknown): value is string[] {
  return Array.isArray(value) && value.length > 0 && value.every((item) => typeof item === 'string') && new Set(value).size === value.length
}

function isFormValue(value: unknown): value is FormValue {
  return typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean'
    || Array.isArray(value) && value.every((item) => typeof item === 'string')
}
