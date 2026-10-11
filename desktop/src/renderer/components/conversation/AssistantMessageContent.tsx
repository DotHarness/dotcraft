import { useMemo } from 'react'
import { MarkdownRenderer } from './MarkdownRenderer'
import { VisualizationFrame } from './VisualizationFrame'
import { splitAssistantMessage } from './assistantMessageSegments'

interface Props {
  text: string
  streaming: boolean
  threadId?: string
  turnId?: string
  itemId?: string
}

export function AssistantMessageContent({ text, streaming, threadId, turnId, itemId }: Props): JSX.Element {
  const source = useMemo(() => threadId && turnId && itemId ? { threadId, turnId, itemId } : null, [itemId, threadId, turnId])
  const segments = useMemo(
    () => splitAssistantMessage(text, { streaming, visualizations: streaming ? 'strip' : source ? 'render' : 'keep' }),
    [source, streaming, text]
  )
  return (
    <>
      {segments.map(segment => {
        if (segment.kind === 'markdown') return <MarkdownRenderer key={`markdown-${segment.start}`} content={segment.text} />
        return (
          <VisualizationFrame
            key={`visual-${segment.start}-${segment.reference.path}`}
            threadId={source!.threadId}
            turnId={source!.turnId}
            itemId={source!.itemId}
            path={segment.reference.path}
            title={segment.reference.title}
            mode={segment.reference.mode}
          />
        )
      })}
    </>
  )
}
