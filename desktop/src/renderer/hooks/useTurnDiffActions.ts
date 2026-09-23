import { useMemo } from 'react'
import type { GitApplyPatchResult } from '../../shared/gitApply'
import { useConversationStore } from '../stores/conversationStore'
import type { TurnFileChange } from '../types/turnDiff'
import { toWorkspaceRelativePatch } from '../utils/unifiedDiff'

export type TurnPatchOutcome = 'reverted' | 'reapplied' | 'partial' | 'failed' | 'not-git-repo'

export interface TurnDiffActions {
  revertTurn(turnId: string): Promise<TurnPatchOutcome>
  reapplyTurn(turnId: string): Promise<TurnPatchOutcome>
}

const needsApply = (change: TurnFileChange, reverse: boolean): boolean =>
  !change.truncated && change.diff.status !== (reverse ? 'reverted' : 'written')

export function useTurnDiffActions(workspacePath: string): TurnDiffActions {
  return useMemo(() => {
    async function apply(change: TurnFileChange, reverse: boolean): Promise<GitApplyPatchResult> {
      const result = await window.api.git
        .applyPatch(workspacePath, toWorkspaceRelativePatch(change.patchText, workspacePath), { reverse })
        .catch((error: unknown): GitApplyPatchResult => ({
          ok: false,
          code: 'apply-failed',
          message: error instanceof Error ? error.message : String(error)
        }))
      if (result.ok) {
        useConversationStore.getState().setTurnFileStatus(change.turnId, change.key, reverse ? 'reverted' : 'written')
      }
      return result
    }

    // A later row can edit lines an earlier row of the same file wrote, so revert walks newest to oldest.
    async function applyTurn(turnId: string, reverse: boolean): Promise<TurnPatchOutcome> {
      const rows = useConversationStore.getState().turnDiffs.get(turnId)?.files ?? []
      const pending = (reverse ? [...rows].reverse() : rows).filter((row) => needsApply(row, reverse))
      for (const [done, row] of pending.entries()) {
        const result = await apply(row, reverse)
        if (result.ok) continue
        if (result.code === 'not-git-repo') return 'not-git-repo'
        return done > 0 ? 'partial' : 'failed'
      }
      return reverse ? 'reverted' : 'reapplied'
    }

    return {
      revertTurn: (turnId) => applyTurn(turnId, true),
      reapplyTurn: (turnId) => applyTurn(turnId, false)
    }
  }, [workspacePath])
}
