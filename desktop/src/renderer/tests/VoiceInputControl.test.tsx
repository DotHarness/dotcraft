import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import type { VoiceRuntimeSnapshot } from '../../shared/voice'
import { VoiceInputControl } from '../components/conversation/VoiceInputControl'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useVoiceStore } from '../voice/voiceStore'

const INSTALLED_SNAPSHOT: VoiceRuntimeSnapshot = {
  model: { phase: 'installed', bytesDownloaded: 1, bytesTotal: 1 },
  sessions: [],
  capacity: 2
}

describe('VoiceInputControl pointer interaction', () => {
  const startRecording = vi.fn<() => Promise<void>>()
  const stopRecording = vi.fn<() => Promise<void>>()

  beforeEach(() => {
    vi.useFakeTimers()
    startRecording.mockReset().mockResolvedValue(undefined)
    stopRecording.mockReset().mockResolvedValue(undefined)
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        initialLocale: 'en',
        settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
        voice: {}
      }
    })
    useVoiceStore.setState({
      initialized: true,
      snapshot: INSTALLED_SNAPSHOT,
      recording: null,
      finalizing: null,
      microphonePermission: 'granted',
      localErrors: {},
      initialize: vi.fn(),
      startRecording: startRecording as never,
      stopRecording: stopRecording as never,
      cancelRecordingStart: vi.fn()
    })
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('keeps a short pointer press as click-to-toggle', () => {
    renderControl()
    const button = screen.getByRole('button', { name: 'Click to dictate or hold' })

    fireEvent.pointerDown(button, { button: 0, pointerId: 1 })
    act(() => vi.advanceTimersByTime(149))
    fireEvent.pointerUp(button, { button: 0, pointerId: 1 })
    fireEvent.click(button)

    expect(startRecording).toHaveBeenCalledTimes(1)
    expect(stopRecording).not.toHaveBeenCalled()
  })

  it('starts after 150 ms and stops once when the hold is released', async () => {
    startRecording.mockImplementation(async () => {
      useVoiceStore.setState({
        recording: { threadId: 'thread-1', startedAt: 0, elapsedMs: 0, level: 0 }
      })
    })
    renderControl()
    const button = screen.getByRole('button', { name: 'Click to dictate or hold' })

    fireEvent.pointerDown(button, { button: 0, pointerId: 2 })
    await act(async () => { vi.advanceTimersByTime(150); await Promise.resolve() })
    fireEvent.pointerUp(button, { button: 0, pointerId: 2 })
    fireEvent.click(button)

    expect(startRecording).toHaveBeenCalledTimes(1)
    expect(stopRecording).toHaveBeenCalledTimes(1)
  })

  it('stops after a pending hold start resolves', async () => {
    const pending = deferred<void>()
    startRecording.mockImplementation(async () => {
      await pending.promise
      useVoiceStore.setState({
        recording: { threadId: 'thread-1', startedAt: 0, elapsedMs: 0, level: 0 }
      })
    })
    renderControl()
    const button = screen.getByRole('button', { name: 'Click to dictate or hold' })

    fireEvent.pointerDown(button, { button: 0, pointerId: 3 })
    act(() => vi.advanceTimersByTime(150))
    fireEvent.pointerUp(button, { button: 0, pointerId: 3 })
    pending.resolve()
    await act(async () => { await pending.promise; await Promise.resolve() })

    expect(stopRecording).toHaveBeenCalledTimes(1)
  })

  it('recovers when only a stale local queue error remains', () => {
    useVoiceStore.setState({ localErrors: { 'thread-1': 'queue-full' } })
    renderControl()

    expect(screen.getByRole('button', { name: 'Click to dictate or hold' })).toBeEnabled()
  })
})

function renderControl(): void {
  render(<LocaleProvider><VoiceInputControl threadId="thread-1" /></LocaleProvider>)
}

function deferred<T>(): { promise: Promise<T>; resolve(value?: T): void } {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => { resolve = res })
  return { promise, resolve }
}
