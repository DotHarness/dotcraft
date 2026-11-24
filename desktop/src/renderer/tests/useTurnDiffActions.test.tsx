import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { GitApplyPatchResult } from '../../shared/gitApply'
import { useTurnDiffActions, type TurnPatchOutcome } from '../hooks/useTurnDiffActions'
import { useConversationStore } from '../stores/conversationStore'
import type { TurnFileChange } from '../types/turnDiff'
import { installDesktopApiMock } from './desktopApiMock'

const WORKSPACE = 'F:/work'
const applyPatch = vi.fn<(workspacePath: string, patchText: string, options: { reverse: boolean }) => Promise<GitApplyPatchResult>>()

function row(name: string, overrides: Partial<TurnFileChange> = {}): TurnFileChange {
  return {
    key: `turn-1::${name}`,
    turnId: 'turn-1',
    patchText: `diff --git a/${WORKSPACE}/${name}.ts b/${WORKSPACE}/${name}.ts`,
    truncated: false,
    diff: {
      filePath: `${name}.ts`,
      additions: 1,
      deletions: 1,
      diffHunks: [],
      status: 'written',
      isNewFile: false
    },
    ...overrides
  }
}

const reverted = (name: string): TurnFileChange => row(name, { diff: { ...row(name).diff, status: 'reverted' } })

function seed(...files: TurnFileChange[]): void {
  useConversationStore.setState({ turnDiffs: new Map([['turn-1', { turnId: 'turn-1', source: 'history', files }]]) })
}

const statuses = (): string[] =>
  useConversationStore.getState().turnDiffs.get('turn-1')?.files.map((file) => file.diff.status) ?? []

const appliedPatches = (): Array<[string, { reverse: boolean }]> =>
  applyPatch.mock.calls.map(([, patchText, options]) => [patchText, options])

function renderActions() {
  return renderHook(() => useTurnDiffActions(WORKSPACE)).result
}

beforeEach(() => {
  useConversationStore.getState().reset()
  applyPatch.mockReset().mockResolvedValue({ ok: true })
  installDesktopApiMock({ git: { applyPatch } })
})

describe('useTurnDiffActions', () => {
  it('marks a row reverted only after git applied its workspace-relative patch in reverse', async () => {
    let finish: (result: GitApplyPatchResult) => void = () => undefined
    applyPatch.mockReturnValue(new Promise((resolve) => { finish = resolve }))
    seed(row('a'))
    const actions = renderActions()

    let pending: Promise<TurnPatchOutcome> = Promise.resolve('reverted')
    act(() => { pending = actions.current.revert(['turn-1']) })
    await waitFor(() => expect(applyPatch).toHaveBeenCalledWith(WORKSPACE, 'diff --git a/a.ts b/a.ts', { reverse: true }))
    expect(statuses()).toEqual(['written'])

    let outcome: TurnPatchOutcome | undefined
    await act(async () => {
      finish({ ok: true })
      outcome = await pending
    })
    expect(outcome).toBe('reverted')
    expect(statuses()).toEqual(['reverted'])
  })

  it('reverts newest first and reports a partial revert when a later row fails', async () => {
    applyPatch
      .mockResolvedValueOnce({ ok: true })
      .mockResolvedValueOnce({ ok: false, code: 'apply-failed', message: 'conflict' })
    seed(row('a'), row('b'), row('c'))
    const actions = renderActions()

    const outcome = await act(() => actions.current.revert(['turn-1']))

    expect(outcome).toBe('partial')
    expect(appliedPatches()).toEqual([
      ['diff --git a/c.ts b/c.ts', { reverse: true }],
      ['diff --git a/b.ts b/b.ts', { reverse: true }]
    ])
    expect(statuses()).toEqual(['written', 'written', 'reverted'])
  })

  it('reports a failure that changed nothing, including a rejected bridge call', async () => {
    applyPatch.mockRejectedValue(new Error('bridge down'))
    seed(row('a'), row('b'))
    const actions = renderActions()

    const outcome = await act(() => actions.current.revert(['turn-1']))

    expect(outcome).toBe('failed')
    expect(applyPatch).toHaveBeenCalledTimes(1)
    expect(statuses()).toEqual(['written', 'written'])
  })

  it('reports a missing Git repository and leaves the rows as they are', async () => {
    applyPatch.mockResolvedValue({ ok: false, code: 'not-git-repo', message: 'not a git repository' })
    seed(row('a'))
    const actions = renderActions()

    const outcome = await act(() => actions.current.revert(['turn-1']))

    expect(outcome).toBe('not-git-repo')
    expect(statuses()).toEqual(['written'])
  })

  it('skips truncated rows and rows already in the requested state', async () => {
    seed(row('a', { truncated: true }), reverted('b'), row('c'))
    const actions = renderActions()

    const outcome = await act(() => actions.current.revert(['turn-1']))

    expect(outcome).toBe('reverted')
    expect(appliedPatches()).toEqual([['diff --git a/c.ts b/c.ts', { reverse: true }]])
  })

  it('undoes across turns in reverse order and reapplies them forward', async () => {
    seed(row('before'))
    const after = row('after', { turnId: 'turn-2', key: 'turn-2::after' })
    useConversationStore.setState(({ turnDiffs }) => ({
      turnDiffs: new Map([...turnDiffs, ['turn-2', { turnId: 'turn-2', source: 'history', files: [after] }]])
    }))
    const actions = renderActions()
    const ids = ['turn-1', 'turn-2']

    expect(await act(() => actions.current.revert(ids))).toBe('reverted')
    expect(await act(() => actions.current.reapply(ids))).toBe('reapplied')
    expect(appliedPatches()).toEqual([
      ['diff --git a/after.ts b/after.ts', { reverse: true }],
      ['diff --git a/before.ts b/before.ts', { reverse: true }],
      ['diff --git a/before.ts b/before.ts', { reverse: false }],
      ['diff --git a/after.ts b/after.ts', { reverse: false }]
    ])
    expect(statuses()).toEqual(['written'])
    expect(useConversationStore.getState().turnDiffs.get('turn-2')?.files[0].diff.status).toBe('written')
  })

})
