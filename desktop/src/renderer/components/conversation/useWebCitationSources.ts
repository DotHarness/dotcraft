import { createContext, useContext, useMemo } from 'react'
import type { ConversationTurn } from '../../types/conversation'
import { useConversationStore } from '../../stores/conversationStore'
import {
  collectWebCitationSources,
  hasWebCitationMarkers,
  NO_WEB_CITATION_SOURCES,
  type WebCitationSources
} from '../../utils/webCitations'

export const TranscriptTurnsContext = createContext<readonly ConversationTurn[] | null>(null)

export function useWebCitationSources(turn: ConversationTurn, streamingMessage: string): WebCitationSources {
  const cites = hasWebCitationMarkers(streamingMessage)
    || turn.items.some((item) => item.type === 'agentMessage' && hasWebCitationMarkers(item.text))
  const transcriptTurns = useContext(TranscriptTurnsContext)
  const conversationTurns = useConversationStore((s) => cites && transcriptTurns == null ? s.turns : null)
  const turns = transcriptTurns ?? conversationTurns
  return useMemo(() => {
    if (!cites) return NO_WEB_CITATION_SOURCES
    const index = turns?.findIndex((candidate) => candidate.id === turn.id) ?? -1
    const earlier = turns && index >= 0 ? turns.slice(0, index) : []
    return collectWebCitationSources([...earlier, turn])
  }, [cites, turn, turns])
}
