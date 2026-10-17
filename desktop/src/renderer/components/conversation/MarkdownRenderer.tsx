import { createContext, memo, useContext, useEffect, useMemo, useState, type ComponentProps } from 'react'
import ReactMarkdown, { defaultUrlTransform } from 'react-markdown'
import remarkGfm from 'remark-gfm'
import type { Extension as FromMarkdownExtension } from 'mdast-util-from-markdown'
import { mathFromMarkdown } from 'mdast-util-math'
import type { Extension as MicromarkExtension } from 'micromark-util-types'
import type { Components, ExtraProps, Options as MarkdownOptions } from 'react-markdown'
import { useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { isBareFileLocationLinkTarget } from '../../../shared/viewer/linkResolver'
import { InlineReferenceLink } from './InlineReferenceLink'
import { CodeBlock, HighlightedCode } from './MarkdownCodeBlock'
import { tableCellMinWidths } from './markdownTable'
import { mathSyntax } from './mathSyntax'
import { copySelectionWithMathSource } from './mathCopy'

interface MarkdownRendererProps {
  content: string
  linkMode?: 'conversation' | 'external'
  containOverflow?: boolean
  enableMermaid?: boolean
}

/** Memoized to avoid re-rendering finalized messages in the turn history. Spec §10.3.3. */
export const MarkdownRenderer = memo(function MarkdownRenderer({
  content,
  linkMode = 'conversation',
  containOverflow = false,
  enableMermaid = true
}: MarkdownRendererProps): JSX.Element {
  const t = useT()
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const remoteWorkspaceActive = useConversationStore((s) => s.remoteWorkspaceActive)
  const activeThreadId = useThreadStore((s) => s.activeThreadId)
  const rehypePlugins = useKatexPlugins()

  const customComponents = useMemo<Components>(() => ({
    ...baseComponents,
    pre({ children, ...props }) {
      return <CodeBlock enableMermaid={enableMermaid} {...props}>{children}</CodeBlock>
    },
    a({ href, children, ...props }) {
      return (
        <InlineReferenceLink
          href={href}
          workspacePath={workspacePath}
          remoteWorkspaceActive={remoteWorkspaceActive}
          activeThreadId={activeThreadId}
          linkMode={linkMode}
          t={t}
          {...props}
        >
          {children}
        </InlineReferenceLink>
      )
    }
  }), [activeThreadId, enableMermaid, linkMode, remoteWorkspaceActive, t, workspacePath])

  return (
    <div
      className={containOverflow ? 'markdown-body markdown-body--contained' : 'markdown-body'}
      // Marks this block as one searchable unit for the window-wide find.
      data-find-segment=""
      style={containOverflow ? containedMarkdownContainerStyle : markdownContainerStyle}
      onCopy={copySelectionWithMathSource}
    >
      <ReactMarkdown
        remarkPlugins={remarkPlugins}
        rehypePlugins={rehypePlugins}
        components={customComponents}
        urlTransform={markdownUrlTransform}
      >
        {content}
      </ReactMarkdown>
    </div>
  )
})

type PluggableList = NonNullable<MarkdownOptions['remarkPlugins']>

function remarkMath(this: { data(): unknown }): void {
  const data = this.data() as { micromarkExtensions?: MicromarkExtension[]; fromMarkdownExtensions?: FromMarkdownExtension[] }
  ;(data.micromarkExtensions ??= []).push(mathSyntax())
  ;(data.fromMarkdownExtensions ??= []).push(mathFromMarkdown())
}

const remarkPlugins: PluggableList = [remarkGfm, remarkMath]

const LINE_BREAK = /^<br\s*\/?>$/i

type HastNode = { type: string; value?: string; children?: HastNode[] }

function rehypeLineBreaks(): (tree: HastNode) => void {
  return replaceLineBreaks
}

function replaceLineBreaks(node: HastNode): void {
  node.children?.forEach((child, index, children) => {
    if (child.type === 'raw' && LINE_BREAK.test(child.value?.trim() ?? '')) {
      children[index] = { type: 'element', tagName: 'br', properties: {}, children: [] } as HastNode
    } else {
      replaceLineBreaks(child)
    }
  })
}

const noRehypePlugins: PluggableList = [rehypeLineBreaks]
let katexPlugins: PluggableList | null = null
let katexLoading: Promise<PluggableList> | null = null

// Until KaTeX loads, formulas show their source as code.
function useKatexPlugins(): PluggableList {
  const [plugins, setPlugins] = useState(katexPlugins)
  useEffect(() => {
    if (plugins) return
    let mounted = true
    katexLoading ??= Promise.all([import('rehype-katex'), import('katex/dist/katex.min.css'), import('./markdownMath.css')])
      .then(([{ default: rehypeKatex }]) => (katexPlugins = [rehypeLineBreaks, rehypeKatex]))
    void katexLoading.then((loaded) => mounted && setPlugins(loaded))
    return () => { mounted = false }
  }, [plugins])
  return plugins ?? noRehypePlugins
}

const minorHeadingStyle: React.CSSProperties = {
  margin: '10px 0 6px',
  fontWeight: 600,
  color: 'var(--text-primary)'
}

const baseComponents: Components = {
  p({ children, ...props }) {
    return (
      <p
        style={{
          margin: '0 0 6px',
          color: 'var(--text-primary)'
        }}
        {...props}
      >
        {children}
      </p>
    )
  },

  h1({ children, ...props }) {
    return (
      <h1
        style={{
          margin: '2px 0 10px',
          fontSize: '1.5em',
          lineHeight: 1.24,
          fontWeight: 600,
          letterSpacing: 0,
          color: 'var(--text-primary)'
        }}
        {...props}
      >
        {children}
      </h1>
    )
  },

  h2({ children, ...props }) {
    return (
      <h2
        style={{
          margin: '14px 0 8px',
          fontSize: '1.25em',
          lineHeight: 1.3,
          fontWeight: 600,
          letterSpacing: 0,
          color: 'var(--text-primary)'
        }}
        {...props}
      >
        {children}
      </h2>
    )
  },

  h3({ children, ...props }) {
    return (
      <h3
        style={{
          margin: '12px 0 7px',
          fontSize: '1.125em',
          lineHeight: 1.34,
          fontWeight: 600,
          color: 'var(--text-primary)'
        }}
        {...props}
      >
        {children}
      </h3>
    )
  },

  h4({ children, ...props }) {
    return <h4 style={minorHeadingStyle} {...props}>{children}</h4>
  },

  h5({ children, ...props }) {
    return <h5 style={minorHeadingStyle} {...props}>{children}</h5>
  },

  h6({ children, ...props }) {
    return <h6 style={minorHeadingStyle} {...props}>{children}</h6>
  },

  ul({ children, ...props }) {
    return (
      <ul
        style={{
          margin: '0 0 6px',
          paddingLeft: '22px'
        }}
        {...props}
      >
        {children}
      </ul>
    )
  },

  ol({ children, ...props }) {
    return (
      <ol
        style={{
          margin: '0 0 6px',
          paddingLeft: '22px'
        }}
        {...props}
      >
        {children}
      </ol>
    )
  },

  li({ children, ...props }) {
    return (
      <li
        style={{
          margin: '0 0 3px',
          color: 'var(--text-primary)'
        }}
        {...props}
      >
        {children}
      </li>
    )
  },

  code({ children, className, ...props }) {
    const isBlock = Boolean(className)
    if (!isBlock) {
      return (
        <code
          style={{
            fontFamily: 'var(--font-mono)',
            fontSize: 'var(--text-code-size)',
            backgroundColor: 'var(--bg-tertiary)',
            padding: '2px 6px',
            borderRadius: '6px',
            color: 'var(--text-primary)'
          }}
          {...props}
        >
          {children}
        </code>
      )
    }
    return (
      <HighlightedCode className={className} {...props}>
        {children}
      </HighlightedCode>
    )
  },

  blockquote({ children, ...props }) {
    return (
      <blockquote
        style={{
          borderLeft: '3px solid var(--border-active)',
          paddingLeft: '14px',
          margin: '8px 0 10px',
          color: 'var(--text-secondary)',
          fontStyle: 'italic'
        }}
        {...props}
      >
        {children}
      </blockquote>
    )
  },

  table({ children, node, ...props }) {
    return (
      <div style={{ overflowX: 'auto', margin: '8px 0 10px' }}>
        <table
          style={{
            borderCollapse: 'collapse',
            width: '100%',
            fontSize: 'var(--conversation-secondary-size)',
            lineHeight: 'var(--conversation-line-height)'
          }}
          {...props}
        >
          <TableCellMinWidths.Provider value={tableCellMinWidths(node)}>
            {children}
          </TableCellMinWidths.Provider>
        </table>
      </div>
    )
  },

  th: MarkdownTableHeader,
  td: MarkdownTableCell
}

const TableCellMinWidths = createContext<ReadonlyMap<object, string>>(new Map())

function MarkdownTableHeader({ children, node, style, ...props }: ComponentProps<'th'> & ExtraProps): JSX.Element {
  const minWidth = useContext(TableCellMinWidths).get(node as object)
  return (
    <th
      style={{
        padding: '8px 12px',
        borderBottom: '1px solid var(--border-active)',
        textAlign: 'left',
        fontWeight: 600,
        color: 'var(--text-primary)',
        minWidth,
        ...style
      }}
      {...props}
    >
      {children}
    </th>
  )
}

function MarkdownTableCell({ children, node, style, ...props }: ComponentProps<'td'> & ExtraProps): JSX.Element {
  const minWidth = useContext(TableCellMinWidths).get(node as object)
  return (
    <td
      style={{
        padding: '8px 12px',
        borderBottom: '1px solid var(--border-default)',
        color: 'var(--text-secondary)',
        minWidth,
        ...style
      }}
      {...props}
    >
      {children}
    </td>
  )
}

function markdownUrlTransform(url: string, key: string): string | null | undefined {
  const trimmed = url.trim()
  if (key === 'href' && (isLocalFileLinkTarget(trimmed) || isBareFileLocationLinkTarget(trimmed))) return trimmed
  return defaultUrlTransform(url)
}

function isLocalFileLinkTarget(value: string): boolean {
  return value.toLowerCase().startsWith('file://') ||
    /^[A-Za-z]:(?:[\\/]|%5C)/i.test(value) ||
    value.startsWith('/') ||
    value.startsWith('\\') ||
    /^%5C/i.test(value)
}

const markdownContainerStyle: React.CSSProperties = {
  color: 'var(--text-primary)',
  fontFamily: 'var(--font-body)',
  fontSize: 'var(--text-body-size)',
  fontWeight: 'var(--conversation-font-weight)',
  lineHeight: 'var(--text-body-line-height)',
  wordBreak: 'break-word',
  width: '100%',
  maxWidth: 'var(--conversation-reading-width)'
}

const containedMarkdownContainerStyle: React.CSSProperties = {
  ...markdownContainerStyle,
  minWidth: 0,
  maxWidth: '100%',
  boxSizing: 'border-box',
  overflowWrap: 'anywhere',
  wordBreak: 'break-word'
}
