import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { PetEvent } from '../../shared/desktopPet'
import { DesktopPet } from '../components/desktopPet/DesktopPet'

const setUiLocale = vi.fn()
vi.mock('../contexts/LocaleContext', () => ({ useT: () => (key: string) => key, useSetUiLocale: () => setUiLocale }))
vi.mock('../utils/theme', () => ({ applyTheme: vi.fn() }))
vi.mock('@dotcraft/avatar/react', () => ({
  Avatar: () => <span>companion</span>,
  MASCOT_SLEEP_AFTER_MS: 60000,
  useComposerAvatarBehavior: ({ semanticPose }: { semanticPose: string }) => ({
    pose: semanticPose,
    expression: undefined,
    gesture: undefined,
    gestureSequence: 0,
    clearGesture: () => {},
    completeGesture: () => {}
  })
}))
vi.mock('../components/desktopPet/PetQuickChat', () => ({
  PetQuickChat: ({ text, onChange, onSubmit }: { text: string; onChange: (text: string) => void; onSubmit: () => void }) =>
    <><input aria-label="draft" value={text} onChange={event => onChange(event.target.value)} /><button onClick={onSubmit}>send</button></>
}))
let listener: (event: PetEvent) => void
const command = vi.fn(async () => {})
const originalApi = window.api
beforeEach(() => {
  command.mockClear()
  setUiLocale.mockClear()
  vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: false })))
  window.api = { desktopPet: { command, onEvent: callback => { listener = callback; return () => {} } } } as typeof window.api
})
afterEach(() => { vi.unstubAllGlobals(); window.api = originalApi })
const snapshot = { name: 'robot', text: 'Draft', theme: 'dark' as const, locale: 'en' as const, reducedMotion: true, canChat: true, editRevision: 0 }

describe('desktop pet quick chat session', () => {
  it('signals readiness once and keeps newer typing when an old snapshot arrives', () => {
    render(<DesktopPet />)
    act(() => listener({ type: 'snapshot', snapshot }))
    expect(setUiLocale).toHaveBeenCalledWith('en')
    act(() => listener({ type: 'position', point: { x: 200, y: 200 }, size: 59, phase: 'pet' }))
    fireEvent.click(screen.getByRole('button', { name: 'desktopPet.chat' }))
    expect(command.mock.calls.filter(([message]) => (message as any).type === 'ready')).toHaveLength(1)
    fireEvent.change(screen.getByLabelText('draft'), { target: { value: 'New draft' } })
    act(() => listener({ type: 'snapshot', snapshot }))
    expect(screen.getByLabelText('draft')).toHaveValue('New draft')
    fireEvent.click(screen.getByText('send'))
    expect(command).toHaveBeenLastCalledWith({ type: 'edit', text: 'New draft', submit: true, revision: 2 })
    act(() => listener({ type: 'snapshot', snapshot: { ...snapshot, text: '', editRevision: 2 } }))
    expect(screen.getByLabelText('draft')).toHaveValue('')
  })
  it('hangs the quick chat centred under the pet and clamps it at the screen edges', () => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 })
    const { container } = render(<DesktopPet />)
    act(() => listener({ type: 'snapshot', snapshot }))
    const left = (x: number): string => {
      act(() => listener({ type: 'position', point: { x, y: 200 }, size: 59, phase: 'pet' }))
      return container.querySelector<HTMLElement>('.desktop-pet-chat-position')!.style.left
    }
    act(() => listener({ type: 'position', point: { x: 200, y: 200 }, size: 59, phase: 'pet' }))
    fireEvent.click(screen.getByRole('button', { name: 'desktopPet.chat' }))
    // The 360px panel centres on the pet's midline at 229.5.
    expect(left(200)).toBe('49.5px')
    expect(left(4)).toBe('12px')
    expect(left(990)).toBe('652px')
  })
  it('routes approval work back to the desktop without submitting a decision', () => {
    render(<DesktopPet />)
    act(() => listener({ type: 'snapshot', snapshot: { ...snapshot, canChat: false } }))
    act(() => listener({ type: 'position', point: { x: 200, y: 200 }, size: 59, phase: 'pet' }))
    fireEvent.click(screen.getByRole('button', { name: 'desktopPet.chat' }))
    expect(command).toHaveBeenLastCalledWith({ type: 'return' })
    expect(screen.queryByLabelText('draft')).not.toBeInTheDocument()
  })
})
