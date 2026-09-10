import { afterEach, describe, expect, it, vi } from 'vitest'
import type { PetEvent } from '../../shared/desktopPet'
import { onDesktopPetPresentationChange } from '../components/desktopPet/petPresentation'
import { conversationRenderPaused, isDesktopWindowBackgrounded } from '../utils/conversationRenderPause'

const shown = { minimized: false, visible: true, focused: true }
const hiddenWindow = { minimized: false, visible: false, focused: false }
const originalApi = window.api

afterEach(() => {
  document.documentElement.removeAttribute('data-desktop-pet-detached')
  Object.defineProperty(document, 'hidden', { configurable: true, value: false })
  window.api = originalApi
})

describe('conversationRenderPaused', () => {
  it('pauses for a hidden or backgrounded window', () => {
    expect(conversationRenderPaused(shown)).toBe(false)
    expect(conversationRenderPaused(hiddenWindow)).toBe(true)
    expect(isDesktopWindowBackgrounded({ ...shown, minimized: true })).toBe(true)
    Object.defineProperty(document, 'hidden', { configurable: true, value: true })
    expect(conversationRenderPaused(shown)).toBe(true)
  })
  it('never pauses while the desktop pet presents the thread from the hidden window', () => {
    document.documentElement.setAttribute('data-desktop-pet-detached', '')
    Object.defineProperty(document, 'hidden', { configurable: true, value: true })
    expect(conversationRenderPaused(hiddenWindow)).toBe(false)
  })
  it('reports ownership changes and nothing else', () => {
    let listener: ((event: PetEvent) => void) | undefined
    const off = vi.fn()
    window.api = { desktopPet: { command: vi.fn(), onEvent: (callback: (event: PetEvent) => void) => { listener = callback; return off } } } as unknown as typeof window.api
    const presenting = vi.fn()
    const unsubscribe = onDesktopPetPresentationChange(presenting)
    listener!({ type: 'ownership', detached: true })
    listener!({ type: 'return-seat' })
    listener!({ type: 'ownership', detached: false })
    expect(presenting.mock.calls).toEqual([[true], [false]])
    unsubscribe()
    expect(off).toHaveBeenCalledTimes(1)
    window.api = {} as typeof window.api
    expect(() => onDesktopPetPresentationChange(presenting)()).not.toThrow()
  })
})
