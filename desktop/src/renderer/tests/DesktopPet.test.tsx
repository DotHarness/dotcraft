import { act, fireEvent, render, screen } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { PetEvent, PetSnapshot, PetStatusInfo } from '../../shared/desktopPet'
import { DesktopPet } from '../components/desktopPet/DesktopPet'

const setUiLocale = vi.fn()
const avatarMocks = vi.hoisted(() => ({
  idleOptions: null as null | { enabled: boolean },
  activeIdle: null as null | { motion: string; phase: string; direction: string; travel: string }
}))
vi.mock('../contexts/LocaleContext', () => ({ useT: () => (key: string) => key, useSetUiLocale: () => setUiLocale }))
vi.mock('../utils/theme', () => ({ applyTheme: vi.fn() }))
vi.mock('@dotcraft/avatar/react', () => ({
  Avatar: () => <span>companion</span>,
  MascotIdleStage: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  MASCOT_SLEEP_AFTER_MS: 60000,
  useComposerAvatarBehavior: ({ semanticPose }: { semanticPose: string }) => ({
    pose: semanticPose,
    expression: undefined,
    gesture: undefined,
    gestureSequence: 0,
    clearGesture: () => {},
    completeGesture: () => {}
  }),
  useMascotActiveIdle: (options: { enabled: boolean }) => {
    avatarMocks.idleOptions = options
    const activeIdle = avatarMocks.activeIdle
    return {
      activeIdle,
      activityRevision: 0,
      cancel: () => {},
      onAnimationEnd: () => {},
      className: activeIdle ? 'composer-mascot-active-idle' : undefined,
      attributes: activeIdle
        ? {
            'data-mascot-active-idle': activeIdle.motion,
            'data-mascot-idle-phase': activeIdle.phase,
            'data-mascot-idle-direction': activeIdle.direction,
            'data-mascot-idle-travel': activeIdle.travel
          }
        : {}
    }
  }
}))
vi.mock('../components/desktopPet/PetQuickChat', () => ({
  PetQuickChat: ({ text, busy, followUpMode, onChange, onSubmit, onStop }: {
    text: string; busy?: boolean; followUpMode: string; onChange: (text: string) => void; onSubmit: () => void; onStop: () => void
  }) => <>
    <input aria-label="draft" value={text} disabled={busy} onChange={event => onChange(event.target.value)} />
    <button onClick={onSubmit}>send</button><button onClick={onStop}>stop</button><span data-testid="follow-up">{followUpMode}</span>
  </>
}))
let listener: (event: PetEvent) => void
const command = vi.fn(async () => {})
const originalApi = window.api
beforeEach(() => {
  command.mockClear()
  setUiLocale.mockClear()
  avatarMocks.idleOptions = null
  avatarMocks.activeIdle = null
  vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: false })))
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 })
  Object.defineProperty(window, 'innerHeight', { configurable: true, value: 768 })
  window.api = { desktopPet: { command, onEvent: callback => { listener = callback; return () => {} } } } as typeof window.api
})
afterEach(() => { vi.unstubAllGlobals(); window.api = originalApi })
const snapshot: PetSnapshot = {
  name: 'robot', text: 'Draft', theme: 'dark', locale: 'en', reducedMotion: true, canChat: true, followUpMode: 'steer', editRevision: 0
}
const running: PetStatusInfo = { status: 'running', title: 'Fix the build', line: 'Running npm test', lineTone: 'neutral', turnId: 'turn-1', canStop: true }
const sent = (type: string): Array<Record<string, unknown>> =>
  command.mock.calls.map(([message]) => message as unknown as Record<string, unknown>).filter(message => message.type === type)
function settle(extra: Partial<PetSnapshot> = {}, point = { x: 200, y: 200 }): void {
  act(() => listener({ type: 'snapshot', snapshot: { ...snapshot, ...extra } }))
  act(() => listener({ type: 'position', point, size: 59, phase: 'pet' }))
}
const toggle = (): void => fireEvent.click(screen.getByRole('button', { name: /desktopPet\.activity\.(show|hide)/ }))

