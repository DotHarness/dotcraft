import type { Dispatch, JSX, SetStateAction } from 'react'
import { Plus, X } from 'lucide-react'
import { useT } from '../../../contexts/LocaleContext'
import { Button } from '../../ui/Button'
import { IconButton } from '../../ui/IconButton'
import { Input } from '../../ui/Input'
import styles from './EditableList.module.css'

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
  addLabel,
  removeLabel
}: EditableValueListProps): JSX.Element {
  const t = useT()

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
    <div className={styles.list}>
      {rows.map((row) => (
        <div key={row.id} className={styles.valueRow}>
          <Input
            mono
            value={row.value}
            onChange={(e) => updateRow(row.id, e.target.value)}
            placeholder={placeholder}
          />
          <RemoveRowButton
            label={removeLabel ?? t('settings.list.remove')}
            disabled={rows.length <= 1 && row.value.trim().length === 0}
            onClick={() => removeRow(row.id)}
          />
        </div>
      ))}
      <AddRowButton label={addLabel ?? t('settings.list.add')} onClick={addRow} />
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
  addLabel,
  removeLabel
}: EditableKeyValueListProps): JSX.Element {
  const t = useT()

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
    <div className={styles.list}>
      {rows.map((row) => (
        <div key={row.id} className={styles.keyValueRow}>
          <Input
            mono
            value={row.key}
            onChange={(e) => updateRow(row.id, e.target.value, row.value)}
            placeholder={keyPlaceholder}
          />
          <Input
            mono
            value={row.value}
            onChange={(e) => updateRow(row.id, row.key, e.target.value)}
            placeholder={valuePlaceholder}
          />
          <RemoveRowButton
            label={removeLabel ?? t('settings.list.remove')}
            disabled={rows.length <= 1 && row.key.trim().length === 0 && row.value.trim().length === 0}
            onClick={() => removeRow(row.id)}
          />
        </div>
      ))}
      <AddRowButton label={addLabel ?? t('settings.list.add')} onClick={addRow} />
    </div>
  )
}

function RemoveRowButton({
  label,
  disabled,
  onClick
}: {
  label: string
  disabled: boolean
  onClick: () => void
}): JSX.Element {
  return <IconButton icon={<X size={14} aria-hidden />} label={label} disabled={disabled} onClick={onClick} />
}

function AddRowButton({ label, onClick }: { label: string; onClick: () => void }): JSX.Element {
  return (
    <Button
      variant="ghost"
      iconLeft={<Plus size={14} aria-hidden />}
      onClick={onClick}
      className={styles.add}
    >
      {label}
    </Button>
  )
}
