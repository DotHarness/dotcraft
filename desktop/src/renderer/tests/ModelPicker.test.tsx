import { act, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ModelPicker } from '../components/conversation/ModelPicker'
import { installDesktopApiMock } from './desktopApiMock'

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
    installDesktopApiMock({
        settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) }
      })
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
    setViewport(originalInnerWidth, originalInnerHeight)
  })

  it('headlines the level and offers one stop per advertised effort', () => {
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

    const panel = openPicker()
    expect(within(panel).getByRole('button', { name: 'claude-opus-4-7' })).toBeInTheDocument()
    const slider = within(panel).getByRole('slider', { name: 'Intelligence' })
    expect(slider).toHaveAttribute('aria-valuetext', 'High')
    expect(slider).toHaveAttribute('max', '1')
  })

  it('exposes a keyboard-accessible provider submenu and can hide Default', () => {
    const onProviderChange = vi.fn()
    render(
      <LocaleProvider>
        <ModelPicker
          providerId="anthropic"
          providerOptions={[
            { id: 'anthropic', displayName: 'Anthropic' },
            { id: 'openai', displayName: 'OpenAI' }
          ]}
          modelName="claude-opus-4-7"
          modelOptions={['claude-opus-4-7']}
          onProviderChange={onProviderChange}
          allowDefaultModel={false}
          triggerStyle={{}}
        />
      </LocaleProvider>
    )

    const menu = openMenu(openPicker(), 'claude-opus-4-7')
    const providerRow = within(menu).getByRole('menuitem', { name: /Provider/ })
    providerRow.focus()
    fireEvent.keyDown(document, { key: 'ArrowRight' })
    const providerMenu = screen.getByRole('listbox', { name: 'Provider' })
    fireEvent.click(within(providerMenu).getByRole('option', { name: /OpenAI/ }))
    expect(onProviderChange).toHaveBeenCalledWith('openai')

    const reopened = openMenu(openPicker(), 'claude-opus-4-7')
    fireEvent.mouseEnter(within(reopened).getByRole('menuitem', { name: /Model/ }))
    expect(within(screen.getByRole('listbox', { name: 'Model' })).queryByText('Default')).not.toBeInTheDocument()
  })

  it('shows the Fast bolt only for a fast-capable model and toggles the speed', () => {
    const onSpeedChange = vi.fn()
    render(
      <LocaleProvider>
        <ModelPicker
          modelName="gpt-5.5"
          modelOptions={['gpt-5.5']}
          modelCatalog={[catalogModel('gpt-5.5', true, ['medium'], 'medium', true)]}
          reasoningValue="medium"
          speedValue="standard"
          onSpeedChange={onSpeedChange}
          triggerStyle={{}}
        />
      </LocaleProvider>
    )

    const bolt = within(openPicker()).getByRole('button', { name: 'Fast', pressed: false })
    fireEvent.click(bolt)
    expect(onSpeedChange).toHaveBeenCalledWith('fast')
    expect(screen.getByRole('dialog', { name: 'Select model' })).toBeInTheDocument()
  })

  it('hides the Fast bolt when capability metadata is absent', () => {
    render(
      <LocaleProvider>
        <ModelPicker
          modelName="gpt-5.4-mini"
          modelOptions={['gpt-5.4-mini']}
          modelCatalog={[catalogModel('gpt-5.4-mini', true, ['medium'], 'medium')]}
          reasoningValue="medium"
          triggerStyle={{}}
        />
      </LocaleProvider>
    )

    expect(within(openPicker()).queryByRole('button', { name: 'Fast' })).not.toBeInTheDocument()
  })

  it('applies a level from the scale without changing the model or closing the panel', () => {
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

    const slider = within(openPicker()).getByRole('slider', { name: 'Intelligence' })
    fireEvent.change(slider, { target: { value: '2' } })

    expect(onReasoningChange).toHaveBeenCalledWith('extraHigh')
    expect(onChange).not.toHaveBeenCalled()
    expect(screen.getByRole('dialog', { name: 'Select model' })).toBeInTheDocument()
  })

  it('offers Ultra after Extra High only when the server advertises it', () => {
    const onReasoningChange = vi.fn()
    const { rerender } = render(
      <LocaleProvider>
        <ModelPicker
          modelName="model-ultra"
          modelOptions={['model-ultra']}
          modelCatalog={[catalogModel('model-ultra', true, ['high', 'extraHigh', 'ultra'], 'extraHigh')]}
          reasoningValue="extraHigh"
          triggerStyle={{}}
          onReasoningChange={onReasoningChange}
        />
      </LocaleProvider>
    )

    let slider = within(openPicker()).getByRole('slider', { name: 'Intelligence' })
    expect(slider).toHaveAttribute('max', '3')
    expect(slider).toHaveValue('2')
    fireEvent.change(slider, { target: { value: '3' } })
    expect(onReasoningChange).toHaveBeenCalledWith('ultra')

    rerender(
      <LocaleProvider>
        <ModelPicker
          modelName="model-ultra"
          modelOptions={['model-ultra']}
          modelCatalog={[catalogModel('model-ultra', true, ['high', 'extraHigh'], 'extraHigh')]}
          reasoningValue="extraHigh"
          triggerStyle={{}}
        />
      </LocaleProvider>
    )
    slider = screen.getByRole('slider', { name: 'Intelligence' })
    expect(slider).toHaveAttribute('max', '2')
  })

  it('resolves inherited reasoning to the model default', () => {
    render(
      <LocaleProvider>
        <ModelPicker
          modelName="model-ultra"
          modelOptions={['model-ultra']}
          modelCatalog={[catalogModel('model-ultra', true, ['high', 'extraHigh'], 'extraHigh')]}
          reasoningValue="default"
          triggerStyle={{}}
        />
      </LocaleProvider>
    )

    const panel = openPicker()
    expect(within(panel).getByRole('slider', { name: 'Intelligence' })).toHaveAttribute('aria-valuetext', 'xHigh')
    expect(within(panel).queryByRole('button', { name: 'Reset to default' })).not.toBeInTheDocument()
  })

  it('offers reset only while a setting differs from the catalog defaults and returns each one', () => {
    const onReasoningChange = vi.fn()
    const onSpeedChange = vi.fn()
    const onContextModeChange = vi.fn()
    const { rerender } = render(
      <LocaleProvider>
        <ModelPicker
          modelName="gpt-5.5"
          modelOptions={['gpt-5.5']}
          modelCatalog={[catalogModel('gpt-5.5', true, ['low', 'medium', 'high'], 'medium', true)]}
          reasoningValue="high"
          speedValue="fast"
          contextMode="default"
          contextSupportsMax
          onReasoningChange={onReasoningChange}
          onSpeedChange={onSpeedChange}
          onContextModeChange={onContextModeChange}
          triggerStyle={{}}
        />
      </LocaleProvider>
    )

    fireEvent.click(within(openPicker()).getByRole('button', { name: 'Reset to default' }))
    expect(onReasoningChange).toHaveBeenCalledWith('medium')
    expect(onSpeedChange).toHaveBeenCalledWith('standard')
    expect(onContextModeChange).not.toHaveBeenCalled()

    rerender(
      <LocaleProvider>
        <ModelPicker
          modelName="gpt-5.5"
          modelOptions={['gpt-5.5']}
          modelCatalog={[catalogModel('gpt-5.5', true, ['low', 'medium', 'high'], 'medium', true)]}
          reasoningValue="medium"
          speedValue="standard"
          contextMode="default"
          contextSupportsMax
          onReasoningChange={onReasoningChange}
          onSpeedChange={onSpeedChange}
          onContextModeChange={onContextModeChange}
          triggerStyle={{}}
        />
      </LocaleProvider>
    )
    expect(screen.queryByRole('button', { name: 'Reset to default' })).not.toBeInTheDocument()
  })

  it('delegates model compatibility adjustment to the atomic model-change handler', () => {
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

    const menu = openMenu(openPicker(), 'gpt-5.5')
    fireEvent.click(within(menu).getByRole('menuitem', { name: /Model/ }))
    fireEvent.click(screen.getByRole('option', { name: 'gpt-5.5-mini' }))

    expect(onChange).toHaveBeenCalledWith('gpt-5.5-mini')
    expect(onReasoningChange).not.toHaveBeenCalled()
    expect(screen.queryByRole('dialog', { name: 'Select model' })).not.toBeInTheDocument()
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
    const menu = openMenu(openPicker(), 'mimo-v2.5-pro')
    fireEvent.click(within(menu).getByRole('menuitem', { name: /Model/ }))

    const listbox = screen.getByRole('listbox', { name: 'Model' })
    expect(within(listbox).queryByRole('option', { name: 'mimo-v2.5-pro' })).not.toBeInTheDocument()
    expect(within(listbox).getByRole('option', { name: 'claude-sonnet-4-5' })).toBeInTheDocument()
  })

  it('localizes the level and the scale from model catalog metadata', async () => {
    installDesktopApiMock({
        settings: { get: vi.fn().mockResolvedValue({ locale: 'zh-Hans' }) }
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
    const panel = screen.getByRole('dialog', { name: '选择模型' })
    expect(within(panel).getByRole('slider', { name: '思考强度' })).toHaveAttribute('aria-valuetext', '超高')
    expect(within(panel).getByRole('button', { name: 'mimo-v2.5-pro' })).toBeInTheDocument()
  })

  it('omits MAX Mode when no context handler is provided', () => {
    render(
      <LocaleProvider>
        <ModelPicker modelName="gpt-5.5" modelOptions={['gpt-5.5']} reasoningValue="off" triggerStyle={{}} />
      </LocaleProvider>
    )

    const panel = openPicker()
    expect(within(panel).queryByText('MAX')).not.toBeInTheDocument()
    const menu = openMenu(panel, 'gpt-5.5')
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

    const menu = openMenu(openPicker(), 'gpt-5.5')
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

    const menu = openMenu(openPicker(), 'my-local-model')
    const maxSwitch = within(menu).getByRole('switch', { name: 'MAX Mode' })
    expect(maxSwitch).toBeDisabled()

    fireEvent.click(maxSwitch)
    expect(onContextModeChange).not.toHaveBeenCalled()
  })

  it('surfaces a degraded MAX thread beside the level and lets the switch reset it', () => {
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

    const panel = openPicker()
    expect(within(panel).getByText('MAX')).toHaveClass('is-degraded')
    const menu = openMenu(panel, 'my-local-model')
    const maxSwitch = within(menu).getByRole('switch', { name: 'MAX Mode' })
    expect(maxSwitch).toHaveAttribute('aria-checked', 'true')
    expect(within(menu).getByText(/128K/)).toBeInTheDocument()

    fireEvent.click(maxSwitch)
    expect(onContextModeChange).toHaveBeenCalledWith('default')
  })

  it('uses Escape to leave a submenu, then the menu, before closing the picker', () => {
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

    const menu = openMenu(openPicker(), 'gpt-5.5')
    fireEvent.click(within(menu).getByRole('menuitem', { name: /Model/ }))
    expect(screen.getByRole('listbox', { name: 'Model' })).toBeInTheDocument()

    fireEvent.keyDown(document, { key: 'Escape' })
    expect(screen.queryByRole('listbox', { name: 'Model' })).not.toBeInTheDocument()
    expect(screen.getByRole('menu', { name: 'Model' })).toBeInTheDocument()

    fireEvent.keyDown(document, { key: 'Escape' })
    expect(screen.queryByRole('menu', { name: 'Model' })).not.toBeInTheDocument()
    expect(screen.getByRole('slider', { name: 'Intelligence' })).toBeInTheDocument()

    fireEvent.keyDown(document, { key: 'Escape' })
    expect(screen.queryByRole('dialog', { name: 'Select model' })).not.toBeInTheDocument()
  })

  it('keeps the current submenu while the pointer crosses another row inside the prediction cone', () => {
    vi.useFakeTimers()
    try {
      render(
        <LocaleProvider>
          <ModelPicker
            providerId="openai"
            providerOptions={[{ id: 'openai', displayName: 'OpenAI' }]}
            onProviderChange={vi.fn()}
            modelName="gpt-5.5"
            modelOptions={['gpt-5.5']}
            modelCatalog={[catalogModel('gpt-5.5', true, ['low', 'medium', 'high'], 'medium')]}
            reasoningValue="high"
            triggerStyle={{}}
          />
        </LocaleProvider>
      )

      const menu = openMenu(openPicker(), 'gpt-5.5')
      const providerRow = within(menu).getByRole('menuitem', { name: /Provider/ })
      const modelRow = within(menu).getByRole('menuitem', { name: /Model/ })
      fireEvent.click(providerRow)

      const providerListbox = screen.getByRole('listbox', { name: 'Provider' })
      vi.spyOn(providerListbox, 'getBoundingClientRect').mockReturnValue(domRect(280, 50, 280, 250))
      fireEvent.mouseMove(providerRow, { clientX: 100, clientY: 100 })
      fireEvent.mouseEnter(modelRow, { clientX: 180, clientY: 140 })

      expect(screen.getByRole('listbox', { name: 'Provider' })).toBeInTheDocument()
      expect(screen.queryByRole('listbox', { name: 'Model' })).not.toBeInTheDocument()

      act(() => vi.advanceTimersByTime(280))
      expect(screen.getByRole('listbox', { name: 'Model' })).toBeInTheDocument()
    } finally {
      vi.useRealTimers()
    }
  })

})

function openPicker(name = 'Select model'): HTMLElement {
  fireEvent.click(screen.getByRole('button', { name }))
  return screen.getByRole('dialog', { name })
}

function openMenu(panel: HTMLElement, modelName: string): HTMLElement {
  fireEvent.click(within(panel).getByRole('button', { name: modelName }))
  return within(panel).getByRole('menu')
}

function catalogModel(
  id: string,
  supportsDisable: boolean,
  efforts: Array<'low' | 'medium' | 'high' | 'extraHigh' | 'ultra'>,
  defaultEffort: 'low' | 'medium' | 'high' | 'extraHigh' | 'ultra',
  supportsFast = false
) {
  return {
    id,
    reasoning: {
      supportsDisable,
      supportedEfforts: efforts.map((effort) => ({ effort, label: effort, description: '' })),
      defaultEffort,
      supportedOutputs: ['full' as const],
      defaultOutput: 'full' as const
    },
    speed: supportsFast
      ? { supportedModes: ['standard' as const, 'fast' as const], defaultMode: 'standard' as const }
      : null
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
