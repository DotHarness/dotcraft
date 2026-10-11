import { AssistantMessageContent } from '@renderer/components/conversation/AssistantMessageContent'
import { ConversationColumn } from '@renderer/components/conversation/ConversationColumn'
import { UserMessageBlock } from '@renderer/components/conversation/UserMessageBlock'
import { comparedExamples } from './content/generativeUiAnswers'
import { toolExamples } from './content/generativeUiTools'
import type { Exchange } from './content/types'
import { visualizationExamples } from './content/visualizations'
import { ShowcaseNote } from './notes'
import { lang, SHOWCASE_THREAD_ID, SHOWCASE_TURN_ID, type Feature, type Selection } from './params'

export function exchangeFor(feature: Feature, { example, mode }: Selection): Exchange | null {
  if (feature === 'visualization') return visualizationExamples[example]?.exchange[lang] ?? null
  const compared = comparedExamples[example]?.[lang]
  if (compared) return { prompt: compared.prompt, answer: mode === 'plain' ? compared.plain : compared.interactive }
  return toolExamples[example]?.[lang] ?? null
}

export function defaultExample(feature: Feature): string {
  return feature === 'visualization' ? 'energy' : 'dinner'
}

export function ConversationExample({ feature, selection }: { feature: Feature; selection: Selection }): JSX.Element | null {
  const exchange = exchangeFor(feature, selection)
  if (!exchange) return null
  const itemId = `showcase-${selection.example}-${selection.mode}`
  return (
    <div className="showcase-conversation">
      <ConversationColumn className="dc-conversation-column-stack">
        <UserMessageBlock key={`${itemId}-prompt`} messageId={`${itemId}-prompt`} text={exchange.prompt} />
        <div key={itemId} className="showcase-answer">
          <AssistantMessageContent text={exchange.answer} streaming={false} threadId={SHOWCASE_THREAD_ID} turnId={SHOWCASE_TURN_ID} itemId={itemId} />
        </div>
        <ShowcaseNote />
      </ConversationColumn>
    </div>
  )
}
