import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { VoiceRuntimeSnapshot } from '../../shared/voice'
import { VoicePanel } from '../components/settings/panels/VoicePanel'
import { ConfirmDialogHost } from '../components/ui/ConfirmDialog'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useVoiceStore } from '../voice/voiceStore'

const settingsGet = vi.fn()
const settingsSet = vi.fn()
const getSnapshot = vi.fn()
const installModel = vi.fn()
const removeModel = vi.fn()
const getMicrophonePermissionStatus = vi.fn()
const requestMicrophonePermission = vi.fn()
const openMicrophoneSettings = vi.fn()
const getUserMedia = vi.fn()

beforeEach(() => {
  settingsGet.mockResolvedValue({ locale: 'en', voice: {} })
  settingsSet.mockResolvedValue(undefined)
  getSnapshot.mockResolvedValue(snapshot('missing'))
  installModel.mockResolvedValue(undefined)
  removeModel.mockResolvedValue(undefined)
  getMicrophonePermissionStatus.mockResolvedValue('not-determined')
  requestMicrophonePermission.mockResolvedValue('granted')
  openMicrophoneSettings.mockResolvedValue(undefined)
  getUserMedia.mockResolvedValue({ getTracks: () => [{ stop: vi.fn() }] })
  useVoiceStore.setState({
    initialized: false,
    finalizing: null,
    microphonePermission: 'unknown',
    deviceFallback: false,
    localErrors: {}
  })
  Object.defineProperty(navigator, 'mediaDevices', {
    configurable: true,
    value: {
      enumerateDevices: vi.fn().mockResolvedValue([
        { kind: 'audioinput', deviceId: 'mic-1', label: 'Desk microphone (1532:0537)' }
      ]),
      getUserMedia,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn()
    }
  })
  Object.defineProperty(window, 'api', {
    configurable: true,
    value: {
      settings: { get: settingsGet, set: settingsSet },
      voice: {
        getSnapshot,
        getMicrophonePermissionStatus,
        requestMicrophonePermission,
        openMicrophoneSettings,
        installModel,
        cancelModelInstall: vi.fn(),
        removeModel,
        repairModel: vi.fn(),
        onSnapshot: vi.fn(() => () => {}),
        onSessionEvent: vi.fn(() => () => {})
      }
    }
  })
})

describe('VoicePanel', () => {
  it('shows only microphone and model lifecycle controls', async () => {
    renderPanel()
    expect(await screen.findByRole('button', { name: 'Install' })).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Microphone' })).toBeInTheDocument()
    expect(screen.queryByText('Input level')).not.toBeInTheDocument()
    expect(screen.queryByText('Dictation')).not.toBeInTheDocument()
    expect(screen.queryByText('Ready')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Install' }))
    await waitFor(() => expect(installModel).toHaveBeenCalledTimes(1))
  })

  it('confirms removal through the shared dialog', async () => {
    getSnapshot.mockResolvedValue(snapshot('installed'))
    renderPanel()
    fireEvent.click(await screen.findByRole('button', { name: 'Remove' }))
    expect(screen.getByRole('dialog', { name: 'Remove Whisper Multilingual?' })).toBeInTheDocument()
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Remove' }))
    await waitFor(() => expect(removeModel).toHaveBeenCalledTimes(1))
  })

  it('keeps download details in Settings', async () => {
    getSnapshot.mockResolvedValue({
      ...snapshot('downloading'),
      model: { phase: 'downloading', bytesDownloaded: 50, bytesTotal: 100 }
    })
    renderPanel()
    expect(await screen.findByRole('progressbar', { name: 'Downloading Whisper…' })).toHaveAttribute('value', '50')
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument()
  })

  it('requests access before opening the device menu and refreshes real labels', async () => {
    useVoiceStore.setState({
      localErrors: { 'thread-1': 'device-missing', 'thread-2': 'queue-full' }
    })
    renderPanel()
    const select = await screen.findByRole('combobox', { name: 'Microphone' })
    fireEvent.click(select)

    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
    expect(await screen.findByRole('option', { name: 'Desk microphone' })).toBeInTheDocument()
    expect(screen.queryByText(/1532:0537/)).not.toBeInTheDocument()
    expect(requestMicrophonePermission).toHaveBeenCalledTimes(1)
    expect(getUserMedia).toHaveBeenCalledTimes(1)
    expect(useVoiceStore.getState().localErrors).toEqual({
      'thread-1': undefined,
      'thread-2': 'queue-full'
    })
  })

  it('shows a working recovery action when access is blocked', async () => {
    getMicrophonePermissionStatus.mockResolvedValue('denied')
    renderPanel()

    const action = await screen.findByRole('button', { name: 'Open system settings' })
    expect(screen.getByRole('combobox', { name: 'Microphone' })).toBeDisabled()
    fireEvent.click(action)
    await waitFor(() => expect(openMicrophoneSettings).toHaveBeenCalledTimes(1))
  })
})

function renderPanel(): void {
  render(
    <LocaleProvider>
      <ConfirmDialogHost />
      <VoicePanel />
    </LocaleProvider>
  )
}

function snapshot(phase: VoiceRuntimeSnapshot['model']['phase']): VoiceRuntimeSnapshot {
  return {
    model: { phase, bytesDownloaded: 0, bytesTotal: null },
    sessions: [],
    capacity: 2
  }
}
