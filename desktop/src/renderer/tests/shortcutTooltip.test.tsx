import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { ActionTooltip } from '../components/ui/ActionTooltip'
import { IconButton } from '../components/ui/IconButton'
import { ShortcutBadge } from '../components/ui/ShortcutBadge'
import { ACTION_SHORTCUTS, formatShortcutParts } from '../components/ui/shortcutKeys'
import { MessageCopyButton } from '../components/conversation/MessageCopyButton'
import { ModelPicker } from '../components/conversation/ModelPicker'
import { ImageViewer } from '../components/detail/viewers/ImageViewer'
import { LocaleProvider } from '../contexts/LocaleContext'

beforeEach(() => {
  Object.defineProperty(window, 'api', {
    configurable: true,
    value: {
      settings: {
        get: vi.fn().mockResolvedValue({ locale: 'en' })
      }
    }
  })
})

describe('shortcut formatting', () => {
  it('formats Mod as Ctrl on Windows and Linux', () => {
    expect(formatShortcutParts(ACTION_SHORTCUTS.toggleDetailPanel, 'Win32')).toEqual([
      'Ctrl',
      'Shift',
      'B'
    ])
    expect(formatShortcutParts(ACTION_SHORTCUTS.settings, 'Linux x86_64')).toEqual(['Ctrl', ','])
  })

  it('formats Mod as Cmd on macOS', () => {
    expect(formatShortcutParts(ACTION_SHORTCUTS.quickOpen, 'MacIntel')).toEqual(['Cmd', 'P'])
  })
})

describe('ActionTooltip', () => {
  it('keeps the global tooltip layer above modal overlays and toasts', () => {
    const tokensCss = readFileSync(resolve(__dirname, '../styles/tokens.css'), 'utf8')

    expect(tokensCss).toContain('--z-tooltip: 40000;')
    expect(tokensCss).toContain('.dc-action-tooltip--multiline')
    expect(tokensCss).toContain('max-width: min(32rem, calc(100vw - 16px));')
    expect(tokensCss).toContain('overflow-wrap: anywhere;')
  })

  it('renders an action label with shortcut keycaps on hover', async () => {
    render(
      <ActionTooltip label="Show viewer panel" shortcut={ACTION_SHORTCUTS.toggleDetailPanel}>
        <button type="button">Open</button>
      </ActionTooltip>
    )

    fireEvent.mouseEnter(screen.getByRole('button').parentElement as HTMLElement)

    const tooltip = await screen.findByRole('tooltip')
    expect(tooltip).toHaveTextContent('Show viewer panel')
    expect(tooltip).toHaveTextContent('Ctrl')
    expect(tooltip).toHaveTextContent('Shift')
    expect(tooltip).toHaveTextContent('B')
    expect(tooltip).not.toHaveAttribute('data-multiline')
    expect(tooltip).not.toHaveClass('dc-action-tooltip--multiline')
  })

  it('uses multiline tooltip styling only when requested', async () => {
    const longLabel =
      'Use ProfilerDriver.GetHierarchyFrameDataView(frame, threadIndex) and show the full path E:\\Git\\dotcraft\\captures\\deep-profile.trace.'

    render(
      <ActionTooltip label={longLabel} multiline>
        <button type="button">Explain</button>
      </ActionTooltip>
    )

    fireEvent.mouseEnter(screen.getByRole('button').parentElement as HTMLElement)

    const tooltip = await screen.findByRole('tooltip')
    expect(tooltip).toHaveTextContent(longLabel)
    expect(tooltip).toHaveAttribute('data-multiline', 'true')
    expect(tooltip).toHaveClass('dc-action-tooltip--multiline')
  })

  it('shows the disabled reason instead of shortcut keycaps', async () => {
    render(
      <ActionTooltip
        label="Commit file changes"
        shortcut={ACTION_SHORTCUTS.send}
        disabledReason="No changes to commit"
      >
        <button type="button" disabled>
          Commit
        </button>
      </ActionTooltip>
    )

    fireEvent.mouseEnter(screen.getByRole('button').parentElement as HTMLElement)

    expect(await screen.findByRole('tooltip')).toHaveTextContent('No changes to commit')
    expect(screen.getByRole('tooltip')).not.toHaveTextContent('Enter')
  })

  it('renders alternate shortcut groups and hides them behind disabled reasons', async () => {
    const { rerender } = render(
      <ActionTooltip label="Accept" shortcut={ACTION_SHORTCUTS.send} alternateShortcuts={[['A'], ['Shift', 'A']]}>
        <button type="button">Accept</button>
      </ActionTooltip>
    )

    fireEvent.mouseEnter(screen.getByRole('button').parentElement as HTMLElement)

    expect(await screen.findByRole('tooltip')).toHaveTextContent('Accept')
    expect(screen.getByRole('tooltip')).toHaveTextContent('Enter')
    expect(screen.getByRole('tooltip')).toHaveTextContent('A')
    expect(screen.getByRole('tooltip')).toHaveTextContent('Shift')

    fireEvent.mouseLeave(screen.getByRole('button').parentElement as HTMLElement)
    rerender(
      <ActionTooltip
        label="Accept"
        shortcut={ACTION_SHORTCUTS.send}
        alternateShortcuts={[['A']]}
        disabledReason="Not available"
      >
        <button type="button" disabled>
          Accept
        </button>
      </ActionTooltip>
    )

    fireEvent.mouseEnter(screen.getByRole('button').parentElement as HTMLElement)

    expect(await screen.findByRole('tooltip')).toHaveTextContent('Not available')
    expect(screen.getByRole('tooltip')).not.toHaveTextContent('Enter')
    expect(screen.getByRole('tooltip')).not.toHaveTextContent('A')
  })
})

