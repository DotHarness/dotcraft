import { useMemo } from 'react'
import { MarkdownRenderer } from './MarkdownRenderer'
import { VisualizationFrame } from './VisualizationFrame'
import { splitAssistantMessage } from './assistantMessageSegments'
import { GenerativeUiMessageBlock } from './generativeUi/GenerativeUiMessageBlock'
import { VisualizationNotice } from './VisualizationNotice'
import { useT } from '../../contexts/LocaleContext'

interface Props {
  text: string
  streaming: boolean
  threadId?: string
  turnId?: string
  itemId?: string
  turnRunning?: boolean
  readOnly?: boolean
}

export function AssistantMessageContent({ text, streaming, threadId, turnId, itemId, turnRunning = false, readOnly = false }: Props): JSX.Element {
  const t = useT()
  const source = useMemo(() => threadId && turnId && itemId ? { threadId, turnId, itemId } : null, [itemId, threadId, turnId])
  const segments = useMemo(
    () => splitAssistantMessage(text, { streaming, visualizations: streaming ? 'strip' : source ? 'render' : 'keep' }),
    [source, streaming, text]
  )
  return (
    <>
      {segments.map(segment => {
        if (segment.kind === 'markdown') return <MarkdownRenderer key={`markdown-${segment.start}`} content={segment.text} streaming={streaming} />
        if (segment.kind === 'invalidVisualization') {
          return (
            <VisualizationNotice
              key={`invalid-visual-${segment.start}`}
              message={t('visualization.invalidReference')}
              fixPrompt={t('visualization.fixReferencePrompt', { reference: segment.text })}
            />
          )
        }
        if (segment.kind === 'generativeUi') {
          return (
            <GenerativeUiMessageBlock
              key={`generative-ui-${segment.index}`}
              code={segment.code}
              closed={segment.closed}
              blockIndex={segment.index}
              source={source}
              live={streaming || turnRunning}
              locked={streaming || turnRunning || readOnly}
            />
          )
        }
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
