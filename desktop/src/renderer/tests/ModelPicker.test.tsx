import { act, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ModelPicker } from '../components/conversation/ModelPicker'

const originalInnerWidth = window.innerWidth
const originalInnerHeight = window.innerHeight

class ResizeObserverStub {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}

describe('ModelPicker', () => {
  beforeEach(() => {
    vi.stubGlobal('ResizeObserver', ResizeObserverStub)
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) }
      }
    })
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
    setViewport(originalInnerWidth, originalInnerHeight)
  })

  it('marks the composer overlay layer active only while the picker is open', () => {
    render(
      <LocaleProvider>
        <ModelPicker
          modelName="gpt-5.5"
          modelOptions={['gpt-5.5']}
          reasoningValue="off"
          triggerStyle={{}}
        />
      </LocaleProvider>
    )

    const trigger = screen.getByRole('button', { name: 'Select model' })
    const picker = trigger.closest('[data-composer-overlay-open]')
    expect(picker).toHaveAttribute('data-composer-overlay-open', 'false')

    fireEvent.click(trigger)
    expect(picker).toHaveAttribute('data-composer-overlay-open', 'true')

    fireEvent.keyDown(document, { key: 'Escape' })
    expect(picker).toHaveAttribute('data-composer-overlay-open', 'false')
  })

  it('shows model and intelligence as secondary menu entries', () => {
    render(
      <LocaleProvider>
        <ModelPicker
          modelName="claude-opus-4-7"
          modelOptions={['claude-opus-4-7']}
          modelCatalog={[catalogModel('claude-opus-4-7', false, ['high', 'extraHigh'], 'extraHigh')]}
          reasoningValue="high"
          triggerStyle={{}}
        />
      </LocaleProvider>
    )

    const menu = openPicker()
    expect(within(menu).getByRole('menuitem', { name: /Model/ })).toBeInTheDocument()
    fireEvent.mouseEnter(within(menu).getByRole('menuitem', { name: /Intelligence/ }))

    const listbox = screen.getByRole('listbox', { name: 'Intelligence' })
    expect(within(listbox).getByRole('option', { name: /xHigh/ })).toBeInTheDocument()
    expect(within(listbox).getByRole('option', { name: /Off/ })).toBeDisabled()
  })

  it('applies an intelligence selection without changing the model', () => {
    const onReasoningChange = vi.fn()
    const onChange = vi.fn()

    render(
      <LocaleProvider>
        <ModelPicker
          modelName="claude-opus-4-7"
          modelOptions={['claude-opus-4-7']}
          modelCatalog={[catalogModel('claude-opus-4-7', true, ['high', 'extraHigh'], 'high')]}
          reasoningValue="high"
          triggerStyle={{}}
          onChange={onChange}
          onReasoningChange={onReasoningChange}
        />
      </LocaleProvider>
    )

    const menu = openPicker()
    fireEvent.click(within(menu).getByRole('menuitem', { name: /Intelligence/ }))
    fireEvent.click(screen.getByRole('option', { name: /xHigh/ }))

    expect(onReasoningChange).toHaveBeenCalledWith('extraHigh')
    expect(onChange).not.toHaveBeenCalled()
    expect(screen.queryByRole('menu', { name: 'Select model' })).not.toBeInTheDocument()
  })

  it('resolves inherited reasoning to the model default without offering a Default option', () => {
    render(
      <LocaleProvider>
        <ModelPicker
          modelName="claude-opus-4-7"
          modelOptions={['claude-opus-4-7']}
          modelCatalog={[catalogModel('claude-opus-4-7', true, ['high', 'extraHigh'], 'extraHigh')]}
          reasoningValue="default"
          triggerStyle={{}}
        />
      </LocaleProvider>
    )

    const menu = openPicker()
    expect(within(menu).getByRole('menuitem', { name: /IntelligencexHigh/ })).toBeInTheDocument()
    fireEvent.click(within(menu).getByRole('menuitem', { name: /Intelligence/ }))

    const listbox = screen.getByRole('listbox', { name: 'Intelligence' })
    expect(within(listbox).queryByRole('option', { name: 'Default' })).not.toBeInTheDocument()
    expect(within(listbox).getByRole('option', { name: /xHigh/ })).toHaveAttribute('aria-selected', 'true')
  })

  it('falls back to the new model default when the current effort is unsupported', () => {
    const onChange = vi.fn()
    const onReasoningChange = vi.fn()
    render(
      <LocaleProvider>
        <ModelPicker
          modelName="gpt-5.5"
          modelOptions={['gpt-5.5', 'gpt-5.5-mini']}
          modelCatalog={[
            catalogModel('gpt-5.5', true, ['low', 'medium', 'high', 'extraHigh'], 'medium'),
            catalogModel('gpt-5.5-mini', true, ['low', 'medium'], 'medium')
          ]}
          reasoningValue="high"
          triggerStyle={{}}
          onChange={onChange}
          onReasoningChange={onReasoningChange}
        />
      </LocaleProvider>
    )

    const menu = openPicker()
    fireEvent.click(within(menu).getByRole('menuitem', { name: /Model/ }))
    fireEvent.click(screen.getByRole('option', { name: 'gpt-5.5-mini' }))

    expect(onChange).toHaveBeenCalledWith('gpt-5.5-mini')
    expect(onReasoningChange).toHaveBeenCalledWith('medium')
  })

  it('does not offer a stale selected model when a ready provider model list excludes it', () => {
    render(
      <LocaleProvider>
        <ModelPicker
          modelName="mimo-v2.5-pro"
          modelOptions={['claude-sonnet-4-5']}
          modelListReady
          reasoningValue="off"
          triggerStyle={{}}
        />
      </LocaleProvider>
    )

    expect(screen.getByText('mimo-v2.5-pro')).toBeInTheDocument()
    const menu = openPicker()
    fireEvent.click(within(menu).getByRole('menuitem', { name: /Model/ }))

    const listbox = screen.getByRole('listbox', { name: 'Model' })
    expect(within(listbox).queryByRole('option', { name: 'mimo-v2.5-pro' })).not.toBeInTheDocument()
    expect(within(listbox).getByRole('option', { name: 'claude-sonnet-4-5' })).toBeInTheDocument()
  })

  it('localizes intelligence options from model catalog metadata', async () => {
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: vi.fn().mockResolvedValue({ locale: 'zh-Hans' }) }
      }
    })

    render(
      <LocaleProvider>
        <ModelPicker
          modelName="mimo-v2.5-pro"
          modelOptions={['mimo-v2.5-pro']}
          modelCatalog={[catalogModel('mimo-v2.5-pro', true, ['low', 'medium', 'high', 'extraHigh'], 'extraHigh')]}
          reasoningValue="extraHigh"
          triggerStyle={{}}
        />
      </LocaleProvider>
    )

    fireEvent.click(await screen.findByRole('button', { name: '选择模型' }))
    const menu = screen.getByRole('menu', { name: '选择模型' })
    fireEvent.click(within(menu).getByRole('menuitem', { name: /思考强度/ }))

    const listbox = screen.getByRole('listbox', { name: '思考强度' })
    expect(within(listbox).getByText('低')).toBeInTheDocument()
    expect(within(listbox).getByText('中')).toBeInTheDocument()
    expect(within(listbox).getByText('高')).toBeInTheDocument()
    expect(within(listbox).getByRole('option', { name: /超高/ })).toBeInTheDocument()
    expect(within(listbox).getByText('支持模型的最高深度。')).toBeInTheDocument()
  })

  it('omits MAX Mode when no context handler is provided', () => {
    render(
      <LocaleProvider>
        <ModelPicker modelName="gpt-5.5" modelOptions={['gpt-5.5']} reasoningValue="off" triggerStyle={{}} />
      </LocaleProvider>
    )

    const menu = openPicker()
    expect(within(menu).queryByText('Context')).not.toBeInTheDocument()
    expect(within(menu).queryByRole('switch', { name: 'MAX Mode' })).not.toBeInTheDocument()
  })

  it('toggles MAX on for a supported model', () => {
    const onContextModeChange = vi.fn()
    render(
      <LocaleProvider>
        <ModelPicker
          modelName="gpt-5.5"
          modelOptions={['gpt-5.5']}
          reasoningValue="off"
          triggerStyle={{}}
          contextMode="default"
          contextSupportsMax
          onContextModeChange={onContextModeChange}
        />
      </LocaleProvider>
    )

    const menu = openPicker()
    const maxSwitch = within(menu).getByRole('switch', { name: 'MAX Mode' })
    expect(maxSwitch).not.toBeDisabled()
    expect(maxSwitch).toHaveAttribute('aria-checked', 'false')

    fireEvent.click(maxSwitch)
    expect(onContextModeChange).toHaveBeenCalledWith('max')
  })

  it('disables MAX when the model does not support it', () => {
    const onContextModeChange = vi.fn()
    render(
      <LocaleProvider>
        <ModelPicker
          modelName="my-local-model"
          modelOptions={['my-local-model']}
          reasoningValue="off"
          triggerStyle={{}}
          contextMode="default"
          contextSupportsMax={false}
          onContextModeChange={onContextModeChange}
        />
      </LocaleProvider>
    )

    const menu = openPicker()
    const maxSwitch = within(menu).getByRole('switch', { name: 'MAX Mode' })
    expect(maxSwitch).toBeDisabled()

    fireEvent.click(maxSwitch)
    expect(onContextModeChange).not.toHaveBeenCalled()
  })

  it('surfaces a degraded MAX thread and lets the switch reset it', () => {
    const onContextModeChange = vi.fn()
    render(
      <LocaleProvider>
        <ModelPicker
          modelName="my-local-model"
          modelOptions={['my-local-model']}
          reasoningValue="off"
          triggerStyle={{}}
          contextMode="max"
          contextSupportsMax={false}
          contextDegraded
          contextConfiguredWindow={128000}
          onContextModeChange={onContextModeChange}
        />
      </LocaleProvider>
    )

    const menu = openPicker()
    const maxSwitch = within(menu).getByRole('switch', { name: 'MAX Mode' })
    expect(maxSwitch).toHaveAttribute('aria-checked', 'true')
    expect(within(menu).getByText(/128K/)).toBeInTheDocument()

    fireEvent.click(maxSwitch)
    expect(onContextModeChange).toHaveBeenCalledWith('default')
  })

  it('uses Escape to leave a secondary menu before closing the picker', () => {
    render(
      <LocaleProvider>
        <ModelPicker
          modelName="gpt-5.5"
          modelOptions={['gpt-5.5']}
          modelCatalog={[catalogModel('gpt-5.5', true, ['low', 'medium', 'high'], 'medium')]}
          reasoningValue="high"
          triggerStyle={{}}
        />
      </LocaleProvider>
    )

    const menu = openPicker()
    fireEvent.click(within(menu).getByRole('menuitem', { name: /Intelligence/ }))
    expect(screen.getByRole('listbox', { name: 'Intelligence' })).toBeInTheDocument()

    fireEvent.keyDown(document, { key: 'Escape' })
    expect(screen.queryByRole('listbox', { name: 'Intelligence' })).not.toBeInTheDocument()
    expect(screen.getByRole('menu', { name: 'Select model' })).toBeInTheDocument()

    fireEvent.keyDown(document, { key: 'Escape' })
    expect(screen.queryByRole('menu', { name: 'Select model' })).not.toBeInTheDocument()
  })

  it('keeps the current submenu while the pointer crosses another row inside the prediction cone', () => {
    vi.useFakeTimers()
    try {
      render(
        <LocaleProvider>
          <ModelPicker
            modelName="gpt-5.5"
            modelOptions={['gpt-5.5']}
            modelCatalog={[catalogModel('gpt-5.5', true, ['low', 'medium', 'high'], 'medium')]}
            reasoningValue="high"
            triggerStyle={{}}
          />
        </LocaleProvider>
      )

      const menu = openPicker()
      const modelRow = within(menu).getByRole('menuitem', { name: /Model/ })
      const intelligenceRow = within(menu).getByRole('menuitem', { name: /Intelligence/ })
      fireEvent.click(modelRow)

      const modelListbox = screen.getByRole('listbox', { name: 'Model' })
      vi.spyOn(modelListbox, 'getBoundingClientRect').mockReturnValue(domRect(280, 50, 310, 250))
      fireEvent.mouseMove(modelRow, { clientX: 100, clientY: 100 })
      fireEvent.mouseEnter(intelligenceRow, { clientX: 180, clientY: 140 })

      expect(screen.getByRole('listbox', { name: 'Model' })).toBeInTheDocument()
      expect(screen.queryByRole('listbox', { name: 'Intelligence' })).not.toBeInTheDocument()

      act(() => vi.advanceTimersByTime(280))
      expect(screen.getByRole('listbox', { name: 'Intelligence' })).toBeInTheDocument()
    } finally {
      vi.useRealTimers()
    }
  })

  it('keeps the picker anchored to its trigger instead of lifting it above the composer card', () => {
    setViewport(1100, 768)
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function getRect(this: HTMLElement) {
      if (this.hasAttribute('data-composer-card')) return domRect(0, 200, 900, 180)
      if (this.parentElement?.hasAttribute('data-composer-card')) return domRect(600, 360, 180, 32)
      if (this.getAttribute('role') === 'menu') return domRect(460, 260, 312, 132)
      return domRect(0, 0, 0, 0)
    })

    renderPickerInComposer()

    const menu = openPicker()
    const intelligenceRow = within(menu).getByRole('menuitem', { name: /Intelligence/ })
    Object.defineProperty(intelligenceRow, 'offsetTop', { configurable: true, value: 46 })
    fireEvent.click(intelligenceRow)

    expect(menu.style.transform).toBe('')
    expect(screen.getByRole('listbox', { name: 'Intelligence' })).toHaveStyle({
      top: '40px',
      maxHeight: '320px'
    })
  })

  it('only shifts and caps a submenu when the viewport is genuinely too short', () => {
    setViewport(1100, 768)
    renderPickerInComposer()

    const menu = openPicker()
    vi.spyOn(menu, 'getBoundingClientRect').mockReturnValue(domRect(460, 100, 312, 132))
    const intelligenceRow = within(menu).getByRole('menuitem', { name: /Intelligence/ })
    Object.defineProperty(intelligenceRow, 'offsetTop', { configurable: true, value: 46 })
    fireEvent.click(intelligenceRow)

    const submenu = screen.getByRole('listbox', { name: 'Intelligence' })
    Object.defineProperty(submenu, 'scrollHeight', { configurable: true, value: 300 })
    Object.defineProperty(submenu, 'offsetHeight', { configurable: true, value: 300 })
    vi.spyOn(submenu, 'getBoundingClientRect').mockReturnValue(domRect(771, 140, 280, 300))

    fireEvent(window, new Event('resize'))
    expect(submenu).toHaveStyle({ top: '40px', maxHeight: '320px' })

    setViewport(1100, 240)
    fireEvent(window, new Event('resize'))
    expect(submenu).toHaveStyle({ top: '-92px', maxHeight: '224px' })

    fireEvent.click(intelligenceRow)
    expect(submenu).toHaveStyle({ top: '-92px', maxHeight: '224px' })
  })
})

