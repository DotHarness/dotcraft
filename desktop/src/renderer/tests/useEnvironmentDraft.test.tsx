import { describe, expect, it, vi } from 'vitest'
import { act, renderHook, waitFor } from '@testing-library/react'
import { useEnvironmentDraft } from '../components/settings/panels/worktrees/useEnvironmentDraft'
import { installDesktopApiMock } from './desktopApiMock'

describe('useEnvironmentDraft', () => {
  it('keeps unsaved edits when the listed environment name changes', async () => {
    const readEnvironment = vi.fn().mockResolvedValue({
      ok: true,
      revision: 'rev-1',
      document: { name: '', actions: [] }
    })
    installDesktopApiMock({ projectActions: { readEnvironment } })
    const { result, rerender } = renderHook(
      ({ name }) => useEnvironmentDraft('C:/projects/app', 'C:/projects/app/.craft/environments/environment.json', name),
      { initialProps: { name: 'app' } }
    )
    await waitFor(() => expect(result.current.load.status).toBe('ready'))

    act(() => result.current.update((draft) => ({ ...draft, setup: { ...draft.setup, default: 'npm ci' } })))
    rerender({ name: 'renamed on disk' })

    expect(readEnvironment).toHaveBeenCalledTimes(1)
    expect(result.current.dirty).toBe(true)
    expect(result.current.draft.setup.default).toBe('npm ci')
  })
})
