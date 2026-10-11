import { createContext } from 'react'
import type { ConversationTurn } from '../../types/conversation'
import { parseVisualizationContentReferences } from './assistantMessageSegments'
import { normalizeVisualizationPath } from './visualizationParser'

export interface VisualizationEntry {
  key: string
  threadId: string
  turnId: string
  itemId: string
  path: string
  title?: string
  mode?: 'wide'
}

export const ConversationVisualizationsContext = createContext<(() => VisualizationEntry[]) | null>(null)

export function collectConversationVisualizations(turns: ConversationTurn[]): VisualizationEntry[] {
  const entries: VisualizationEntry[] = []
  for (const turn of turns) {
    for (const item of turn.items) {
      if (item.type !== 'agentMessage' || item.status !== 'completed' || !item.text) continue
      parseVisualizationContentReferences(item.text).forEach(({ path, title, mode }, index) => {
        entries.push({ key: `${item.id}:${index}`, threadId: turn.threadId, turnId: turn.id, itemId: item.id, path, title, mode })
      })
    }
  }
  return entries
}

export function galleryForVisualization(
  entry: Omit<VisualizationEntry, 'key'>,
  getConversationVisualizations: (() => VisualizationEntry[]) | null
): { entries: VisualizationEntry[]; index: number } {
  const entries = getConversationVisualizations?.() ?? []
  const path = normalizeVisualizationPath(entry.path)
  const index = entries.findIndex(candidate =>
    candidate.threadId === entry.threadId && candidate.itemId === entry.itemId && normalizeVisualizationPath(candidate.path) === path)
  return index >= 0 ? { entries, index } : { entries: [{ ...entry, key: `${entry.itemId}:${path}` }], index: 0 }
}
