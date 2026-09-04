// @vitest-environment jsdom
import './setupPluginRuntime'
import { act, fireEvent, render } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { DesktopPluginHost } from '@dotcraft/plugin'
import { ComposerShell } from '../components/conversation/ComposerShell'
import {
  clearDesktopPluginRegistry,
  registerDesktopPluginSurface
} from '../plugins/desktopPluginRegistry'

let resizeObserverCallback: ResizeObserverCallback | null = null

class ResizeObserverMock {
  constructor(callback: ResizeObserverCallback) {
    resizeObserverCallback = callback
  }

  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}

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
      desktopPluginSurfaceContext={{
        workspacePath: 'X:\\fixtures\\workspace',
        threadId: 'thread-1',
        mode: 'agent',
        busy: false,
        awaitingApproval: false,
        variant: 'default',
        minimalChrome: false
      }}
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
    clearDesktopPluginRegistry()
    vi.useFakeTimers()
    vi.spyOn(Math, 'random').mockReturnValue(0)
    document.documentElement.removeAttribute('data-reduce-motion')
    Object.defineProperty(document, 'hidden', { configurable: true, value: false })
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      value: vi.fn().mockReturnValue({ matches: false })
    })
    Object.defineProperty(globalThis, 'ResizeObserver', {
      configurable: true,
      value: ResizeObserverMock
    })
    resizeObserverCallback = null
  })

  afterEach(() => {
    act(() => clearDesktopPluginRegistry())
    vi.useRealTimers()
    vi.restoreAllMocks()
    document.documentElement.removeAttribute('data-reduce-motion')
    delete (document as Document & { hidden?: boolean }).hidden
    delete (globalThis as { ResizeObserver?: typeof ResizeObserver }).ResizeObserver
  })

  it('exposes reasoning intensity, Fast speed, and MAX context to independent mascot treatments', () => {
    const { container } = renderComposer({
      mascotReasoningEffort: 'extraHigh',
      mascotSpeed: 'fast',
      mascotContextMax: true
    })

    expect(mascot(container)).toHaveAttribute('data-mascot-effort', 'extraHigh')
    expect(mascot(container)).toHaveAttribute('data-mascot-speed', 'fast')
    expect(mascot(container)).toHaveAttribute('data-mascot-context', 'max')
    expect(container.querySelector('.composer-mascot-fast-echo')).not.toBeNull()
  })

  it('lets a plugin replace the mascot character and receive semantic state', () => {
    registerDesktopPluginSurface(
      'fixture.mascot',
      {
        plugin: { id: 'fixture.mascot', version: '1.0.0', displayName: 'Fixture mascot' }
      } as DesktopPluginHost,
      'composer.mascot',
      'replace',
      ({ context }) => (
        <div
          data-testid="custom-mascot"
          data-activity={context.activity}
          data-expression={context.expression}
          data-light={context.light}
          data-submit-revision={context.submitRevision}
          data-effort={context.reasoningEffort}
          data-speed={context.speed}
          data-context-max={String(context.contextMax)}
          data-size={context.size}
          data-thread-id={context.threadId}
        />
      )
    )

    const view = renderComposer({
      mascotInteraction: { expression: 'operator' },
      mascotReasoningEffort: 'high',
      mascotSpeed: 'fast',
      mascotContextMax: true
    })
    let custom = view.getByTestId('custom-mascot')

    expect(view.container.querySelector('.mascot-robot')).toBeNull()
    expect(custom).toHaveAttribute('data-activity', 'working')
    expect(custom).toHaveAttribute('data-expression', 'operator')
    expect(custom).toHaveAttribute('data-light', 'default')
    expect(custom).toHaveAttribute('data-submit-revision', '0')
    expect(custom).toHaveAttribute('data-effort', 'high')
    expect(custom).toHaveAttribute('data-speed', 'fast')
    expect(custom).toHaveAttribute('data-context-max', 'true')
    expect(custom).toHaveAttribute('data-size', '58')
    expect(custom).toHaveAttribute('data-thread-id', 'thread-1')

    view.rerender(composer({ mascotBounceSignal: 1 }))
    custom = view.getByTestId('custom-mascot')
    expect(custom).toHaveAttribute('data-submit-revision', '1')
    expect(view.container.querySelector('.composer-mascot-launch')).not.toBeNull()

    view.rerender(composer({ mascotBounceSignal: 1, mascotInteraction: { light: 'success' } }))
    custom = view.getByTestId('custom-mascot')
    expect(custom).toHaveAttribute('data-activity', 'success')
    expect(custom).toHaveAttribute('data-submit-revision', '1')
    expect(view.container.querySelector('.composer-mascot-cheer')).not.toBeNull()
  })

  it('finishes the Core-owned click greeting when the default SVG is replaced', () => {
    registerDesktopPluginSurface(
      'fixture.mascot',
      {
        plugin: { id: 'fixture.mascot', version: '1.0.0', displayName: 'Fixture mascot' }
      } as DesktopPluginHost,
      'composer.mascot',
      'replace',
      ({ context }) => <div data-testid="custom-mascot" data-expression={context.expression} />
    )

    const view = renderComposer()
    const jelly = view.container.querySelector<HTMLElement>('.composer-mascot-jelly')
    if (!jelly) throw new Error('Mascot interaction layer was not rendered')

    fireEvent.click(jelly)
    expect(view.getByTestId('custom-mascot')).toHaveAttribute('data-expression', 'happy')

    act(() => vi.advanceTimersByTime(1_600))
    expect(view.getByTestId('custom-mascot')).toHaveAttribute('data-expression', 'neutral')
  })

  it('keeps the mascot visible and anchors it to a measured top accessory', () => {
    const view = renderComposer({
      topAccessoryVisible: true,
      topAccessory: <div>Background activity</div>
    })
    const overlay = view.getByTestId('composer-top-accessory-overlay')
    vi.spyOn(overlay, 'getBoundingClientRect').mockReturnValue({
      x: 0,
      y: 0,
      top: 0,
      right: 600,
      bottom: 80,
      left: 0,
      width: 600,
      height: 80,
      toJSON: () => ({})
    })

    expect(mascot(view.container)).toHaveAttribute('data-mascot-anchor-offset', '0')
    act(() => {
      resizeObserverCallback?.([], {} as ResizeObserver)
    })

    expect(mascot(view.container)).toHaveAttribute('data-mascot-anchor-offset', '80')
    expect(view.container.querySelector('.composer-mascot-push-lift')).toBeNull()
    act(() => vi.advanceTimersByTime(48))
    expect(view.container.querySelector('.composer-mascot-push-lift')).not.toBeNull()
  })

  it('lands on the composer when its top accessory disappears', () => {
    const props = {
      topAccessoryVisible: true,
      topAccessory: <div>Background activity</div>
    }
    const view = renderComposer(props)
    const overlay = view.getByTestId('composer-top-accessory-overlay')
    vi.spyOn(overlay, 'getBoundingClientRect').mockReturnValue({
      x: 0,
      y: 0,
      top: 0,
      right: 600,
      bottom: 80,
      left: 0,
      width: 600,
      height: 80,
      toJSON: () => ({})
    })
    act(() => {
      resizeObserverCallback?.([], {} as ResizeObserver)
      vi.advanceTimersByTime(48)
      vi.advanceTimersByTime(430)
    })

    view.rerender(composer({ ...props, topAccessoryVisible: false }))
    expect(mascot(view.container)).toHaveAttribute('data-mascot-anchor-offset', '0')
    act(() => vi.advanceTimersByTime(310))
    expect(view.container.querySelector('.composer-mascot-land')).not.toBeNull()
  })

  it('moves to a top accessory without transition when reduced motion is on', () => {
    document.documentElement.dataset.reduceMotion = 'on'
    const view = renderComposer({
      topAccessoryVisible: true,
      topAccessory: <div>Background activity</div>
    })
    const overlay = view.getByTestId('composer-top-accessory-overlay')
    vi.spyOn(overlay, 'getBoundingClientRect').mockReturnValue({
      x: 0,
      y: 0,
      top: 0,
      right: 600,
      bottom: 80,
      left: 0,
      width: 600,
      height: 80,
      toJSON: () => ({})
    })

    act(() => {
      resizeObserverCallback?.([], {} as ResizeObserver)
      vi.advanceTimersByTime(48)
    })

    const element = mascot(view.container)
    expect(element).toHaveAttribute('data-mascot-anchor-offset', '80')
    expect(element.style.transition).toBe('')
    expect(element.style.transform).toBe('')
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

  it('does not launch an active idle after dozing off while the window was hidden', () => {
    // The patrol defers itself every 5s while the document is hidden, but the
    // doze timer does not — so the mascot falls asleep with a patrol still
    // pending. Returning to the window must not launch it out of the sleep pose.
    Object.defineProperty(document, 'hidden', { configurable: true, value: true })
    const { container } = renderComposer()
    const element = mascot(container)

    act(() => vi.advanceTimersByTime(90_000))
    expect(element).not.toHaveAttribute('data-mascot-active-idle')
    expect(element).toHaveClass('composer-mascot-sleeping')

    Object.defineProperty(document, 'hidden', { configurable: true, value: false })
    act(() => vi.advanceTimersByTime(70_000))

    expect(element).not.toHaveAttribute('data-mascot-active-idle')
    expect(element).toHaveClass('composer-mascot-sleeping')
  })

})
