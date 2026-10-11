import { lazy, Suspense, useState, type ReactNode } from 'react'
import { ImageOff } from 'lucide-react'
import { useLocale } from '../../../../contexts/LocaleContext'
import { MarkdownRenderer } from '../../MarkdownRenderer'
import { formatGenerativeUiValue } from '../generativeUiFormat'
import type { GenerativeUiProps } from '../generativeUiSchema'
import styles from '../GenerativeUi.module.css'
import dataStyles from './GenerativeUiData.module.css'

const LazyIcon = lazy(() => import('./GenerativeUiIcon'))

type ContentProps<Name extends 'Title' | 'Text' | 'Caption' | 'Markdown' | 'Badge' | 'Icon' | 'Image' | 'List' | 'Table'> = {
  props: GenerativeUiProps<Name>
  children?: ReactNode
}

export function GenerativeUiIconGlyph({ name, size = 16, label }: { name: string; size?: number; label?: string }): JSX.Element {
  return (
    <Suspense fallback={<span className={styles.icon} style={{ width: size, height: size }} />}>
      <LazyIcon name={name} size={size} label={label} />
    </Suspense>
  )
}

const TITLE_TAGS = { 1: 'h3', 2: 'h4', 3: 'h5' } as const

export function GenerativeUiTitle({ props, children }: ContentProps<'Title'>): JSX.Element {
  const level = props.level ?? 2
  const Tag = TITLE_TAGS[level]
  return <Tag className={styles.title} data-level={level}>{props.value ?? children}</Tag>
}

export function GenerativeUiText({ props, children }: ContentProps<'Text'>): JSX.Element {
  return (
    <p
      className={`${styles.text} ${styles.toned}`}
      data-tone={props.tone}
      data-size={props.size}
      data-weight={props.weight}
      data-mono={props.mono ? '' : undefined}
      data-tabular={props.tabular ? '' : undefined}
      data-truncate={props.truncate ? '' : undefined}
      style={props.align ? { textAlign: props.align } : undefined}
    >
      {props.value ?? children}
    </p>
  )
}

export function GenerativeUiCaption({ props, children }: ContentProps<'Caption'>): JSX.Element {
  return <span className={`${styles.caption} ${styles.toned}`} data-tone={props.tone}>{props.value ?? children}</span>
}

export function GenerativeUiMarkdown({ props }: ContentProps<'Markdown'>): JSX.Element {
  return (
    <div className={styles.markdown}>
      <MarkdownRenderer content={props.content} linkMode="external" enableMermaid={false} containOverflow />
    </div>
  )
}

export function GenerativeUiBadge({ props, children }: ContentProps<'Badge'>): JSX.Element {
  const tone = props.tone === 'neutral' ? undefined : props.tone
  return (
    <span className={`dc-status-badge ${styles.badge}`} data-tone={tone}>
      <span className="dc-status-badge__label">{props.label ?? children}</span>
    </span>
  )
}

export function GenerativeUiIcon({ props }: ContentProps<'Icon'>): JSX.Element {
  return (
    <span className={`${styles.icon} ${styles.toned}`} data-tone={props.tone}>
      <GenerativeUiIconGlyph name={props.name} size={props.size ?? 16} label={props.label} />
    </span>
  )
}

export function GenerativeUiImage({ props }: ContentProps<'Image'>): JSX.Element {
  const [failed, setFailed] = useState(false)
  const size = { width: props.width, height: props.height }
  if (failed) {
    return (
      <div role="img" aria-label={props.alt} className={styles.imageFallback} style={size}>
        <ImageOff size={14} aria-hidden />
        <span>{props.alt}</span>
      </div>
    )
  }
  return (
    <img
      className={styles.image}
      src={props.src}
      alt={props.alt}
      data-fit={props.fit}
      data-radius={props.radius ?? 'md'}
      draggable={false}
      style={size}
      onError={() => setFailed(true)}
    />
  )
}

export function GenerativeUiList({ props }: ContentProps<'List'>): JSX.Element {
  const Tag = props.ordered ? 'ol' : 'ul'
  return (
    <Tag className={dataStyles.list} data-divided={props.divided ? '' : undefined}>
      {props.items.map((item, index) => (
        <li key={index} className={dataStyles.listItem}>
          {props.ordered && <span className={dataStyles.listIndex}>{index + 1}</span>}
          {item.icon && <span className={dataStyles.listIcon}><GenerativeUiIconGlyph name={item.icon} /></span>}
          <span className={dataStyles.listBody}>
            <span className={dataStyles.listTitle}>{item.title}</span>
            {item.description && <span className={dataStyles.listDescription}>{item.description}</span>}
          </span>
          {item.trailing && <span className={dataStyles.listTrailing}>{item.trailing}</span>}
        </li>
      ))}
    </Tag>
  )
}

export function GenerativeUiTable({ props }: ContentProps<'Table'>): JSX.Element {
  const locale = useLocale()
  return (
    <div className={dataStyles.tableScroll}>
      <table className={dataStyles.table}>
        {props.caption && <caption className={dataStyles.tableCaption}>{props.caption}</caption>}
        <thead>
          <tr>
            {props.columns.map(column => <th key={column.key} scope="col" data-align={column.align}>{column.label}</th>)}
          </tr>
        </thead>
        <tbody>
          {props.rows.map((row, index) => (
            <tr key={props.rowKey && row[props.rowKey] != null ? String(row[props.rowKey]) : index}>
              {props.columns.map(column => (
                <td key={column.key} data-align={column.align ?? (typeof row[column.key] === 'number' ? 'end' : undefined)}>
                  {formatCell(row[column.key], column, locale)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function formatCell(value: unknown, column: GenerativeUiProps<'Table'>['columns'][number], locale: string): string {
  if (typeof value === 'boolean') return value ? '✓' : '—'
  return formatGenerativeUiValue(value, column.format, locale, column.currency)
}
