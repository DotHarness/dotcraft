import { createContext, useContext } from 'react'
import { useConversationStore } from '../../stores/conversationStore'
import { useSubAgentTranscriptStore } from '../../stores/subAgentTranscriptStore'
import type { FileDiff } from '../../types/toolCall'

const NO_DIFFS: ReadonlyMap<string, FileDiff> = new Map()

export const TranscriptThreadContext = createContext<string | null>(null)

export function useTranscriptItemDiff(itemId: string, enabled: boolean): FileDiff | undefined {
  const childThreadId = useContext(TranscriptThreadContext)
  const conversationDiff = useConversationStore((s) =>
    enabled && childThreadId == null ? s.itemDiffs.get(itemId) : undefined
  )
  const transcriptDiff = useSubAgentTranscriptStore((s) =>
    enabled && childThreadId != null ? s.transcripts.get(childThreadId)?.itemDiffs.get(itemId) : undefined
  )
  return childThreadId == null ? conversationDiff : transcriptDiff
}

export function useTranscriptItemDiffs(): ReadonlyMap<string, FileDiff> {
  const childThreadId = useContext(TranscriptThreadContext)
  const conversationDiffs = useConversationStore((s) => s.itemDiffs)
  const transcriptDiffs = useSubAgentTranscriptStore((s) =>
    childThreadId == null ? undefined : s.transcripts.get(childThreadId)?.itemDiffs
  )
  return childThreadId == null ? conversationDiffs : transcriptDiffs ?? NO_DIFFS
}

export function useTranscriptWorkspacePath(): string {
  const childThreadId = useContext(TranscriptThreadContext)
  const conversationPath = useConversationStore((s) => s.workspacePath)
  const transcriptPath = useSubAgentTranscriptStore((s) =>
    childThreadId == null ? '' : s.transcripts.get(childThreadId)?.workspacePath ?? ''
  )
  return transcriptPath || conversationPath
}