describe('ShortcutBadge', () => {
  it('supports an on-accent tone for primary buttons', () => {
    render(<ShortcutBadge shortcut={ACTION_SHORTCUTS.newThread} tone="onAccent" />)

    const badge = screen.getByText('Ctrl').parentElement
    expect(badge).toHaveStyle({ '--shortcut-text': 'var(--on-accent)' })
    expect(badge).toHaveStyle({ '--shortcut-border': 'color-mix(in srgb, var(--on-accent) 48%, transparent)' })
  })
})

describe('IconButton', () => {
  it('keeps the legacy label prop as the accessible name', () => {
    render(<IconButton icon={<span aria-hidden>R</span>} label="Refresh" />)

    expect(screen.getByRole('button', { name: 'Refresh' })).toBeInTheDocument()
  })
})

describe('migrated action tooltips', () => {
  it('MessageCopyButton uses ActionTooltip instead of a native title', async () => {
    render(
      <LocaleProvider>
        <MessageCopyButton getText={() => 'hello'} visible />
      </LocaleProvider>
    )

    const button = screen.getByRole('button', { name: 'Copy message' })
    expect(button).not.toHaveAttribute('title')

    fireEvent.mouseEnter(button.parentElement as HTMLElement)

    expect(await screen.findByRole('tooltip')).toHaveTextContent('Copy message')
  })

  it('ModelPicker uses ActionTooltip instead of a native title', async () => {
    render(
      <LocaleProvider>
        <ModelPicker
          modelName="Default"
          modelOptions={['Default', 'gpt-5']}
          triggerStyle={{}}
        />
      </LocaleProvider>
    )

    const button = screen.getByRole('button', { name: 'Select model' })
    expect(button).not.toHaveAttribute('title')

    fireEvent.mouseEnter(button.parentElement as HTMLElement)

    expect(await screen.findByRole('tooltip')).toHaveTextContent('Select model')
  })

  it('ImageViewer toolbar buttons use ActionTooltip instead of native titles', async () => {
    render(
      <LocaleProvider>
        <ImageViewer absolutePath="E:\\dotcraft\\sample.png" />
      </LocaleProvider>
    )

    const button = screen.getByRole('button', { name: 'Zoom out' })
    expect(button).not.toHaveAttribute('title')

    fireEvent.mouseEnter(button.parentElement as HTMLElement)

    expect(await screen.findByRole('tooltip')).toHaveTextContent('Zoom out')
  })
})
