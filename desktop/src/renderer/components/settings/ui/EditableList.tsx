import type { CSSProperties, Dispatch, JSX, SetStateAction } from 'react'

export interface ValueRow {
  id: string
  value: string
}

export interface KeyValueRow {
  id: string
  key: string
  value: string
}

export function createRowId(prefix: string): string {
  return `${prefix}-${Math.random().toString(36).slice(2, 10)}`
}

export function normalizeValueRows(values?: string[] | null): ValueRow[] {
  if (!values || values.length === 0) return [{ id: createRowId('value'), value: '' }]
  return values.map((value) => ({ id: createRowId('value'), value }))
}

export function normalizeKeyValueRows(values?: Record<string, string> | null): KeyValueRow[] {
  const entries = Object.entries(values ?? {})
  if (entries.length === 0) {
    return [{ id: createRowId('kv'), key: '', value: '' }]
  }
  return entries.map(([key, value]) => ({
    id: createRowId('kv'),
    key,
    value
  }))
}

export function rowsToValues(rows: ValueRow[]): string[] {
  return rows.map((row) => row.value.trim()).filter((value) => value.length > 0)
}

export function rowsToRecord(rows: KeyValueRow[]): Record<string, string> {
  const record: Record<string, string> = {}
  for (const row of rows) {
    const key = row.key.trim()
    const value = row.value.trim()
    if (key.length > 0 && value.length > 0) {
      record[key] = value
    }
  }
  return record
}

interface EditableValueListProps {
  rows: ValueRow[]
  setRows: Dispatch<SetStateAction<ValueRow[]>>
  placeholder: string
  addLabel?: string
  removeLabel?: string
}

export function EditableValueList({
  rows,
  setRows,
  placeholder,
  addLabel = '+ Add',
  removeLabel = 'Remove'
}: EditableValueListProps): JSX.Element {
  function updateRow(id: string, value: string): void {
    setRows((prev) => prev.map((row) => (row.id === id ? { ...row, value } : row)))
  }

  function addRow(): void {
    setRows((prev) => [...prev, { id: createRowId('value'), value: '' }])
  }

  function removeRow(id: string): void {
    setRows((prev) =>
      prev.length <= 1 ? [{ id: createRowId('value'), value: '' }] : prev.filter((row) => row.id !== id)
    )
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
      {rows.map((row) => (
        <div key={row.id} style={{ display: 'grid', gridTemplateColumns: '1fr auto', gap: '8px' }}>
          <input
            type="text"
            value={row.value}
            onChange={(e) => updateRow(row.id, e.target.value)}
            placeholder={placeholder}
            style={inputStyle(true)}
          />
          <button type="button" onClick={() => removeRow(row.id)} style={secondaryButtonStyle()}>
            {removeLabel}
          </button>
        </div>
      ))}
      <button type="button" onClick={addRow} style={secondaryButtonStyle()}>
        {addLabel}
      </button>
    </div>
  )
}

interface EditableKeyValueListProps {
  rows: KeyValueRow[]
  setRows: Dispatch<SetStateAction<KeyValueRow[]>>
  keyPlaceholder: string
  valuePlaceholder: string
  addLabel?: string
  removeLabel?: string
}

export function EditableKeyValueList({
  rows,
  setRows,
  keyPlaceholder,
  valuePlaceholder,
  addLabel = '+ Add',
  removeLabel = 'Remove'
}: EditableKeyValueListProps): JSX.Element {
  function updateRow(id: string, nextKey: string, nextValue: string): void {
    setRows((prev) =>
      prev.map((row) => (row.id === id ? { ...row, key: nextKey, value: nextValue } : row))
    )
  }

  function addRow(): void {
    setRows((prev) => [...prev, { id: createRowId('kv'), key: '', value: '' }])
  }

  function removeRow(id: string): void {
    setRows((prev) =>
      prev.length <= 1 ? [{ id: createRowId('kv'), key: '', value: '' }] : prev.filter((row) => row.id !== id)
    )
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
      {rows.map((row) => (
        <div key={row.id} style={{ display: 'grid', gridTemplateColumns: '1fr 1fr auto', gap: '8px' }}>
          <input
            type="text"
            value={row.key}
            onChange={(e) => updateRow(row.id, e.target.value, row.value)}
            placeholder={keyPlaceholder}
            style={inputStyle(true)}
          />
          <input
            type="text"
            value={row.value}
            onChange={(e) => updateRow(row.id, row.key, e.target.value)}
            placeholder={valuePlaceholder}
            style={inputStyle(true)}
          />
          <button type="button" onClick={() => removeRow(row.id)} style={secondaryButtonStyle()}>
            {removeLabel}
          </button>
        </div>
      ))}
      <button type="button" onClick={addRow} style={secondaryButtonStyle()}>
        {addLabel}
      </button>
    </div>
  )
}

function inputStyle(mono = false): CSSProperties {
  return {
    width: '100%',
    boxSizing: 'border-box',
    padding: '8px 10px',
    fontSize: '13px',
    borderRadius: '8px',
    border: '1px solid var(--border-default)',
    background: 'var(--bg-primary)',
    color: 'var(--text-primary)',
    outline: 'none',
    fontFamily: mono ? 'var(--font-mono)' : undefined
  }
}

function secondaryButtonStyle(): CSSProperties {
  return {
    padding: '8px 14px',
    border: '1px solid var(--border-default)',
    borderRadius: '8px',
    background: 'transparent',
    color: 'var(--text-primary)',
    fontSize: '13px',
    fontWeight: 500,
    cursor: 'pointer'
  }
}
