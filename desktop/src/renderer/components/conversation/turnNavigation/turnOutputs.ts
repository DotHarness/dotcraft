import { turnWrittenFiles } from '../../../stores/turnDiffs'
import type { ConversationItem, ConversationTurn } from '../../../types/conversation'
import type { TurnDiff } from '../../../types/turnDiff'
import { basename } from '../../../utils/path'
import { toTurnArtifact } from '../turnArtifactFiles'

export type TurnOutputKind = 'webPreview' | 'file' | 'image'

export interface TurnOutput {
  kind: TurnOutputKind
  label: string
}

const KIND_ORDER: readonly TurnOutputKind[] = ['webPreview', 'file', 'image']

function isGeneratedImage(item: ConversationItem): boolean {
  const status = item.imageGenerationStatus ?? (item.status === 'completed' ? 'completed' : undefined)
  return item.type === 'imageGeneration' && status === 'completed' && Boolean(item.result?.trim())
}

export function deriveTurnOutputs(turn: ConversationTurn, turnDiffs: ReadonlyMap<string, TurnDiff>): TurnOutput[] {
  if (turn.status !== 'completed') return []
  const outputs: TurnOutput[] = []
  for (const row of turnWrittenFiles(turnDiffs, turn.id)) {
    const artifact = toTurnArtifact(row.diff)
    if (artifact) {
      outputs.push({ kind: artifact.kind === 'html' ? 'webPreview' : 'file', label: basename(artifact.diff.filePath) })
    }
  }
  for (const item of turn.items) {
    if (isGeneratedImage(item)) outputs.push({ kind: 'image', label: '' })
  }
  const unique = new Map(outputs.map((output) => [`${output.kind}\u0000${output.label}`, output]))
  return [...unique.values()].sort((a, b) => KIND_ORDER.indexOf(a.kind) - KIND_ORDER.indexOf(b.kind))
}
