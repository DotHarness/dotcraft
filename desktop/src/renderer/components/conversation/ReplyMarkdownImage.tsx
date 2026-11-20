import { useContext, useState } from 'react'
import { resolveConversationLink } from '../../../shared/viewer/linkResolver'
import { isRemoteProjectKey } from '../../../shared/workspaceProjectKey'
import { useConversationStore } from '../../stores/conversationStore'
import { buildViewerUrlRenderer } from '../../utils/viewerUrl'
import { ImageReadScopeContext } from './imagePreview/galleryImages'

interface ImageProps {
  src?: string
  alt?: string
  title?: string
}

export function ReplyMarkdownImage({ src, alt = '', title }: ImageProps): JSX.Element | string | null {
  const scope = useContext(ImageReadScopeContext)
  const remoteWorkspace = useConversationStore((state) => state.remoteWorkspaceActive)
  if (!src) return alt || null
  if (/^(?:https?:\/\/|\/\/)/i.test(src)) return <img src={src} alt={alt} title={title} />
  const local = !remoteWorkspace && !isRemoteProjectKey(scope?.source?.connectionKey)
  const url = local && scope?.workspacePath ? workspaceImageUrl(src, scope.workspacePath) : null
  return url ? <WorkspaceImage key={url} src={url} alt={alt} title={title} /> : alt || null
}

function workspaceImageUrl(target: string, workspacePath: string): string | null {
  const normalized = workspacePath.replace(/\\/g, '/')
  const root = normalized.replace(/\/+$/, '')
  const windowsPath = /^(?:[a-z]:\/|\/\/)/i.test(normalized)
  const resolved = resolveConversationLink({ target, workspacePath })
  if (resolved.kind !== 'file') return null
  const path = resolved.absolutePath
  const inside = windowsPath
    ? path.toLowerCase().startsWith(root.toLowerCase() + '/')
    : path.startsWith(root + '/')
  return inside ? `${buildViewerUrlRenderer(path)}?workspaceImage=${encodeURIComponent(workspacePath)}` : null
}

function WorkspaceImage({ src, alt, title }: ImageProps): JSX.Element | string | null {
  const [failed, setFailed] = useState(false)
  return failed ? alt || null : (
    <img className="dc-reply-markdown-image" src={src} alt={alt} title={title} loading="lazy" onError={() => setFailed(true)} />
  )
}
