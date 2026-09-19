// @vitest-environment jsdom
import './setupPluginRuntime'
import { act, cleanup, fireEvent, render } from '@testing-library/react'
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
    document.documentElement.removeAttribute('data-theme')
    document.documentElement.removeAttribute('data-reduce-motion')
    Object.defineProperty(document, 'hidden', { configurable: true, value: false })
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      value: vi.fn().mockReturnValue({ matches: false, addEventListener: vi.fn(), removeEventListener: vi.fn() })
    })
    Object.defineProperty(globalThis, 'ResizeObserver', {
      configurable: true,
      value: ResizeObserverMock
    })
    resizeObserverCallback = null
  })

  afterEach(() => {
    cleanup()
    act(() => clearDesktopPluginRegistry())
    vi.useRealTimers()
    vi.restoreAllMocks()
    document.documentElement.removeAttribute('data-theme')
    document.documentElement.removeAttribute('data-reduce-motion')
    delete (document as Document & { hidden?: boolean }).hidden
    delete (globalThis as { ResizeObserver?: typeof ResizeObserver }).ResizeObserver
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

    expect(view.container.querySelector('.dca-robot')).toBeNull()
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

})
