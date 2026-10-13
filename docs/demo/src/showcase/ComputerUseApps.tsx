import { useLayoutEffect, useRef, useState, type JSX, type ReactNode } from 'react'
import { ChevronDown, FileSpreadsheet, Inbox, Mail, Minus, Receipt, Reply, Send, Square, X } from 'lucide-react'
import type { ComputerUseAppKind, ComputerUseScript } from './content/computerUse'
import './computerUseApps.css'

interface AppProps {
  labels: Record<string, string>
  values: Record<string, string>
  focus: string | null
}

const ICONS: Record<ComputerUseAppKind, typeof Mail> = { sheet: FileSpreadsheet, form: Receipt, mail: Mail }

export function AppIcon({ kind, size = 14 }: { kind: ComputerUseAppKind; size?: number }): JSX.Element {
  const Icon = ICONS[kind]
  return <Icon size={size} aria-hidden className="cu-app-icon" data-kind={kind} />
}

function target(name: string, focus: string | null): { 'data-cu-target': string; 'data-focus'?: '' } {
  return focus === name ? { 'data-cu-target': name, 'data-focus': '' } : { 'data-cu-target': name }
}

function SheetApp({ labels, values, focus }: AppProps): JSX.Element {
  const rowFocus = focus === 'row1'
  const formulaCell = focus && /^[A-C][1-7]$/.test(focus) ? focus : null
  const formula = formulaCell ? values[`${formulaCell}.formula`] ?? values[formulaCell] ?? '' : ''
  return (
    <div className="cu-sheet">
      <div className="cu-sheet__ribbon">
        <button type="button" tabIndex={-1} className="cu-sheet__tool" {...target('sort', focus)}>{labels.sort}</button>
        <button type="button" tabIndex={-1} className="cu-sheet__tool cu-sheet__tool--bold" {...target('bold', focus)}>{labels.bold}</button>
      </div>
      <div className="cu-sheet__formula">
        <span className="cu-sheet__ref">{formulaCell ?? ''}</span>
        <span className="cu-sheet__fx">fx</span>
        <span className="cu-sheet__formula-text">{formula}</span>
      </div>
      <div className="cu-sheet__grid" role="table">
        <span className="cu-sheet__corner" />
        {['A', 'B', 'C'].map((column) => <span key={column} className="cu-sheet__head">{column}</span>)}
        {[1, 2, 3, 4, 5, 6, 7].map((row) => (
          <SheetRow key={row} row={row} values={values} focus={focus} selected={row === 1 && rowFocus} />
        ))}
      </div>
    </div>
  )
}

function SheetRow({ row, values, focus, selected }: { row: number; values: Record<string, string>; focus: string | null; selected: boolean }): JSX.Element {
  const header = row === 1
  return (
    <>
      <span className="cu-sheet__head cu-sheet__rownum" {...(header ? target('row1', focus) : {})}>{row}</span>
      {['A', 'B', 'C'].map((column) => {
        const cell = `${column}${row}`
        return (
          <span
            key={cell}
            className="cu-sheet__cell"
            data-header={header || undefined}
            data-bold={(header && values.bold === '1') || undefined}
            data-number={(column === 'C' && !header) || undefined}
            data-selected={selected || undefined}
            {...target(cell, focus)}
          >
            {values[cell] ?? ''}
          </span>
        )
      })}
    </>
  )
}

function Field({ name, label, values, focus, children }: AppProps & { name: string; label: string; children?: ReactNode }): JSX.Element {
  return (
    <label className="cu-form__field">
      <span className="cu-form__label">{label}</span>
      <span className="cu-form__input" {...target(name, focus)}>
        {values[name] ?? ''}
        {children}
      </span>
    </label>
  )
}

function FormApp(props: AppProps): JSX.Element {
  const { labels, values, focus } = props
  const open = values.form === '1'
  return (
    <div className="cu-form">
      <nav className="cu-form__nav">
        <span className="cu-form__nav-item" data-active="">{labels.nav}</span>
        <span className="cu-form__nav-item">{labels.reports}</span>
      </nav>
      <div className="cu-form__main">
        <div className="cu-form__toolbar">
          <button type="button" tabIndex={-1} className="cu-form__new" {...target('new', focus)}>+ {labels.new}</button>
        </div>
        {open ? (
          <div className="cu-form__sheet">
            <strong className="cu-form__heading">{labels.heading}</strong>
            <div className="cu-form__fields">
              <Field {...props} name="date" label={labels.date} />
              <Field {...props} name="merchant" label={labels.merchant} />
              <Field {...props} name="amount" label={labels.amount} />
              <Field {...props} name="category" label={labels.category}>
                {!values.category && <span className="cu-form__placeholder">{labels.choose}</span>}
                <ChevronDown size={12} aria-hidden className="cu-form__chevron" />
                {values['category.open'] === '1' && (
                  <span className="cu-form__options">
                    <span {...target('option.travel', focus)}>{labels.travel}</span>
                    <span>{labels.meals}</span>
                    <span>{labels.office}</span>
                  </span>
                )}
              </Field>
            </div>
            {values.saved === '1' && <span className="cu-form__saved">{labels.saved}</span>}
            <div className="cu-form__actions">
              <button type="button" tabIndex={-1} className="cu-form__button" {...target('save', focus)}>{labels.save}</button>
              <button type="button" tabIndex={-1} className="cu-form__button cu-form__button--primary">{labels.submit}</button>
            </div>
          </div>
        ) : (
          <div className="cu-form__list">
            {['row1', 'row2'].map((row) => (
              <div key={row} className="cu-form__row">
                <span>{values[`${row}.title`]}</span>
                <span className="cu-form__meta">{values[`${row}.meta`]}</span>
              </div>
            ))}
            <span className="cu-form__empty">{labels.empty}</span>
          </div>
        )}
      </div>
    </div>
  )
}

