import { memo, useEffect, useMemo, useState } from 'react'
import ReactMarkdown, { defaultUrlTransform } from 'react-markdown'
import remarkGfm from 'remark-gfm'
import type { Extension as FromMarkdownExtension } from 'mdast-util-from-markdown'
import { mathFromMarkdown } from 'mdast-util-math'
import type { Extension as MicromarkExtension } from 'micromark-util-types'
import type { Components, Options as MarkdownOptions } from 'react-markdown'
import { useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { isBareFileLocationLinkTarget } from '../../../shared/viewer/linkResolver'
import { InlineReferenceLink } from './InlineReferenceLink'
import { CodeBlock, HighlightedCode } from './MarkdownCodeBlock'
import { MarkdownTableBlock, MarkdownTableCell, MarkdownTableHeader } from './MarkdownTableBlock'
import { ReplyMarkdownImage } from './ReplyMarkdownImage'
import { mathSyntax } from './mathSyntax'
import { copySelectionWithMathSource } from './mathCopy'
import { rehypeStreamingFade } from './streamingFade'
import { repairStreamingMarkdown } from './streamingMarkdown'

interface MarkdownRendererProps {
  content: string
  linkMode?: 'conversation' | 'external'
  containOverflow?: boolean
  enableMermaid?: boolean
  streaming?: boolean
  workspaceImages?: boolean
}

/** Memoized to avoid re-rendering finalized messages in the turn history. Spec §10.3.3. */
export const MarkdownRenderer = memo(function MarkdownRenderer({
  content,
  linkMode = 'conversation',
  containOverflow = false,
  enableMermaid = true,
  streaming = false,
  workspaceImages = false
}: MarkdownRendererProps): JSX.Element {
  const t = useT()
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const remoteWorkspaceActive = useConversationStore((s) => s.remoteWorkspaceActive)
  const activeThreadId = useThreadStore((s) => s.activeThreadId)
  const basePlugins = useKatexPlugins()
  const rehypePlugins = useMemo(() => streaming ? [...basePlugins, rehypeStreamingFade] : basePlugins, [basePlugins, streaming])
  const markdown = useMemo(() => streaming ? repairStreamingMarkdown(content) : content, [content, streaming])

  const customComponents = useMemo<Components>(() => ({
    ...baseComponents,
    ...(workspaceImages ? { img: ReplyMarkdownImage } : {}),
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
  }), [activeThreadId, enableMermaid, linkMode, remoteWorkspaceActive, t, workspaceImages, workspacePath])

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
        urlTransform={(url, key) => markdownUrlTransform(url, key, workspaceImages)}
      >
        {markdown}
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

  table: MarkdownTableBlock,

  th: MarkdownTableHeader,
  td: MarkdownTableCell
}

function markdownUrlTransform(url: string, key: string, workspaceImages: boolean): string {
  const trimmed = url.trim()
  if ((key === 'href' || (key === 'src' && workspaceImages)) && isLocalFileTarget(trimmed)) return trimmed
  if (key === 'href' && isBareFileLocationLinkTarget(trimmed)) return trimmed
  return defaultUrlTransform(url)
}

function isLocalFileTarget(value: string): boolean {
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
