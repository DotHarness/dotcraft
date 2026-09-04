import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { WhatsNewDialog } from '../components/whats-new/WhatsNewDialog'
import {
  getWhatsNewMediaStateKey,
  type WhatsNewMediaState
} from '../../shared/whatsNew'
import { WHATS_NEW_TEST_RELEASE_0_1_7, WHATS_NEW_TEST_RELEASES } from './whatsNewFixtures'
import { installDesktopApiMock } from './desktopApiMock'

const settingsGet = vi.fn()
const openExternal = vi.fn()

function installApi(locale = 'en'): void {
  settingsGet.mockResolvedValue({ locale })
  installDesktopApiMock({
    settings: { get: settingsGet },
    shell: { openExternal }
  })
}

function renderDialog(props: Partial<Parameters<typeof WhatsNewDialog>[0]> = {}) {
  const onClose = props.onClose ?? vi.fn()
  const result = render(
    <LocaleProvider>
      <WhatsNewDialog
        releases={props.releases ?? WHATS_NEW_TEST_RELEASES}
        mediaStates={props.mediaStates ?? {}}
        onClose={onClose}
      />
    </LocaleProvider>
  )
  return { ...result, onClose }
}

function readyMediaStates(): Record<string, WhatsNewMediaState> {
  const states: Record<string, WhatsNewMediaState> = {}
  for (const release of WHATS_NEW_TEST_RELEASES) {
    for (const card of release.cards) {
      if (!card.media) continue
      states[getWhatsNewMediaStateKey(release.version, card.id)] = {
        releaseVersion: release.version,
        cardId: card.id,
        status: 'ready',
        cachedUrl: `file:///tmp/whats-new/${card.id}.gif`
      }
    }
  }
  return states
}

describe('WhatsNewDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    installApi()
  })

  it('renders the active release cards with skeleton fallback media', () => {
    renderDialog()

    expect(screen.getByRole('dialog', { name: "What's New in DotCraft" })).toBeInTheDocument()
    expect(screen.getByText('Highlights from v0.1.6')).toBeInTheDocument()
    expect(screen.getByText('Background Channels')).toBeInTheDocument()
    expect(screen.getByText('Dreams')).toBeInTheDocument()
    expect(screen.getByText('Goal')).toBeInTheDocument()
    expect(screen.getAllByRole('img', { name: 'Downloading preview...' })).toHaveLength(3)
  })

  it('swaps ready cached media in and falls back to a skeleton from failed images', () => {
    renderDialog({ mediaStates: readyMediaStates() })

    const images = document.body.querySelectorAll('img')
    expect(images).toHaveLength(3)

    fireEvent.error(images[0])

    expect(document.body.querySelectorAll('img')).toHaveLength(2)
    expect(screen.getAllByRole('img', { name: 'Downloading preview...' })).toHaveLength(1)
  })

  it('closes from the primary action', () => {
    const { onClose } = renderDialog()

    fireEvent.click(screen.getByRole('button', { name: "Let's Start" }))

    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('paginates between releases via footer nav when more than one release is present', () => {
    renderDialog({
      releases: [WHATS_NEW_TEST_RELEASE_0_1_7, ...WHATS_NEW_TEST_RELEASES]
    })

    expect(screen.getByText('Highlights from v0.1.7')).toBeInTheDocument()
    expect(screen.getByText('Agent Builder')).toBeInTheDocument()
    expect(screen.queryByText('Background Channels')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Newer/ })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Older (v0.1.6)' }))

    expect(screen.getByText('Highlights from v0.1.6')).toBeInTheDocument()
    expect(screen.getByText('Background Channels')).toBeInTheDocument()
    expect(screen.queryByText('Agent Builder')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Older/ })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Newer (v0.1.7)' }))

    expect(screen.getByText('Highlights from v0.1.7')).toBeInTheDocument()
    expect(screen.getByText('Agent Builder')).toBeInTheDocument()
  })

  it('renders no pagination buttons for a single release', () => {
    renderDialog()

    expect(screen.queryByRole('button', { name: /Older/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Newer/ })).not.toBeInTheDocument()
  })
})
