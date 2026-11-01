import { useCallback, useEffect, useId, useLayoutEffect, useMemo, useRef, useState, type JSX } from 'react'
import { createPortal } from 'react-dom'
import { Plus, X } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { LayerBoundary } from '../../contexts/LayerContext'
import { Input } from './Input'
import { SelectMenuOption } from './SelectMenu'
import { useDismissOnViewportChange } from './anchoredPopup'
import styles from './TokenPicker.module.css'

export interface TokenPickerOption {
  value: string
  label: string
}

export function TokenPicker({ values, options, onValuesChange, ariaLabel, placeholder, addLabel, ordered = false }: {
  values: readonly string[]
  options: readonly TokenPickerOption[]
  onValuesChange: (values: string[]) => void
  ariaLabel: string
  placeholder: string
  addLabel: string
  ordered?: boolean
}): JSX.Element {
  const t = useT()
  const id = useId()
  const root = useRef<HTMLDivElement>(null)
  const trigger = useRef<HTMLButtonElement>(null)
  const menu = useRef<HTMLDivElement>(null)
  const input = useRef<HTMLInputElement>(null)
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [active, setActive] = useState(0)
  const [dragged, setDragged] = useState<string | null>(null)
  const [dropTarget, setDropTarget] = useState<{ value: string; after: boolean } | null>(null)
  const [announcement, setAnnouncement] = useState('')
  const [position, setPosition] = useState<{ top: number; left: number; width: number; maxHeight: number } | null>(null)
  const positioned = position !== null
  const byValue = useMemo(() => {
    const entries = new Map(options.map((option) => [option.value, option]))
    for (const value of values) if (!entries.has(value)) entries.set(value, { value, label: value })
    return entries
  }, [options, values])
  const filtered = useMemo(() => {
    const search = query.trim().toLocaleLowerCase()
    const all = [...byValue.values()]
    const matches = all.filter((option) => !values.includes(option.value) && `${option.label} ${option.value}`.toLocaleLowerCase().includes(search))
    const known = all.some((option) => option.label.toLocaleLowerCase() === search || option.value.toLocaleLowerCase() === search)
    if (query.trim() && matches.length === 0 && !known) matches.push({ value: query.trim(), label: query.trim() })
    return matches
  }, [byValue, query, values])
  const dismiss = useCallback(() => setOpen(false), [])
  useDismissOnViewportChange(menu, open ? dismiss : null)

  useLayoutEffect(() => {
    if (!open || !input.current) return
    const rect = input.current.getBoundingClientRect()
    const below = window.innerHeight - rect.bottom - 14
    const above = rect.top - 14
    const upwards = below < 200 && above > below
    const maxHeight = Math.min(320, Math.max(120, upwards ? above : below))
    const height = Math.min(maxHeight, 10 + filtered.length * 40)
    const width = Math.min(280, window.innerWidth - 16)
    setPosition({ top: upwards ? Math.max(8, rect.top - height - 6) : rect.bottom + 6,
      left: Math.max(8, Math.min(rect.right - width, window.innerWidth - width - 8)), width, maxHeight })
  }, [open, filtered.length])

  useEffect(() => {
    if (!open || !positioned) return
    input.current?.focus()
    const outside = (event: PointerEvent): void => {
      const target = event.target as Node
      if (!root.current?.contains(target) && !menu.current?.contains(target)) dismiss()
    }
    document.addEventListener('pointerdown', outside)
    return () => document.removeEventListener('pointerdown', outside)
  }, [open, positioned, dismiss])

  useLayoutEffect(() => {
    if (open) document.getElementById(`${id}-${active}`)?.scrollIntoView({ block: 'nearest' })
  }, [open, active, id])

  function add(value: string): void {
    onValuesChange([...values, value])
    setQuery('')
    setActive(0)
    dismiss()
    requestAnimationFrame(() => trigger.current?.focus())
  }

  function move(value: string, target: number): void {
    const index = values.indexOf(value)
    if (target < 0 || target >= values.length || index === target) return
    const next = [...values]
    next.splice(index, 1)
    next.splice(target, 0, value)
    onValuesChange(next)
    setAnnouncement(t('tokenPicker.moved', { name: byValue.get(value)?.label ?? value, position: target + 1, count: values.length }))
    requestAnimationFrame(() => document.getElementById(`${id}-chip-${value}`)?.focus())
  }

  return <div ref={root} className={styles.root} role="group" aria-label={ariaLabel}>
    <div className={styles.tokens}>
      {values.map((value, index) => <span className={styles.chip} key={value}
        id={`${id}-chip-${value}`} draggable={ordered} tabIndex={ordered ? 0 : undefined}
        role={ordered ? 'group' : undefined} aria-label={ordered ? t('tokenPicker.priority', { name: byValue.get(value)?.label ?? value, position: index + 1 }) : undefined}
        aria-describedby={ordered ? `${id}-sort-help` : undefined}
        aria-keyshortcuts={ordered ? 'Alt+ArrowLeft Alt+ArrowRight Alt+ArrowUp Alt+ArrowDown' : undefined}
        title={ordered ? t('tokenPicker.dragTitle') : undefined}
        data-sortable={ordered || undefined} data-dragging={dragged === value || undefined}
        data-drop={dropTarget?.value === value ? dropTarget.after ? 'after' : 'before' : undefined}
        onDragStart={(event) => {
          event.dataTransfer.effectAllowed = 'move'
          event.dataTransfer.setData('text/plain', value)
          setDragged(value)
        }}
        onDragOver={(event) => {
          if (!ordered || dragged === null || dragged === value) return
          event.preventDefault()
          event.dataTransfer.dropEffect = 'move'
          const rect = event.currentTarget.getBoundingClientRect()
          setDropTarget({ value, after: event.clientX >= rect.left + rect.width / 2 })
        }}
        onDrop={(event) => {
          if (dragged === null || dropTarget?.value !== value) return
          event.preventDefault()
          let target = index + (dropTarget.after ? 1 : 0)
          if (values.indexOf(dragged) < target) target--
          move(dragged, target)
          setDragged(null)
          setDropTarget(null)
        }}
        onDragEnd={() => { setDragged(null); setDropTarget(null) }}
        onKeyDown={(event) => {
          if (!ordered || !event.altKey || event.target !== event.currentTarget) return
          if (['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) {
            event.preventDefault()
            move(value, index + (event.key === 'ArrowLeft' || event.key === 'ArrowUp' ? -1 : 1))
          }
        }}>
        <span>{ordered ? `${index + 1}. ` : ''}{byValue.get(value)?.label ?? value}</span>
        <button type="button" aria-label={t('tokenPicker.remove', { name: byValue.get(value)?.label ?? value })}
          onClick={() => onValuesChange(values.filter((item) => item !== value))}><X size={12} aria-hidden="true" /></button>
      </span>)}
      {open ? <span className={styles.input}>
        <Input ref={input} bare autoFocus value={query} role="combobox" aria-expanded="true"
          aria-label={t('tokenPicker.search', { name: ariaLabel.toLocaleLowerCase() })} placeholder={placeholder}
          aria-controls={filtered.length ? id : undefined} aria-activedescendant={filtered.length ? `${id}-${active}` : undefined}
          onBlur={dismiss} onChange={(event) => { setQuery(event.target.value); setActive(0) }}
          onKeyDown={(event) => {
            if (event.key === 'Escape') {
              event.preventDefault()
              event.stopPropagation()
              dismiss()
              requestAnimationFrame(() => trigger.current?.focus())
            } else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
              event.preventDefault()
              setActive((current) => Math.max(0, Math.min(filtered.length - 1, current + (event.key === 'ArrowDown' ? 1 : -1))))
            } else if (event.key === 'Enter' && filtered[active]) { event.preventDefault(); add(filtered[active].value) }
          }} />
      </span> : <button ref={trigger} type="button" className={styles.add}
        aria-label={t('tokenPicker.add', { name: ariaLabel.toLocaleLowerCase() })} aria-haspopup="listbox" aria-expanded="false" title={placeholder}
        onClick={() => { setQuery(''); setActive(0); setOpen(true) }}><Plus size={12} aria-hidden="true" />{addLabel}</button>}
    </div>
    {ordered && <>
      <span id={`${id}-sort-help`} className={styles.srOnly}>{t('tokenPicker.dragHelp')}</span>
      <span className={styles.srOnly} role="status" aria-live="polite">{announcement}</span>
    </>}
    {open && position && filtered.length > 0 && createPortal(<LayerBoundary>
      <div ref={menu} className={`dc-settings-select-menu ${styles.menu}`} style={position}
        onMouseDown={(event) => event.preventDefault()}>
        <div id={id} role="listbox" aria-label={ariaLabel} className={styles.options}>
          {filtered.map((option, index) => <SelectMenuOption key={option.value} id={`${id}-${index}`} value={option.value}
            option={{ label: option.label, description: option.label === option.value ? undefined : option.value }}
            selected={false} active={index === active} showTooltip={false}
            onActivate={() => setActive(index)} onSelect={() => add(option.value)} />)}
        </div>
      </div>
    </LayerBoundary>, document.body)}
  </div>
}
