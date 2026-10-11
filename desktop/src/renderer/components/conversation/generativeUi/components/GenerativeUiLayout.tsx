import { useMemo, useRef, type CSSProperties, type FormEvent, type ReactNode } from 'react'
import { GenerativeUiFormContext, useGenerativeUiInvoke, type GenerativeUiFormContextValue } from '../GenerativeUiContext'
import type { GenerativeUiJson } from '../generativeUiProtocol'
import type { GenerativeUiProps } from '../generativeUiSchema'
import styles from '../GenerativeUi.module.css'

type LayoutProps<Name extends 'Card' | 'Row' | 'Col' | 'Box' | 'Spacer' | 'Divider' | 'Form'> = {
  props: GenerativeUiProps<Name>
  children?: ReactNode
}

const FLEX_ALIGN = { start: 'flex-start', center: 'center', end: 'flex-end', stretch: 'stretch', baseline: 'baseline' } as const
const FLEX_JUSTIFY = { start: 'flex-start', center: 'center', end: 'flex-end', between: 'space-between', around: 'space-around' } as const

function flexValue(flex: number | undefined): string | undefined {
  if (flex === undefined) return undefined
  return flex === 0 ? '0 0 auto' : `${flex} 1 0`
}

function flexStyle(props: { gap?: number; align?: keyof typeof FLEX_ALIGN; justify?: keyof typeof FLEX_JUSTIFY; flex?: number }, gap: number): CSSProperties {
  return {
    gap: props.gap ?? gap,
    alignItems: props.align ? FLEX_ALIGN[props.align] : undefined,
    justifyContent: props.justify ? FLEX_JUSTIFY[props.justify] : undefined,
    flex: flexValue(props.flex)
  }
}

export function GenerativeUiCard({ props, children }: LayoutProps<'Card'>): JSX.Element {
  return (
    <section className={styles.card} data-padding={props.padding} style={{ gap: props.gap ?? 12 }}>
      {(props.title || props.subtitle) && (
        <header className={styles.cardHeader}>
          {props.title && <h3 className={styles.cardTitle}>{props.title}</h3>}
          {props.subtitle && <span className={styles.cardSubtitle}>{props.subtitle}</span>}
        </header>
      )}
      {children}
    </section>
  )
}

export function GenerativeUiRow({ props, children }: LayoutProps<'Row'>): JSX.Element {
  return <div className={styles.row} data-wrap={props.wrap ? '' : undefined} style={flexStyle(props, 8)}>{children}</div>
}

export function GenerativeUiCol({ props, children }: LayoutProps<'Col'>): JSX.Element {
  return <div className={styles.stack} style={flexStyle(props, 8)}>{children}</div>
}

export function GenerativeUiBox({ props, children }: LayoutProps<'Box'>): JSX.Element {
  return (
    <div
      className={styles.box}
      data-surface={props.surface}
      data-radius={props.radius}
      style={{
        gap: props.gap ?? 8,
        padding: props.padding,
        flex: flexValue(props.flex),
        minWidth: props.minWidth,
        maxWidth: props.maxWidth
      }}
    >
      {children}
    </div>
  )
}

export function GenerativeUiSpacer({ props }: LayoutProps<'Spacer'>): JSX.Element {
  return <div aria-hidden="true" className={props.size === undefined ? styles.spacer : undefined} style={props.size === undefined ? undefined : { flex: `0 0 ${props.size}px` }} />
}

export function GenerativeUiDivider({ props }: LayoutProps<'Divider'>): JSX.Element {
  return <hr className={styles.divider} style={{ marginBlock: props.spacing ?? 4 }} />
}

export function GenerativeUiForm({ props, children }: LayoutProps<'Form'>): JSX.Element {
  const invoke = useGenerativeUiInvoke()
  const values = useRef(new Map<string, GenerativeUiJson>())
  const context = useMemo<GenerativeUiFormContextValue>(() => ({
    setValue: (name, value) => { values.current.set(name, value) }
  }), [])
  const submit = (event: FormEvent): void => {
    event.preventDefault()
    invoke(props.onSubmit, [Object.fromEntries(values.current)])
  }
  return (
    <GenerativeUiFormContext.Provider value={context}>
      <form className={styles.stack} style={{ gap: props.gap ?? 12 }} noValidate onSubmit={submit}>
        {children}
      </form>
    </GenerativeUiFormContext.Provider>
  )
}
