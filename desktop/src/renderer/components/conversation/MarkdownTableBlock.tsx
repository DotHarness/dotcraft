import { createContext, useContext, useState, type ComponentProps } from 'react'
import type { ExtraProps } from 'react-markdown'
import { Maximize2 } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { IconButton } from '../ui/IconButton'
import { tableCellMinWidths } from './markdownTable'
import { MarkdownTableViewer } from './MarkdownTableViewer'
import styles from './MarkdownTable.module.css'

const TableCellMinWidths = createContext<ReadonlyMap<object, string>>(new Map())

export function MarkdownTableBlock({ children, node, ...props }: ComponentProps<'table'> & ExtraProps): JSX.Element {
  const t = useT()
  const [expanded, setExpanded] = useState(false)
  const table = (
    <TableCellMinWidths.Provider value={tableCellMinWidths(node)}>
      <table {...props} className={styles.table}>{children}</table>
    </TableCellMinWidths.Provider>
  )

  return (
    <div className={styles.root}>
      <div className={styles.scroller}>{table}</div>
      <div className={styles.actions}>
        <IconButton
          size={24}
          icon={<Maximize2 size={14} aria-hidden />}
          label={t('markdown.table.expand')}
          aria-haspopup="dialog"
          aria-expanded={expanded}
          tooltipLabel={t('markdown.table.expand')}
          onClick={() => setExpanded(true)}
        />
      </div>
      {expanded && <MarkdownTableViewer onClose={() => setExpanded(false)}>{table}</MarkdownTableViewer>}
    </div>
  )
}

export function MarkdownTableHeader({ children, node, style, ...props }: ComponentProps<'th'> & ExtraProps): JSX.Element {
  const minWidth = useContext(TableCellMinWidths).get(node as object)
  return <th {...props} style={{ minWidth, ...style }}>{children}</th>
}

export function MarkdownTableCell({ children, node, style, ...props }: ComponentProps<'td'> & ExtraProps): JSX.Element {
  const minWidth = useContext(TableCellMinWidths).get(node as object)
  return <td {...props} style={{ minWidth, ...style }}>{children}</td>
}
