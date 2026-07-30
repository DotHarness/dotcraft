import { beforeEach, describe, expect, it } from 'vitest'

import { useAppNavigationStore, type AppNavigationLocation } from '../stores/appNavigationStore'
import { useUIStore } from '../stores/uiStore'
import { handleAppNavigationShortcut } from '../utils/appNavigationShortcut'

const first: AppNavigationLocation = {
  kind: 'conversation',
  threadId: null,
  detailVisible: false,
  activeDetailTab: { kind: 'launcher' },
  selectedChangedFile: null
}

function shortcut(key: '[' | ']', target?: HTMLElement, isComposing = false): KeyboardEvent {
  const event = new KeyboardEvent('keydown', {
    key,
    ctrlKey: true,
    cancelable: true,
    bubbles: true,
    isComposing
  })
  ;(target ?? window).dispatchEvent(event)
  return event
}

beforeEach(() => {
  useAppNavigationStore.getState().reset(first)
  useAppNavigationStore.getState().push({ kind: 'settings', tab: 'general' })
  useUIStore.setState({ quickOpenVisible: false })
})

describe('handleAppNavigationShortcut', () => {
  it('moves backward and forward on Windows/Linux', () => {
    const back = shortcut('[')
    expect(handleAppNavigationShortcut(back, 'win32')).toBe(true)
    expect(back.defaultPrevented).toBe(true)
    expect(useAppNavigationStore.getState().index).toBe(0)

    const forward = shortcut(']')
    expect(handleAppNavigationShortcut(forward, 'linux')).toBe(true)
    expect(useAppNavigationStore.getState().index).toBe(1)
  })

  it('does not register the shortcut on macOS', () => {
    const event = shortcut('[')
    expect(handleAppNavigationShortcut(event, 'darwin')).toBe(false)
    expect(event.defaultPrevented).toBe(false)
    expect(useAppNavigationStore.getState().index).toBe(1)
  })

  it('blocks navigation while composing or inside a modal', () => {
    const composing = shortcut('[', undefined, true)
    expect(handleAppNavigationShortcut(composing, 'win32')).toBe(false)

    const dialog = document.createElement('div')
    dialog.setAttribute('role', 'dialog')
    const input = document.createElement('input')
    dialog.appendChild(input)
    document.body.appendChild(dialog)
    const modalEvent = shortcut('[', input)
    expect(handleAppNavigationShortcut(modalEvent, 'win32')).toBe(true)
    expect(useAppNavigationStore.getState().index).toBe(1)
    dialog.remove()
  })

  it('blocks navigation while Quick Open is visible', () => {
    useUIStore.setState({ quickOpenVisible: true })
    expect(handleAppNavigationShortcut(shortcut('['), 'win32')).toBe(true)
    expect(useAppNavigationStore.getState().index).toBe(1)
  })
})