describe('desktop pet quick chat session', () => {
  it('signals readiness once and keeps newer typing when an old snapshot arrives', () => {
    render(<DesktopPet />)
    settle()
    expect(setUiLocale).toHaveBeenCalledWith('en')
    toggle()
    expect(sent('ready')).toHaveLength(1)
    fireEvent.change(screen.getByLabelText('draft'), { target: { value: 'New draft' } })
    act(() => listener({ type: 'snapshot', snapshot }))
    expect(screen.getByLabelText('draft')).toHaveValue('New draft')
    fireEvent.click(screen.getByText('send'))
    expect(command).toHaveBeenLastCalledWith({ type: 'edit', text: 'New draft', submit: true, revision: 2 })
    act(() => listener({ type: 'snapshot', snapshot: { ...snapshot, text: '', editRevision: 2 } }))
    expect(screen.getByLabelText('draft')).toHaveValue('')
    expect(sent('return')).toHaveLength(0)
  })
  it('keeps the quick chat open but disabled while the source is busy', () => {
    render(<DesktopPet />)
    settle()
    toggle()
    act(() => listener({ type: 'snapshot', snapshot: { ...snapshot, busy: true } }))
    expect(screen.getByLabelText('draft')).toBeDisabled()
    act(() => listener({ type: 'snapshot', snapshot: { ...snapshot, busy: false } }))
    expect(screen.getByLabelText('draft')).toBeEnabled()
  })
  it('offers the way back to the desktop instead of a draft when the source cannot chat', () => {
    render(<DesktopPet />)
    settle({ canChat: false })
    toggle()
    expect(screen.queryByLabelText('draft')).not.toBeInTheDocument()
    const back = screen.getAllByRole('button', { name: 'desktopPet.return' })
    fireEvent.click(back[back.length - 1])
    expect(command).toHaveBeenLastCalledWith({ type: 'return' })
  })
})

describe('desktop pet activity pill', () => {
  it('opens on its own when the source starts working and shows the status line', () => {
    const { container } = render(<DesktopPet />)
    settle()
    expect(container.querySelector('.desktop-pet-pill')).toBeNull()
    settle({ status: running })
    const pill = container.querySelector<HTMLElement>('.desktop-pet-pill')!
    expect(pill).toHaveAttribute('data-status', 'running')
    expect(pill.querySelector('.desktop-pet-pill-title')).toHaveTextContent('Fix the build')
    const line = pill.querySelector<HTMLElement>('.desktop-pet-pill-line')!
    expect(line).toHaveTextContent('Running npm test')
    expect(line).toHaveClass('tool-running-gradient-text')
    expect(line).not.toHaveAttribute('data-wrap')
    settle({ status: { ...running, status: 'failed', line: 'spawn ENOENT', lineTone: 'danger', canStop: false } })
    expect(container.querySelector('.desktop-pet-pill-line')).toHaveAttribute('data-wrap', 'true')
    expect(container.querySelector('.desktop-pet-pill-line')).not.toHaveClass('tool-running-gradient-text')
  })
  it('stops the turn through the composer control and treats closing a Ready pill as having looked', () => {
    const { container } = render(<DesktopPet />)
    settle({ status: running })
    expect(container.querySelectorAll('.desktop-pet-pill-head button')).toHaveLength(1)
    expect(screen.getByTestId('follow-up')).toHaveTextContent('steer')
    fireEvent.click(screen.getByText('stop'))
    expect(command).toHaveBeenLastCalledWith({ type: 'stop', turnId: 'turn-1' })
    fireEvent.click(screen.getByRole('button', { name: 'desktopPet.activity.dismiss' }))
    expect(sent('read')).toHaveLength(0)
    settle({ status: { ...running, status: 'review', line: 'All done', lineTone: 'success', canStop: false } })
    expect(container.querySelector('.desktop-pet-pill')).toHaveAttribute('data-status', 'review')
    fireEvent.click(screen.getByRole('button', { name: 'desktopPet.activity.dismiss' }))
    expect(sent('read')).toEqual([{ type: 'read', turnId: 'turn-1' }])
  })
  it('keeps a way to stop when the source cannot chat', () => {
    render(<DesktopPet />)
    settle({ canChat: false, status: running })
    expect(screen.queryByLabelText('draft')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'composer.stopAria' })).toBeEnabled()
    settle({ canChat: false, status: { ...running, canStop: false, stopping: true } })
    expect(screen.getByRole('button', { name: 'composer.stoppingAria' })).toBeDisabled()
  })
  it('stays dismissed for the same turn until it needs more attention', () => {
    const { container } = render(<DesktopPet />)
    settle({ status: running })
    fireEvent.click(screen.getByRole('button', { name: 'desktopPet.activity.dismiss' }))
    expect(container.querySelector('.desktop-pet-pill')).toBeNull()
    settle({ status: { ...running, line: 'Editing a.ts' } })
    expect(container.querySelector('.desktop-pet-pill')).toBeNull()
    settle({ status: { ...running, status: 'waiting', line: 'run npm test', lineTone: 'warning' } })
    expect(container.querySelector('.desktop-pet-pill')).toHaveAttribute('data-status', 'waiting')
    fireEvent.click(screen.getByRole('button', { name: 'desktopPet.activity.dismiss' }))
    settle({ status: { ...running, turnId: 'turn-2' } })
    expect(container.querySelector('.desktop-pet-pill')).toHaveAttribute('data-status', 'running')
  })
  it('walks Escape from the pill to the desktop', () => {
    const { container } = render(<DesktopPet />)
    settle({ status: running })
    expect(container.querySelector('.desktop-pet-pill')).not.toBeNull()
    fireEvent.keyDown(window, { key: 'Escape' })
    expect(container.querySelector('.desktop-pet-pill')).toBeNull()
    expect(sent('return')).toHaveLength(0)
    fireEvent.keyDown(window, { key: 'Escape' })
    expect(command).toHaveBeenLastCalledWith({ type: 'return' })
  })
  it('flips above the pet when there is no room below and reports its height', () => {
    Object.defineProperty(window, 'innerHeight', { configurable: true, value: 400 })
    const rect = vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue(
      { height: 120, width: 315, x: 0, y: 0, top: 0, left: 0, right: 315, bottom: 120, toJSON: () => ({}) } as DOMRect
    )
    const { container } = render(<DesktopPet />)
    settle({ status: running }, { x: 300, y: 320 })
    const surface = container.querySelector<HTMLElement>('.desktop-pet-pill-position')!
    expect(sent('layout').at(-1)).toEqual({ type: 'layout', height: 120 })
    expect(surface).toHaveAttribute('data-anchor', 'above')
    expect(parseFloat(surface.style.top)).toBeLessThan(320)
    act(() => listener({ type: 'position', point: { x: 300, y: 40 }, size: 59, phase: 'pet' }))
    expect(surface).toHaveAttribute('data-anchor', 'below')
    expect(surface.style.top).toBe('111px')
    fireEvent.click(screen.getByRole('button', { name: 'desktopPet.activity.dismiss' }))
    expect(sent('layout').at(-1)).toEqual({ type: 'layout', height: 0 })
    rect.mockRestore()
  })
})

