import { useEffect, useId, useMemo, useRef, useState, type CSSProperties, type JSX } from 'react'
import { useT } from '../../contexts/LocaleContext'
import type { ModelCatalogItem, ReasoningEffortWire } from '../../stores/modelCatalogStore'
import { ActionTooltip } from '../ui/ActionTooltip'
import type { ShortcutSpec } from '../ui/shortcutKeys'
import { composerFooterControlBoxStyle } from './ComposerShell'

export type ReasoningQuickValue = 'default' | 'off' | ReasoningEffortWire

interface ModelPickerProps {
  modelName: string
  modelOptions: string[]
  modelCatalog?: ModelCatalogItem[]
  reasoningValue?: ReasoningQuickValue
  disabled?: boolean
  loading?: boolean
  unsupported?: boolean
  errorMessage?: string | null
  modelListReady?: boolean
  onChange?: (model: string) => void
  onReasoningChange?: (value: ReasoningQuickValue) => void
  onRetry?: () => void
  shortcut?: ShortcutSpec
  triggerStyle: CSSProperties
}

type PickerRow =
  | { kind: 'heading'; id: string; label: string }
  | { kind: 'reasoning'; id: string; value: ReasoningQuickValue; label: string; description?: string; disabled?: boolean }
  | { kind: 'model'; id: string; value: string; label: string }

