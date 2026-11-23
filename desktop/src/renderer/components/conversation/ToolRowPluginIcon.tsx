import { useState, type JSX } from 'react'
import type { ConversationItem } from '../../types/conversation'
import type { ToolRowIcon } from '../../utils/toolCallDisplay'
import { useToolPluginIconUrl } from '../../utils/mcpPluginIcon'
import { ToolRowGlyph } from './ToolRowGlyph'

export function ToolRowPluginIcon({
  item,
  fallback,
  threadId,
  live
}: {
  item: Pick<ConversationItem, 'type' | 'source'>
  fallback?: ToolRowIcon
  threadId?: string | null
  live?: boolean
}): JSX.Element | null {
  const iconUrl = useToolPluginIconUrl(item, threadId)
  const [failedSrc, setFailedSrc] = useState<string | null>(null)

  if (iconUrl && failedSrc !== iconUrl) {
    return (
      <img
        className="dc-tool-row-plugin-icon"
        src={iconUrl}
        alt=""
        draggable={false}
        aria-hidden
        onError={() => setFailedSrc(iconUrl)}
      />
    )
  }

  if (!fallback) return null
  return <ToolRowGlyph icon={fallback} live={live} />
}