describe('desktop pet decisions', () => {
  const decision = {
    id: 'tool:r1', question: 'Run this command?', operation: 'run', target: 'npm test', reason: '', declineValue: 'decline',
    options: [{ value: 'accept', label: 'Allow once' }, { value: 'decline', label: 'Deny' }]
  }
  const waiting: PetStatusInfo = { ...running, status: 'waiting', line: 'run npm test', lineTone: 'warning', canStop: false, decision }
  it('answers a pending approval from the pill and locks the choices until the next request', () => {
    render(<DesktopPet />)
    settle({ canChat: false, status: waiting })
    expect(screen.queryByLabelText('draft')).not.toBeInTheDocument()
    expect(screen.getByText('Run this command?')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Allow once' }))
    expect(command).toHaveBeenLastCalledWith({ type: 'decision', id: 'tool:r1', value: 'accept' })
    expect(screen.getByRole('button', { name: 'Deny' })).toBeDisabled()
    settle({ canChat: false, status: { ...waiting, decision: { ...decision, id: 'tool:r2' } } })
    expect(screen.getByRole('button', { name: 'Deny' })).toBeEnabled()
    fireEvent.click(screen.getByRole('button', { name: 'desktopPet.decision.open' }))
    expect(command).toHaveBeenLastCalledWith({ type: 'return' })
  })
})

describe('desktop pet idle antics', () => {
  const awake = { reducedMotion: false }
  it('lets the pet wander only while settled, idle, awake, and alone', () => {
    render(<DesktopPet />)
    settle(awake, { x: 600, y: 300 })
    expect(avatarMocks.idleOptions?.enabled).toBe(true)
    toggle()
    expect(avatarMocks.idleOptions?.enabled).toBe(false)
    toggle()
    expect(avatarMocks.idleOptions?.enabled).toBe(true)
    act(() => listener({ type: 'snapshot', snapshot: { ...snapshot, ...awake, activity: 'working' } }))
    expect(avatarMocks.idleOptions?.enabled).toBe(false)
    act(() => listener({ type: 'snapshot', snapshot: { ...snapshot, reducedMotion: true } }))
    expect(avatarMocks.idleOptions?.enabled).toBe(false)
    act(() => listener({ type: 'snapshot', snapshot: { ...snapshot, ...awake } }))
    act(() => listener({ type: 'position', point: { x: 600, y: 300 }, size: 59, phase: 'pet', held: true }))
    expect(avatarMocks.idleOptions?.enabled).toBe(false)
  })
  it('stands down gaze and pointer capture while a trip runs', () => {
    avatarMocks.activeIdle = { motion: 'rocket', phase: 'outbound', direction: 'right', travel: 'right' }
    const { container } = render(<DesktopPet />)
    settle(awake, { x: 600, y: 300 })
    const character = container.querySelector<HTMLElement>('.desktop-pet-character')!
    expect(character).toHaveClass('composer-mascot-active-idle')
    expect(character).toHaveAttribute('data-mascot-active-idle', 'rocket')
    expect(character).toHaveAttribute('data-mascot-idle-direction', 'right')
    expect(character.querySelector('.desktop-pet-reaction')).toHaveAttribute('data-gaze', 'false')
    expect(command).toHaveBeenCalledWith({ type: 'interactive', value: false })
  })
})
