import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ViewerHeader } from '../components/detail/ViewerHeader'
import { useUIStore } from '../stores/uiStore'

vi.mock('../components/conversation/OpenTargetButton', () => ({
  OpenTargetButton: ({ variant }: { variant?: string }) => (
    <button type="button" data-testid="viewer-open-target" data-variant={variant}>
      Open target
    </button>
  )
}))

vi.mock('../components/detail/ViewerActionsMenu', () => ({
  ViewerActionsMenu: () => <button type="button">Viewer actions</button>
}))

const settingsGet = vi.fn()

describe('ViewerHeader', () => {
  beforeEach(() => {
    settingsGet.mockResolvedValue({ locale: 'en' })
    useUIStore.setState({ explorerVisible: false })
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet }
      }
    })
  })

  it('uses the outline intent for the open-file compound action', () => {
    render(
      <LocaleProvider>
        <ViewerHeader
          absolutePath="/workspace/example/README.md"
          relativePath="README.md"
          isText
          wordWrap
          onToggleWordWrap={vi.fn()}
        />
      </LocaleProvider>
    )

    expect(screen.getByTestId('viewer-open-target')).toHaveAttribute('data-variant', 'outline')
  })
})
