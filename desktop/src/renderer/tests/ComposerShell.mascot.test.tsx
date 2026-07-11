// @vitest-environment jsdom
import { act, fireEvent, render } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ComposerShell } from '../components/conversation/ComposerShell'

function composer(
  props: Partial<React.ComponentProps<typeof ComposerShell>> = {}
): React.ReactElement {
  return (
    <ComposerShell
      dragOver={false}
      dropLabel="Drop"
      editor={<textarea aria-label="Prompt" />}
      footerLeading={<span>Leading</span>}
      footerAction={<button type="button">Send</button>}
      onDragOver={vi.fn()}
      onDragLeave={vi.fn()}
      onDrop={vi.fn()}
      showMascot
      {...props}
    />
  )
}

function renderComposer(
  props: Partial<React.ComponentProps<typeof ComposerShell>> = {}
): ReturnType<typeof render> {
  return render(composer(props))
}

function mascot(container: HTMLElement): HTMLElement {
  const element = container.querySelector<HTMLElement>('[data-mascot-effort]')
  if (!element) throw new Error('Mascot was not rendered')
  return element
}

describe('ComposerShell mascot energy and active idle', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.spyOn(Math, 'random').mockReturnValue(0)
    document.documentElement.removeAttribute('data-reduce-motion')
    Object.defineProperty(document, 'hidden', { configurable: true, value: false })
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      value: vi.fn().mockReturnValue({ matches: false })
    })
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
    document.documentElement.removeAttribute('data-reduce-motion')
    delete (document as Document & { hidden?: boolean }).hidden
  })

  it('exposes reasoning intensity and MAX context to the mascot treatment', () => {
    const { container } = renderComposer({
      mascotReasoningEffort: 'extraHigh',
      mascotContextMax: true
    })

    expect(mascot(container)).toHaveAttribute('data-mascot-effort', 'extraHigh')
    expect(mascot(container)).toHaveAttribute('data-mascot-context', 'max')
  })

  it('hands an Agent Profile avatar over at the hidden midpoint', () => {
    const first = { palette: 0, face: 0, accessory: 0 }
    const second = { palette: 2, face: 1, accessory: 3 }
    const view = renderComposer({ mascotAvatar: first })
    const element = mascot(view.container)

    expect(element.style.getPropertyValue('--mascot-body-dark')).toBe('#2563eb')
    view.rerender(composer({ mascotAvatar: second }))

    expect(element).toHaveAttribute('data-mascot-profile-transition', 'active')
    act(() => vi.advanceTimersByTime(619))
    expect(element.style.getPropertyValue('--mascot-body-dark')).toBe('#2563eb')

    act(() => vi.advanceTimersByTime(1))
    expect(element.style.getPropertyValue('--mascot-body-dark')).toBe('#6d28d9')

    act(() => vi.advanceTimersByTime(620))
    expect(element).toHaveAttribute('data-mascot-profile-transition', 'idle')
  })

  it('switches Agent Profile avatars immediately when reduced motion is on', () => {
    document.documentElement.dataset.reduceMotion = 'on'
    const view = renderComposer({ mascotAvatar: { palette: 0, face: 0, accessory: 0 } })
    const element = mascot(view.container)

    view.rerender(composer({ mascotAvatar: { palette: 2, face: 1, accessory: 3 } }))

    expect(element.style.getPropertyValue('--mascot-body-dark')).toBe('#6d28d9')
    expect(element).toHaveAttribute('data-mascot-profile-transition', 'idle')
  })

  it.each([
    { random: 0, delay: 35_000, motion: 'hop', travel: 2_560, hold: 1_400 },
    { random: 0.7, delay: 56_000, motion: 'rocket', travel: 1_960, hold: 1_400 },
    { random: 0.95, delay: 63_500, motion: 'hover', travel: 1_960, hold: 2_800 }
  ])('runs $motion active idle through outbound, away, and inbound phases', ({
    random,
    delay,
    motion,
    travel,
    hold
  }) => {
    vi.mocked(Math.random).mockReturnValue(random)
    const { container } = renderComposer()
    const element = mascot(container)

    act(() => vi.advanceTimersByTime(delay))
    expect(element).toHaveAttribute('data-mascot-active-idle', motion)
    expect(element).toHaveAttribute('data-mascot-idle-phase', 'outbound')

    act(() => vi.advanceTimersByTime(travel))
    expect(element).toHaveAttribute('data-mascot-idle-phase', 'away')

    act(() => vi.advanceTimersByTime(hold))
    expect(element).toHaveAttribute('data-mascot-idle-phase', 'inbound')

    act(() => vi.advanceTimersByTime(travel))
    expect(element).not.toHaveAttribute('data-mascot-active-idle')
    expect(element).not.toHaveAttribute('data-mascot-idle-phase')
  })

  it('cancels an active idle immediately when the user interacts', () => {
    const { container } = renderComposer()
    const element = mascot(container)

    act(() => vi.advanceTimersByTime(35_000))
    expect(element).toHaveAttribute('data-mascot-active-idle', 'hop')

    fireEvent.keyDown(window, { key: 'a' })
    expect(element).not.toHaveAttribute('data-mascot-active-idle')
  })

  it('does not schedule active idle motion when reduced motion is forced on', () => {
    document.documentElement.dataset.reduceMotion = 'on'
    const { container } = renderComposer()
    const element = mascot(container)

    act(() => vi.advanceTimersByTime(90_000))
    expect(element).not.toHaveAttribute('data-mascot-active-idle')
  })
})
