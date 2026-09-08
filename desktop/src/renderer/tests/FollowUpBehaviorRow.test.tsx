import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { FollowUpBehaviorRow } from '../components/settings/panels/FollowUpBehaviorRow'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useComposerPreferencesStore } from '../stores/composerPreferencesStore'
import { useToastStore } from '../stores/toastStore'
import { installDesktopApiMock } from './desktopApiMock'

const settingsSet = vi.fn()

describe('Follow-up behavior preference', () => {
  beforeEach(() => {
    settingsSet.mockReset().mockResolvedValue(undefined)
    installDesktopApiMock({ settings: { get: async () => ({ locale: 'en' }), set: settingsSet } })
    useComposerPreferencesStore.setState({ followUpQueueMode: 'steer', saving: false })
    useToastStore.setState({ toasts: [] })
  })

  it.each([
    [undefined, 'Steer'], ['queue', 'Queue']
  ])('restores %s as %s', (stored, selected) => {
    useComposerPreferencesStore.getState().hydrate({ followUpQueueMode: stored })
    render(<LocaleProvider><FollowUpBehaviorRow /></LocaleProvider>)
    expect(screen.getByRole('button', { name: selected })).toHaveAttribute('aria-pressed', 'true')
  })

  it('applies the selection only after saving succeeds', async () => {
    let finishSave!: () => void
    settingsSet.mockImplementation(() => new Promise<void>((resolve) => { finishSave = resolve }))
    render(<LocaleProvider><FollowUpBehaviorRow /></LocaleProvider>)
    fireEvent.click(screen.getByRole('button', { name: 'Queue' }))
    expect(settingsSet).toHaveBeenCalledWith({ followUpQueueMode: 'queue' })
    expect(screen.getByRole('button', { name: 'Steer' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: 'Queue' })).toBeDisabled()
    await act(async () => { finishSave() })
    expect(screen.getByRole('button', { name: 'Queue' })).toHaveAttribute('aria-pressed', 'true')
  })

  it('preserves the selection and reports failed saves', async () => {
    settingsSet.mockRejectedValue(new Error('settings unavailable'))
    render(<LocaleProvider><FollowUpBehaviorRow /></LocaleProvider>)
    fireEvent.click(screen.getByRole('button', { name: 'Queue' }))
    await waitFor(() => expect(useToastStore.getState().toasts).toEqual([
      expect.objectContaining({ message: 'Failed to save follow-up behavior: settings unavailable' })
    ]))
    expect(screen.getByRole('button', { name: 'Steer' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: 'Queue' })).not.toBeDisabled()
  })
})
