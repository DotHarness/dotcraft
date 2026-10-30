import { useMemo, useState, type AnchorHTMLAttributes, type MouseEvent } from 'react'
import { Globe, Link2 } from 'lucide-react'
import { FileTypeIcon } from '../ui/FileTypeIcon'
import { openConversationLink } from '../../utils/conversationDeepLink'
import { basename } from '../../utils/path'
import { referenceLocation } from '../../utils/referencePaths'
import type { OpenTargetLocation } from '../../../shared/openTargets'
import { parseForgeRefUrl } from '../../../shared/forgeReferences'
import { isPreviewableUrl } from '../../../shared/linkPreviews'
import { resolveConversationLink, type LinkNavigationHint } from '../../../shared/viewer/linkResolver'
import { ActionTooltip } from '../ui/ActionTooltip'
import type { ContextMenuPosition } from '../ui/ContextMenu'
import { ReferencePathContextMenu } from './ReferencePathContextMenu'
import { extractText } from './markdownText'
import { ForgeReferenceChip } from './forgeReferences/ForgeReferenceChip'
import { WebLinkPreviewAnchor } from './linkPreviews/WebLinkPreview'

type InlineReferenceKind = 'file' | 'browser' | 'external'

const REFERENCE_TOOLTIP_DELAY_MS = 200

export function InlineReferenceLink({
  href,
  children,
  workspacePath,
  remoteWorkspaceActive,
  activeThreadId,
  linkMode,
  t,
  ...props
}: AnchorHTMLAttributes<HTMLAnchorElement> & {
  workspacePath: string
  remoteWorkspaceActive: boolean
  activeThreadId: string | null
  linkMode: 'conversation' | 'external'
  t: (key: string, vars?: Record<string, string | number>) => string
}): JSX.Element {
  const [contextMenu, setContextMenu] = useState<{ position: ContextMenuPosition; targetPath: string; location: OpenTargetLocation | null } | null>(null)
  const presentation = useMemo(
    () => getInlineReferencePresentation(href, workspacePath, extractText(children), t),
    [children, href, t, workspacePath]
  )
  const forgeTarget = useMemo(() => (linkMode === 'conversation' ? parseForgeRefUrl(href) : null), [href, linkMode])
  const previewed = linkMode === 'conversation' && !forgeTarget && presentation.kind === 'browser' && isPreviewableUrl(href)

  async function openLink(target: string, event: MouseEvent<HTMLElement>): Promise<void> {
    if (linkMode === 'conversation' && event.detail !== 0 && selectionOverlapsReference(event.currentTarget)) return
    if (linkMode === 'external') {
      const externalUrl = resolveExternalMarkdownUrl(target)
      if (!externalUrl) return
      await window.api.shell.openExternal(externalUrl).catch(() => undefined)
      return
    }
    if (!workspacePath || !activeThreadId) return
    if (remoteWorkspaceActive && presentation.kind === 'file') return
    await openConversationLink({
      target,
      workspacePath,
      threadId: activeThreadId,
      forceNew: event.ctrlKey || event.metaKey,
      t
    })
  }

  function handleClick(event: MouseEvent<HTMLAnchorElement>): void {
    event.preventDefault()
    if (href) void openLink(href, event)
  }

  function handleContextMenu(event: MouseEvent<HTMLAnchorElement>): void {
    if (remoteWorkspaceActive) return
    if (linkMode !== 'conversation' || !presentation.absolutePath) return
    event.preventDefault()
    event.stopPropagation()
    setContextMenu({
      position: { x: event.clientX, y: event.clientY },
      targetPath: presentation.absolutePath,
      location: presentation.location ?? null
    })
  }

  const linkProps: AnchorHTMLAttributes<HTMLAnchorElement> & Record<`data-${string}`, string | undefined> = {
    href,
    onClick: handleClick,
    onContextMenu: handleContextMenu,
    draggable: linkMode === 'conversation' ? false : undefined,
    onDragStart: linkMode === 'conversation' ? (event) => event.preventDefault() : undefined,
    'data-conversation-reference': linkMode === 'conversation' ? '' : undefined,
    'data-inline-reference-kind': presentation.kind,
    ...props
  }

  const NonFileIcon = presentation.kind === 'browser' ? Globe : Link2
  const anchorClassName = `dc-ref ${presentation.kind === 'file' ? 'dc-ref-file' : 'dc-ref-link'}`
  const anchorStyle = {
    margin: '0 4px',
    maxWidth: 'min(100%, var(--inline-reference-max-width))',
    cursor: href ? 'pointer' : 'default'
  }
  const anchorContent = (
    <>
      {presentation.kind === 'file'
        ? <FileTypeIcon path={presentation.absolutePath ?? presentation.label} size={12} style={{ display: 'inline-block' }} />
        : <NonFileIcon size={12} strokeWidth={2.1} aria-hidden />}
      <span>{presentation.label}</span>
    </>
  )

  const anchor = forgeTarget ? (
    <ForgeReferenceChip
      target={forgeTarget}
      href={href ?? ''}
      text={extractText(children)}
      linkProps={linkProps}
      onOpenLink={(url, event) => { void openLink(url, event) }}
    />
  ) : previewed && href ? (
    <WebLinkPreviewAnchor
      href={href}
      className={anchorClassName}
      style={anchorStyle}
      linkProps={linkProps}
      onOpenLink={(url, event) => { void openLink(url, event) }}
    >
      {anchorContent}
    </WebLinkPreviewAnchor>
  ) : (
    <a className={anchorClassName} style={anchorStyle} {...linkProps}>
      {anchorContent}
    </a>
  )

  return (
    <>
      {presentation.title && !forgeTarget && !previewed ? (
        <ActionTooltip
          label={presentation.title}
          wrapperClassName={linkMode === 'conversation' ? 'dc-conversation-reference-tooltip' : undefined}
          openDelayMs={REFERENCE_TOOLTIP_DELAY_MS}
        >
          {anchor}
        </ActionTooltip>
      ) : (
        anchor
      )}
      {contextMenu && (
        <ReferencePathContextMenu
          position={contextMenu.position}
          targetPath={contextMenu.targetPath}
          location={contextMenu.location}
          onClose={() => setContextMenu(null)}
        />
      )}
    </>
  )
}

