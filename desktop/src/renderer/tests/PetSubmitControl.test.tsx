import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { PetStatusInfo } from '../../shared/desktopPet'
import { PetSubmitControl } from '../components/desktopPet/PetSubmitControl'

vi.mock('../components/conversation/ComposerSubmitButton', () => ({
  ComposerSubmitButton: ({ mode, disabled, tone, onClick }: { mode: string; disabled?: boolean; tone?: string; onClick: () => void }) =>
    <button aria-label={mode} disabled={disabled} data-tone={tone} onClick={onClick} />
}))

const running: PetStatusInfo = { status: 'running', title: 'Fix', line: 'Running tests', lineTone: 'neutral', turnId: 'turn-1', canStop: true }

describe('PetSubmitControl', () => {
  it('sends when nothing runs, following the draft', () => {
    const onSubmit = vi.fn()
    const { rerender } = render(<PetSubmitControl status={undefined} followUpMode="steer" hasDraft={false} busy={false} onSubmit={onSubmit} onStop={() => {}} />)
    expect(screen.getByRole('button', { name: 'send' })).toBeDisabled()
    rerender(<PetSubmitControl status={{ ...running, status: 'review', canStop: false }} followUpMode="steer" hasDraft busy={false} onSubmit={onSubmit} onStop={() => {}} />)
    fireEvent.click(screen.getByRole('button', { name: 'send' }))
    expect(onSubmit).toHaveBeenCalledTimes(1)
  })
  it('routes a draft into the running turn with the configured follow-up mode', () => {
    const { rerender } = render(<PetSubmitControl status={running} followUpMode="steer" hasDraft busy={false} onSubmit={() => {}} onStop={() => {}} />)
    expect(screen.getByRole('button', { name: 'steer' })).toBeEnabled()
    rerender(<PetSubmitControl status={running} followUpMode="queue" hasDraft busy onSubmit={() => {}} onStop={() => {}} />)
    expect(screen.getByRole('button', { name: 'queue' })).toBeDisabled()
  })
  it('turns into Stop for an empty draft and locks while the interruption lands', () => {
    const onStop = vi.fn()
    const { rerender } = render(<PetSubmitControl status={running} followUpMode="steer" hasDraft={false} busy={false} onSubmit={() => {}} onStop={onStop} />)
    const stop = screen.getByRole('button', { name: 'stop' })
    expect(stop).toHaveAttribute('data-tone', 'enabled')
    fireEvent.click(stop)
    expect(onStop).toHaveBeenCalledTimes(1)
    rerender(<PetSubmitControl status={{ ...running, canStop: false, stopping: true }} followUpMode="steer" hasDraft={false} busy={false} onSubmit={() => {}} onStop={onStop} />)
    expect(screen.getByRole('button', { name: 'stopping' })).toBeDisabled()
    rerender(<PetSubmitControl status={{ ...running, canStop: false }} followUpMode="steer" hasDraft={false} busy={false} onSubmit={() => {}} onStop={onStop} />)
    expect(screen.getByRole('button', { name: 'send' })).toBeDisabled()
  })
})
