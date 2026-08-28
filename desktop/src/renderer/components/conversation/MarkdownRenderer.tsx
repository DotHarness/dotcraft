import { memo, useMemo, useState } from 'react'
import { Globe, Link2 } from 'lucide-react'
import { FileTypeIcon } from '../ui/FileTypeIcon'
import ReactMarkdown, { defaultUrlTransform } from 'react-markdown'
import remarkGfm from 'remark-gfm'
import type { Components } from 'react-markdown'
import { useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { openConversationLink } from '../../utils/conversationDeepLink'
import { basename } from '../../utils/path'
import { resolveConversationLink } from '../../../shared/viewer/linkResolver'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ReferencePathContextMenu } from './ReferencePathContextMenu'
import type { ContextMenuPosition } from '../ui/ContextMenu'
import { CodeBlock, HighlightedCode } from './MarkdownCodeBlock'
import { extractText } from './markdownText'

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
    >
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        components={customComponents}
        urlTransform={markdownUrlTransform}
      >
        {content}
      </ReactMarkdown>
    </div>
  )
})

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
          fontSize: '1.45rem',
          lineHeight: 1.24,
          fontWeight: 650,
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
          fontSize: '1.18rem',
          lineHeight: 1.3,
          fontWeight: 640,
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
          fontSize: '1.02rem',
          lineHeight: 1.34,
          fontWeight: 630,
          color: 'var(--text-primary)'
        }}
        {...props}
      >
        {children}
      </h3>
    )
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

  table({ children, ...props }) {
    return (
      <div style={{ overflowX: 'auto', margin: '8px 0 10px' }}>
        <table
          style={{
            borderCollapse: 'collapse',
            width: '100%',
            fontSize: 'var(--text-body-secondary-size)',
            lineHeight: 'var(--text-body-secondary-line-height)'
          }}
          {...props}
        >
          {children}
        </table>
      </div>
    )
  },

  th({ children, ...props }) {
    return (
      <th
        style={{
          padding: '8px 12px',
          borderBottom: '1px solid var(--border-active)',
          textAlign: 'left',
          fontWeight: 600,
          color: 'var(--text-primary)'
        }}
        {...props}
      >
        {children}
      </th>
    )
  },

  td({ children, ...props }) {
    return (
      <td
        style={{
          padding: '8px 12px',
          borderBottom: '1px solid var(--border-default)',
          color: 'var(--text-secondary)'
        }}
        {...props}
      >
        {children}
      </td>
    )
  }
}

type InlineReferenceKind = 'file' | 'browser' | 'external'

function InlineReferenceLink({
  href,
  children,
  workspacePath,
  remoteWorkspaceActive,
  activeThreadId,
  linkMode,
  t,
  ...props
}: React.AnchorHTMLAttributes<HTMLAnchorElement> & {
  workspacePath: string
  remoteWorkspaceActive: boolean
  activeThreadId: string | null
  linkMode: 'conversation' | 'external'
  t: (key: string) => string
}): JSX.Element {
  const [focused, setFocused] = useState(false)
  const [contextMenu, setContextMenu] = useState<{ position: ContextMenuPosition; targetPath: string } | null>(null)
  const presentation = useMemo(
    () => getInlineReferencePresentation(href, workspacePath, extractText(children)),
    [children, href, workspacePath]
  )

  async function handleClick(event: React.MouseEvent<HTMLAnchorElement>): Promise<void> {
    event.preventDefault()
    if (!href) return
    if (linkMode === 'external') {
      const externalUrl = resolveExternalMarkdownUrl(href)
      if (!externalUrl) return
      try {
        await window.api.shell.openExternal(externalUrl)
      } catch {
        // Ignore external handler failures in read-only markdown previews.
      }
      return
    }
    if (!href || !workspacePath || !activeThreadId) return
    if (remoteWorkspaceActive && presentation.kind === 'file') return
    await openConversationLink({
      target: href,
      workspacePath,
      threadId: activeThreadId,
      forceNew: event.ctrlKey || event.metaKey,
      t
    })
  }

  function handleContextMenu(event: React.MouseEvent<HTMLAnchorElement>): void {
    if (remoteWorkspaceActive) return
    if (linkMode !== 'conversation' || !presentation.absolutePath) return
    event.preventDefault()
    event.stopPropagation()
    setContextMenu({
      position: { x: event.clientX, y: event.clientY },
      targetPath: presentation.absolutePath
    })
  }

  const NonFileIcon = presentation.kind === 'browser' ? Globe : Link2

  const anchor = (
    <a
        href={href}
        onClick={(event) => { void handleClick(event) }}
        onContextMenu={handleContextMenu}
        onFocus={() => setFocused(true)}
        onBlur={() => setFocused(false)}
        data-inline-reference-kind={presentation.kind}
        // Shares the .dc-ref* chip rules (tokens.css) with the composer pills and
        // user-message refs, including their baseline alignment.
        className={`dc-ref ${presentation.kind === 'file' ? 'dc-ref-file' : 'dc-ref-link'}`}
        style={{
          margin: '0 4px',
          maxWidth: 'min(100%, var(--inline-reference-max-width))',
          fontSize: '12px',
          lineHeight: 1.25,
          textDecoration: 'none',
          cursor: href ? 'pointer' : 'default',
          boxShadow: focused ? '0 0 0 3px color-mix(in srgb, var(--accent) 22%, transparent)' : 'none'
        }}
        {...props}
      >
        {presentation.kind === 'file'
          ? <FileTypeIcon path={presentation.absolutePath ?? presentation.label} size={12} style={{ display: 'inline-block' }} />
          : <NonFileIcon size={12} strokeWidth={2.1} aria-hidden />}
        <span>{presentation.label}</span>
      </a>
  )

  return (
    <>
      {presentation.title ? (
        <ActionTooltip label={presentation.title}>{anchor}</ActionTooltip>
      ) : (
        anchor
      )}
      {contextMenu && (
        <ReferencePathContextMenu
          position={contextMenu.position}
          targetPath={contextMenu.targetPath}
          onClose={() => setContextMenu(null)}
        />
      )}
    </>
  )
}

