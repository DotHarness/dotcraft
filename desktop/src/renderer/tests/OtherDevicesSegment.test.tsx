import { act, fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { OtherDevicesSegment } from '../components/settings/panels/otherDevices/OtherDevicesSegment'
import { LocaleProvider } from '../contexts/LocaleContext'
import { usePairedComputersStore } from '../stores/pairedComputersStore'

type PairResult = Awaited<ReturnType<typeof window.api.pairedComputers.pair>>
const pair = vi.fn<typeof window.api.pairedComputers.pair>()
const cancelPairing = vi.fn()
const OLD_LINK = `dotcraft://pair?v=1&id=old&name=Old&port=47610&fp=${'a'.repeat(64)}&addr=192.0.2.1&code=old`
const NEW_LINK = OLD_LINK.replace('id=old&name=Old', 'id=new&name=New').replace('code=old', 'code=new')

beforeEach(() => {
  pair.mockReset()
  cancelPairing.mockReset()
  Object.defineProperty(window, 'api', {
    configurable: true,
    value: {
      initialLocale: 'en',
      pairedComputers: {
        list: async () => [],
        refresh: async () => {},
        onEvent: () => () => {},
        pair,
        cancelPairing
      }
    }
  })
  usePairedComputersStore.getState().resetFlow()
  usePairedComputersStore.setState({ loaded: true, error: null, computers: [], pendingLink: null })
})

async function openDialog(): Promise<void> {
  await act(async () => {
    render(
      <LocaleProvider loadSettings={false}>
        <OtherDevicesSegment onSubPageChange={() => {}} />
      </LocaleProvider>
    )
  })
  fireEvent.click(screen.getByRole('button', { name: 'Add device' }))
}

function receiveLink(link: string): void {
  act(() => usePairedComputersStore.getState().applyEvent({ kind: 'pairingLink', link }))
}

describe('OtherDevicesSegment pairing links', () => {
  it('replaces an open failed dialog without pairing, including a repeated link after editing', async () => {
    pair.mockResolvedValue({ ok: false, code: 'pairingCodeInvalid' })
    await openDialog()
    fireEvent.change(screen.getByRole('textbox'), { target: { value: OLD_LINK } })
    await act(async () => fireEvent.click(screen.getByRole('button', { name: 'Continue' })))
    expect(screen.getByRole('alert')).toBeInTheDocument()

    receiveLink(NEW_LINK)

    expect(screen.getByRole('textbox')).toHaveValue(NEW_LINK)
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(pair).toHaveBeenCalledTimes(1)
    fireEvent.change(screen.getByRole('textbox'), { target: { value: OLD_LINK } })

    receiveLink(NEW_LINK)

    expect(screen.getByRole('textbox')).toHaveValue(NEW_LINK)
    expect(pair).toHaveBeenCalledTimes(1)
    await act(async () => fireEvent.click(screen.getByRole('button', { name: 'Continue' })))
    expect(pair).toHaveBeenLastCalledWith(NEW_LINK)
  })

  it('cancels a pending pairing and ignores its result after a replacement flow starts', async () => {
    let resolveOld!: (result: PairResult) => void
    pair.mockReturnValueOnce(new Promise((resolve) => { resolveOld = resolve }))
      .mockReturnValueOnce(new Promise(() => {}))
    await openDialog()
    fireEvent.change(screen.getByRole('textbox'), { target: { value: OLD_LINK } })
    fireEvent.click(screen.getByRole('button', { name: 'Continue' }))
    act(() => usePairedComputersStore.getState().applyEvent({ kind: 'pairingWaiting', name: 'Old' }))

    receiveLink(NEW_LINK)

    expect(cancelPairing).toHaveBeenCalledOnce()
    expect(screen.getByRole('textbox')).toHaveValue(NEW_LINK)
    expect(pair).toHaveBeenCalledTimes(1)
    fireEvent.click(screen.getByRole('button', { name: 'Continue' }))
    expect(pair).toHaveBeenLastCalledWith(NEW_LINK)
    await act(async () => resolveOld({
      ok: true,
      value: {
        computerId: 'old', name: 'Old', status: 'connected', version: '0.10.4',
        requiredVersion: null, lastConnectedAt: null, projects: []
      }
    }))

    expect(usePairedComputersStore.getState().flow).toEqual({ step: 'connecting', name: 'New' })
    expect(usePairedComputersStore.getState().computers).toEqual([])
  })
})