export function ModelPicker({
  modelName,
  modelOptions,
  modelCatalog = [],
  reasoningValue = 'off',
  disabled = false,
  loading = false,
  unsupported = false,
  errorMessage = null,
  modelListReady = false,
  onChange,
  onReasoningChange,
  onRetry,
  shortcut,
  triggerStyle
}: ModelPickerProps): JSX.Element {
  const t = useT()
  const [open, setOpen] = useState(false)
  const [highlight, setHighlight] = useState(0)
  const [triggerActive, setTriggerActive] = useState(false)
  const wrapRef = useRef<HTMLDivElement>(null)
  const listId = useId()

  const modelChoices = useMemo(() => {
    const withDefault = ['Default', ...modelOptions.filter((option) => option !== 'Default')]
    if (!modelName || modelName === 'Default') return withDefault
    if (withDefault.includes(modelName)) return withDefault
    if (modelListReady && modelOptions.length > 0) return withDefault
    return [modelName, ...withDefault]
  }, [modelListReady, modelName, modelOptions])

  const reasoningChoices = useMemo(() => {
    const activeModel = modelCatalog.find((model) => model.id === modelName)
    const capability = activeModel?.reasoning ?? null
    const choices: Array<PickerRow & { kind: 'reasoning' }> = [
      { kind: 'reasoning', id: 'reasoning-default', value: 'default', label: t('composer.reasoning.default') }
    ]
    if (capability) {
      if (capability.supportsDisable) {
        choices.push({ kind: 'reasoning', id: 'reasoning-off', value: 'off', label: t('composer.reasoning.off') })
      }
      for (const effort of capability.supportedEfforts) {
        choices.push({
          kind: 'reasoning',
          id: `reasoning-${effort.effort}`,
          value: effort.effort,
          label: reasoningLabel(t, effort.effort),
          description: reasoningDescription(t, effort.effort)
        })
      }
      if (!capability.supportsDisable) {
        choices.push({
          kind: 'reasoning',
          id: 'reasoning-off-disabled',
          value: 'off',
          label: t('composer.reasoning.off'),
          description: t('composer.reasoning.offUnavailable'),
          disabled: true
        })
      }
      return choices
    }

    if (reasoningValue === 'off') {
      choices.push({ kind: 'reasoning', id: 'reasoning-off', value: 'off', label: t('composer.reasoning.off') })
    } else if (reasoningValue !== 'default') {
      choices.push({
        kind: 'reasoning',
        id: `reasoning-${reasoningValue}`,
        value: reasoningValue,
        label: reasoningLabel(t, reasoningValue)
      })
    }
    return choices
  }, [modelCatalog, modelName, reasoningValue, t])

  const rows = useMemo<PickerRow[]>(() => [
    { kind: 'heading', id: 'heading-thinking', label: t('composer.reasoning.heading') },
    ...reasoningChoices,
    { kind: 'heading', id: 'heading-model', label: t('composer.modelHeading') },
    ...modelChoices.map((option) => ({
      kind: 'model' as const,
      id: `model-${option}`,
      value: option,
      label: option === 'Default' ? t('composer.defaultModel') : option
    }))
  ], [modelChoices, reasoningChoices, t])

  const selectableIndexes = useMemo(
    () => rows
      .map((row, index) => row.kind === 'heading' || row.disabled ? -1 : index)
      .filter((index) => index >= 0),
    [rows]
  )
  const hasError = Boolean(errorMessage)
  const interactive = !disabled && !loading && (!unsupported || hasError) && selectableIndexes.length > 0
  const selectedIndex = Math.max(0, rows.findIndex((row) =>
    (row.kind === 'model' && row.value === modelName) ||
    (row.kind === 'reasoning' && !row.disabled && row.value === reasoningValue)
  ))

  useEffect(() => {
    setHighlight(selectableIndexes.includes(selectedIndex) ? selectedIndex : (selectableIndexes[0] ?? 0))
  }, [selectedIndex, selectableIndexes])

  useEffect(() => {
    if (!shortcut) return
    const handleShortcut = (event: KeyboardEvent): void => {
      const mod = event.ctrlKey || event.metaKey
      if (
        !mod ||
        !event.shiftKey ||
        event.altKey ||
        event.isComposing ||
        event.key.toLowerCase() !== 'm'
      ) {
        return
      }
      if (!interactive) return
      event.preventDefault()
      event.stopPropagation()
      setHighlight(selectableIndexes.includes(selectedIndex) ? selectedIndex : (selectableIndexes[0] ?? 0))
      setOpen(true)
    }

    window.addEventListener('keydown', handleShortcut, true)
    return () => {
      window.removeEventListener('keydown', handleShortcut, true)
    }
  }, [interactive, selectableIndexes, selectedIndex, shortcut])

  useEffect(() => {
    if (!open) return
    const moveHighlight = (direction: 1 | -1): void => {
      if (selectableIndexes.length === 0) return
      const currentSlot = selectableIndexes.indexOf(highlight)
      const nextSlot = currentSlot < 0
        ? 0
        : Math.max(0, Math.min(selectableIndexes.length - 1, currentSlot + direction))
      setHighlight(selectableIndexes[nextSlot])
    }
    const applyRow = (row: PickerRow | undefined): void => {
      if (!row || row.kind === 'heading' || row.disabled) return
      if (row.kind === 'model') onChange?.(row.value)
      else onReasoningChange?.(row.value)
      setOpen(false)
    }
    const handlePointerDown = (event: MouseEvent): void => {
      if (!wrapRef.current?.contains(event.target as Node)) {
        setOpen(false)
      }
    }
    const handleKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') {
        event.preventDefault()
        setOpen(false)
        return
      }
      if (!interactive || selectableIndexes.length === 0) return
      if (event.key === 'ArrowDown') {
        event.preventDefault()
        moveHighlight(1)
        return
      }
      if (event.key === 'ArrowUp') {
        event.preventDefault()
        moveHighlight(-1)
        return
      }
      if (event.key === 'Enter') {
        event.preventDefault()
        applyRow(rows[highlight])
      }
    }
    window.addEventListener('mousedown', handlePointerDown, true)
    window.addEventListener('keydown', handleKeyDown, true)
    return () => {
      window.removeEventListener('mousedown', handlePointerDown, true)
      window.removeEventListener('keydown', handleKeyDown, true)
    }
  }, [highlight, interactive, onChange, onReasoningChange, open, rows, selectableIndexes])

  const modelLabel = modelName === 'Default' ? t('composer.defaultModel') : modelName
  const reasoningDisplayLabel = reasoningQuickLabel(t, reasoningValue)
  const tooltipLabel = t('composer.selectModelTitle')
  const disabledReason = loading
    ? t('composer.modelListLoading')
    : unsupported && !hasError
      ? t('composer.modelListUnsupportedTitle')
      : undefined

  return (
    <div
      ref={wrapRef}
      style={{
        ...composerFooterControlBoxStyle,
        position: 'relative',
        minWidth: 0
      }}
    >
      <ActionTooltip
        label={tooltipLabel}
        shortcut={shortcut}
        disabledReason={disabledReason}
        placement="top"
        wrapperStyle={{ minWidth: 0 }}
      >
        <button
          type="button"
          aria-label={tooltipLabel}
          aria-haspopup={interactive ? 'listbox' : undefined}
          aria-expanded={interactive ? open : undefined}
          aria-controls={interactive && open ? listId : undefined}
          disabled={!interactive}
          onMouseEnter={() => setTriggerActive(true)}
          onMouseLeave={() => setTriggerActive(false)}
          onFocus={(event) => {
            if (event.currentTarget.matches(':focus-visible')) setTriggerActive(true)
          }}
          onBlur={() => setTriggerActive(false)}
          onClick={() => {
            if (!interactive) return
            setOpen((current) => !current)
          }}
          style={{
            ...triggerStyle,
            backgroundColor: interactive && (open || triggerActive) ? 'var(--bg-tertiary)' : 'transparent',
            cursor: interactive ? 'pointer' : 'default'
          }}
        >
          {loading ? (
            <span
              style={{
                minWidth: 0,
                overflow: 'hidden',
                textOverflow: 'ellipsis',
                whiteSpace: 'nowrap'
              }}
            >
              {t('composer.modelListLoading')}
            </span>
          ) : (
            <span
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                gap: '7px',
                minWidth: 0,
                overflow: 'hidden',
                whiteSpace: 'nowrap'
              }}
            >
              <span
                style={{
                  minWidth: 0,
                  overflow: 'hidden',
                  textOverflow: 'ellipsis',
                  whiteSpace: 'nowrap',
                  color: disabled ? 'var(--composer-footer-muted)' : 'var(--composer-footer-highlight)'
                }}
              >
                {modelLabel}
              </span>
              <span
                style={{
                  flexShrink: 0,
                  color: disabled ? 'var(--composer-footer-muted)' : 'var(--composer-footer-text)',
                  fontWeight: 'var(--type-ui-weight)'
                }}
              >
                {reasoningDisplayLabel}
              </span>
            </span>
          )}
          {interactive && (
            <span
              aria-hidden
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                justifyContent: 'center',
                width: '14px',
                height: '14px',
                flexShrink: 0,
                color: 'var(--composer-footer-muted)',
                transform: open ? 'rotate(180deg)' : 'none',
                transition: 'transform 120ms ease'
              }}
            >
              <svg width="10" height="10" viewBox="0 0 12 12" fill="none">
                <path
                  d="M3 4.5L6 7.5L9 4.5"
                  stroke="currentColor"
                  strokeWidth="1.7"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                />
              </svg>
            </span>
          )}
        </button>
      </ActionTooltip>

      {interactive && open && (
        <div
          id={listId}
          role="listbox"
          aria-label={t('composer.selectModelTitle')}
          style={{
            position: 'absolute',
            right: 0,
            bottom: 'calc(100% + 8px)',
            minWidth: '240px',
            maxWidth: '320px',
            maxHeight: '320px',
            overflowY: 'auto',
            zIndex: 70,
            border: '1px solid var(--glass-border)',
            borderRadius: '12px',
            background: 'var(--glass-surface-strong)',
            boxShadow: 'var(--glass-shadow-soft)',
            backdropFilter: 'var(--glass-blur)',
            WebkitBackdropFilter: 'var(--glass-blur)',
            padding: '6px'
          }}
        >
          {errorMessage && (
            <div
              role="status"
              aria-live="polite"
              style={{
                padding: '8px 10px',
                marginBottom: '4px',
                borderRadius: '10px',
                background: 'rgba(220, 38, 38, 0.08)',
                color: 'var(--error)',
                fontSize: '12px',
                lineHeight: 1.4
              }}
            >
              <div style={{ fontWeight: 600 }}>{t('composer.modelListError')}</div>
              <div style={{ color: 'var(--text-secondary)', marginTop: '2px' }}>{errorMessage}</div>
              {onRetry && (
                <button
                  type="button"
                  onClick={(event) => {
                    event.stopPropagation()
                    onRetry()
                  }}
                  style={{
                    marginTop: '6px',
                    border: 'none',
                    background: 'transparent',
                    color: 'var(--accent)',
                    padding: 0,
                    cursor: 'pointer',
                    fontSize: '12px',
                    fontWeight: 600
                  }}
                >
                  {t('composer.modelListRetry')}
                </button>
              )}
            </div>
          )}
          {rows.map((row, index) => {
            if (row.kind === 'heading') {
              return (
                <div
                  key={row.id}
                  style={{
                    padding: index === 0 ? '6px 10px 4px' : '10px 10px 4px',
                    color: 'var(--text-dimmed)',
                    fontSize: '11px',
                    fontWeight: 700,
                    letterSpacing: 0,
                    textTransform: 'uppercase'
                  }}
                >
                  {row.label}
                </div>
              )
            }

            const selected = row.kind === 'model'
              ? row.value === modelName
              : !row.disabled && row.value === reasoningValue
            const highlighted = index === highlight
            return (
              <button
                key={row.id}
                type="button"
                role="option"
                aria-selected={selected}
                disabled={row.disabled}
                onMouseEnter={() => {
                  if (!row.disabled) setHighlight(index)
                }}
                onClick={() => {
                  if (row.disabled) return
                  if (row.kind === 'model') onChange?.(row.value)
                  else onReasoningChange?.(row.value)
                  setOpen(false)
                }}
                style={{
                  width: '100%',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  gap: '10px',
                  border: 'none',
                  borderRadius: '10px',
                  padding: '8px 10px',
                  background: highlighted ? 'var(--bg-tertiary)' : 'transparent',
                  color: row.disabled
                    ? 'var(--text-dimmed)'
                    : selected ? 'var(--text-primary)' : 'var(--text-secondary)',
                  cursor: row.disabled ? 'not-allowed' : 'pointer',
                  textAlign: 'left',
                  fontSize: 'var(--type-secondary-size)',
                  lineHeight: 'var(--type-secondary-line-height)'
                }}
              >
                <span style={{ minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                  {row.label}
                  {row.kind === 'reasoning' && row.description && (
                    <span style={{ display: 'block', color: 'var(--text-dimmed)', fontSize: '11px', lineHeight: 1.25 }}>
                      {row.description}
                    </span>
                  )}
                </span>
                {selected && (
                  <span
                    aria-hidden
                    style={{
                      width: '7px',
                      height: '7px',
                      borderRadius: '999px',
                      background: 'var(--accent)',
                      flexShrink: 0
                    }}
                  />
                )}
              </button>
            )
          })}
        </div>
      )}
    </div>
  )
}

function reasoningQuickLabel(t: ReturnType<typeof useT>, value: ReasoningQuickValue): string {
  if (value === 'default') return t('composer.reasoning.default')
  if (value === 'off') return t('composer.reasoning.off')
  return reasoningLabel(t, value)
}

function reasoningLabel(t: ReturnType<typeof useT>, value: ReasoningEffortWire): string {
  switch (value) {
    case 'low': return t('composer.reasoning.low')
    case 'medium': return t('composer.reasoning.medium')
    case 'high': return t('composer.reasoning.high')
    case 'extraHigh': return t('composer.reasoning.extraHigh')
  }
}

function reasoningDescription(t: ReturnType<typeof useT>, value: ReasoningEffortWire): string {
  switch (value) {
    case 'low': return t('composer.reasoning.low.description')
    case 'medium': return t('composer.reasoning.medium.description')
    case 'high': return t('composer.reasoning.high.description')
    case 'extraHigh': return t('composer.reasoning.extraHigh.description')
  }
}
