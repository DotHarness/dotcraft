import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { PetEvent } from '../../shared/desktopPet'
import { DesktopPet } from '../components/desktopPet/DesktopPet'

vi.mock('../contexts/LocaleContext', () => ({ useT: () => (key: string) => key }))
vi.mock('../utils/theme', () => ({ applyTheme: vi.fn() }))
vi.mock('@dotcraft/avatar/react', () => ({ Avatar: () => <span>companion</span> }))
vi.mock('../components/desktopPet/PetQuickChat', () => ({
  PetQuickChat: ({ text, onChange, onSubmit }: { text: string; onChange: (text: string) => void; onSubmit: () => void }) =>
    <><input aria-label="draft" value={text} onChange={event => onChange(event.target.value)} /><button onClick={onSubmit}>send</button></>
}))
let listener: (event: PetEvent) => void
const command = vi.fn(async () => {})
const originalApi = window.api
beforeEach(() => {
  command.mockClear()
  window.api = { desktopPet: { command, onEvent: callback => { listener = callback; return () => {} } } } as typeof window.api
})
afterEach(() => { window.api = originalApi })
const snapshot = { name: 'robot', text: 'Draft', theme: 'dark' as const, reducedMotion: true, canChat: true, editRevision: 0 }

describe('desktop pet quick chat session', () => {
  it('signals readiness once and keeps newer typing when an old snapshot arrives', () => {
    render(<DesktopPet />)
    act(() => listener({ type: 'snapshot', snapshot }))
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
  it('routes approval work back to the desktop without submitting a decision', () => {
    render(<DesktopPet />)
    act(() => listener({ type: 'snapshot', snapshot: { ...snapshot, canChat: false } }))
    act(() => listener({ type: 'position', point: { x: 200, y: 200 }, size: 59, phase: 'pet' }))
    fireEvent.click(screen.getByRole('button', { name: 'desktopPet.chat' }))
    expect(command).toHaveBeenLastCalledWith({ type: 'return' })
    expect(screen.queryByLabelText('draft')).not.toBeInTheDocument()
  })
})