function markdownUrlTransform(url: string, key: string): string | null | undefined {
  const trimmed = url.trim()
  if (key === 'href' && isLocalFileLinkTarget(trimmed)) return trimmed
  return defaultUrlTransform(url)
}

function isLocalFileLinkTarget(value: string): boolean {
  return value.toLowerCase().startsWith('file://') ||
    /^[A-Za-z]:[\\/]/.test(value) ||
    value.startsWith('/')
}

function resolveExternalMarkdownUrl(href: string): string | null {
  try {
    const parsed = new URL(href)
    if (
      parsed.protocol === 'http:' ||
      parsed.protocol === 'https:' ||
      parsed.protocol === 'mailto:' ||
      parsed.protocol === 'tel:'
    ) {
      return parsed.href
    }
  } catch {
    return null
  }
  return null
}

function getInlineReferencePresentation(
  href: string | undefined,
  workspacePath: string,
  childrenText: string
): { kind: InlineReferenceKind; label: string; title: string; absolutePath?: string } {
  const rawHref = href?.trim() ?? ''
  const childLabel = childrenText.trim()
  const hasCustomLabel = childLabel.length > 0 && childLabel !== rawHref
  const resolution = rawHref
    ? resolveConversationLink({ target: rawHref, workspacePath: workspacePath || '' })
    : { kind: 'reject' as const }

  if (resolution.kind === 'file') {
    return {
      kind: 'file',
      label: hasCustomLabel ? childLabel : basename(resolution.absolutePath),
      title: rawHref || resolution.absolutePath,
      ...(isAbsoluteLocalPath(resolution.absolutePath) ? { absolutePath: resolution.absolutePath } : {})
    }
  }

  if (resolution.kind === 'browser') {
    return {
      kind: 'browser',
      label: hasCustomLabel ? childLabel : shortenUrlForDisplay(resolution.url),
      title: rawHref || resolution.url
    }
  }

  if (resolution.kind === 'external') {
    return {
      kind: 'external',
      label: hasCustomLabel ? childLabel : shortenUrlForDisplay(resolution.url),
      title: rawHref || resolution.url
    }
  }

  return {
    kind: 'external',
    label: childLabel || rawHref,
    title: rawHref || childLabel
  }
}

function isAbsoluteLocalPath(value: string): boolean {
  return /^[A-Za-z]:[\\/]/.test(value) || value.startsWith('/') || value.startsWith('\\\\')
}

function shortenUrlForDisplay(rawUrl: string): string {
  try {
    const parsed = new URL(rawUrl)
    const path = parsed.pathname === '/' ? '' : parsed.pathname.replace(/\/+$/, '')
    if (!path) return parsed.hostname
    const compactPath = path.length <= 18
      ? path
      : `/${path.split('/').filter(Boolean)[0] ?? ''}`
    return compactPath ? `${parsed.hostname}${compactPath}` : parsed.hostname
  } catch {
    return rawUrl
  }
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