function renderPickerInComposer(): void {
  render(
    <div data-composer-card>
      <LocaleProvider>
        <ModelPicker
          modelName="gpt-5.5"
          modelOptions={['gpt-5.5']}
          modelCatalog={[catalogModel('gpt-5.5', true, ['low', 'medium', 'high', 'extraHigh'], 'high')]}
          reasoningValue="high"
          triggerStyle={{}}
        />
      </LocaleProvider>
    </div>
  )
}

function openPicker(name = 'Select model'): HTMLElement {
  fireEvent.click(screen.getByRole('button', { name }))
  return screen.getByRole('menu', { name })
}

function catalogModel(
  id: string,
  supportsDisable: boolean,
  efforts: Array<'low' | 'medium' | 'high' | 'extraHigh'>,
  defaultEffort: 'low' | 'medium' | 'high' | 'extraHigh'
) {
  return {
    id,
    reasoning: {
      supportsDisable,
      supportedEfforts: efforts.map((effort) => ({ effort, label: effort })),
      defaultEffort,
      supportedOutputs: ['full' as const],
      defaultOutput: 'full' as const
    }
  }
}

function domRect(left: number, top: number, width: number, height: number): DOMRect {
  return {
    x: left,
    y: top,
    left,
    top,
    width,
    height,
    right: left + width,
    bottom: top + height,
    toJSON: () => ({})
  }
}

function setViewport(width: number, height: number): void {
  Object.defineProperty(window, 'innerWidth', { configurable: true, writable: true, value: width })
  Object.defineProperty(window, 'innerHeight', { configurable: true, writable: true, value: height })
}
