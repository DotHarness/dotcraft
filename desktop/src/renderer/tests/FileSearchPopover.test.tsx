import { act, fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { FileSearchPopover } from '../components/conversation/FileSearchPopover'
import { installDesktopApiMock } from './desktopApiMock'

describe('FileSearchPopover', () => {
  it('drills into a folder with Tab and references it with Enter', async () => {
    installDesktopApiMock({
      settings: { get: async () => ({ locale: 'en' }) },
      workspace: {
        searchFiles: async () => ({
          files: [{ kind: 'dir', name: 'docs', relativePath: 'docs', dir: '', positions: [0, 1, 2, 3] }],
          indexStatus: 'ready'
        })
      }
    })
    const onSelect = vi.fn()
    const onComplete = vi.fn()
    render(
      <LocaleProvider>
        <FileSearchPopover query="docs" visible workspacePath="/workspace" onSelect={onSelect} onComplete={onComplete} onDismiss={vi.fn()} />
      </LocaleProvider>
    )
    await screen.findByRole('option')

    act(() => { fireEvent.keyDown(window, { key: 'Tab' }) })
    expect(onComplete).toHaveBeenCalledWith('docs/')
    expect(onSelect).not.toHaveBeenCalled()

    act(() => { fireEvent.keyDown(window, { key: 'Enter' }) })
    expect(onSelect).toHaveBeenCalledWith('docs/')
  })
})