function selectionOverlapsReference(anchor: Element): boolean {
  const selection = anchor.ownerDocument.getSelection()
  if (!selection || selection.isCollapsed) return false
  for (let index = 0; index < selection.rangeCount; index++) {
    if (selection.getRangeAt(index).intersectsNode(anchor)) return true
  }
  return false
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
  childrenText: string,
  t: (key: string, vars?: Record<string, string | number>) => string
): { kind: InlineReferenceKind; label: string; title: string; absolutePath?: string; location?: OpenTargetLocation | null } {
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
      title: fileReferenceTitle(rawHref || resolution.absolutePath, resolution.hint, t),
      ...(isAbsoluteLocalPath(resolution.absolutePath)
        ? { absolutePath: resolution.absolutePath, location: referenceLocation(resolution.hint) }
        : {})
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

function fileReferenceTitle(
  path: string,
  hint: LinkNavigationHint | undefined,
  t: (key: string, vars?: Record<string, string | number>) => string
): string {
  if (!hint?.line) return path
  const written = path.replace(/(?:#L\d+(?:C\d+)?(?:-L?\d+(?:C\d+)?)?|:\d+(?::\d+|-\d+)?)$/, '')
  const parts = [
    hint.endLine && hint.endLine !== hint.line
      ? t('conversation.reference.lines', { start: hint.line, end: hint.endLine })
      : t('conversation.reference.line', { line: hint.line })
  ]
  if (hint.column) parts.push(t('conversation.reference.column', { column: hint.column }))
  return `${written} (${parts.join(', ')})`
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