function MailApp({ labels, values, focus }: AppProps): JSX.Element {
  const open = values.open === 'dana'
  return (
    <div className="cu-mail">
      <div className="cu-mail__list">
        <span className="cu-mail__folder"><Inbox size={13} aria-hidden /> {labels.inbox}</span>
        <div className="cu-mail__item" data-selected={open || undefined} data-unread={!open || undefined} {...target('message.dana', focus)}>
          <strong>{labels.danaFrom}</strong>
          <span>{labels.danaSubject}</span>
        </div>
        <div className="cu-mail__item">
          <strong>{labels.otherFrom}</strong>
          <span>{labels.otherSubject}</span>
        </div>
      </div>
      <div className="cu-mail__pane">
        {values.sent === '1' && <div className="cu-mail__sent"><Send size={12} aria-hidden /> {labels.sent}</div>}
        {open ? (
          <>
            <div className="cu-mail__message">
              <strong>{labels.danaSubject}</strong>
              <span className="cu-mail__from">{labels.danaFrom}</span>
              <p>{labels.danaPreview}</p>
              {values.compose !== '1' && values.sent !== '1' && (
                <button type="button" tabIndex={-1} className="cu-mail__button" {...target('reply', focus)}><Reply size={12} aria-hidden /> {labels.reply}</button>
              )}
            </div>
            {values.compose === '1' && (
              <div className="cu-mail__compose">
                <div className="cu-mail__header"><span>{labels.to}</span> {labels.danaFrom}</div>
                <div className="cu-mail__header">{labels.subject}</div>
                <div className="cu-mail__body" {...target('body', focus)}>{values.body ?? ''}</div>
                <button type="button" tabIndex={-1} className="cu-mail__button cu-mail__button--primary" {...target('send', focus)}><Send size={12} aria-hidden /> {labels.send}</button>
              </div>
            )}
          </>
        ) : (
          <span className="cu-mail__empty">{labels.pick}</span>
        )}
      </div>
    </div>
  )
}

function Cursor({ host, target: name, clicks, values }: { host: HTMLElement | null; target: string | null; clicks: number; values: unknown }): JSX.Element {
  const [position, setPosition] = useState<{ x: number; y: number } | null>(null)

  useLayoutEffect(() => {
    if (!host) return
    const measure = (): void => {
      const element = name ? host.querySelector<HTMLElement>(`[data-cu-target="${name}"]`) : null
      if (!element) return
      const box = host.getBoundingClientRect()
      const rect = element.getBoundingClientRect()
      setPosition({ x: rect.left - box.left + Math.min(rect.width / 2, 40), y: rect.top - box.top + rect.height / 2 })
    }
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(host)
    return () => observer.disconnect()
  }, [host, name, values])

  const visible = name !== null && position !== null
  return (
    <span
      className="cu-cursor"
      data-visible={visible || undefined}
      style={position ? { transform: `translate(${position.x}px, ${position.y}px)` } : undefined}
      aria-hidden
    >
      {clicks > 0 && <span key={clicks} className="cu-cursor__click" />}
      <svg viewBox="0 0 16 22" width="16" height="22"><path d="M1 1v17.5l4.6-4.3 3 6.8 3-1.3-3-6.6H15L1 1Z" fill="#fff" stroke="#111" strokeWidth="1.2" strokeLinejoin="round" /></svg>
    </span>
  )
}

export function AppWindow({ script, values, focus, cursor, clicks }: {
  script: ComputerUseScript
  values: Record<string, string>
  focus: string | null
  cursor: string | null
  clicks: number
}): JSX.Element {
  const bodyRef = useRef<HTMLDivElement>(null)
  const [host, setHost] = useState<HTMLDivElement | null>(null)
  useLayoutEffect(() => setHost(bodyRef.current), [])
  const props: AppProps = { labels: script.labels, values, focus }
  return (
    <section className="cu-window cu-app" aria-label={script.app.window}>
      <header className="cu-window__titlebar">
        <AppIcon kind={script.app.kind} />
        <span className="cu-window__title">{script.app.window}</span>
        <span className="cu-window__controls" aria-hidden><Minus size={12} /><Square size={10} /><X size={12} /></span>
      </header>
      <div ref={bodyRef} className="cu-app__body">
        {script.app.kind === 'sheet' && <SheetApp {...props} />}
        {script.app.kind === 'form' && <FormApp {...props} />}
        {script.app.kind === 'mail' && <MailApp {...props} />}
        <Cursor host={host} target={cursor} clicks={clicks} values={values} />
      </div>
    </section>
  )
}
