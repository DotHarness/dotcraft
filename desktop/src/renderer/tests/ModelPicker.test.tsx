import { fireEvent, render, screen, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ModelPicker } from '../components/conversation/ModelPicker'

describe('ModelPicker', () => {
  beforeEach(() => {
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) }
      }
    })
  })

  it('renders thinking and model sections from model catalog reasoning metadata', () => {
    render(
      <LocaleProvider>
        <ModelPicker
          modelName="claude-opus-4-7"
          modelOptions={['claude-opus-4-7']}
          modelCatalog={[
            {
              id: 'claude-opus-4-7',
              reasoning: {
                supportsDisable: false,
                supportedEfforts: [
                  { effort: 'high', label: 'High' },
                  { effort: 'extraHigh', label: 'Extra High' }
                ],
                defaultEffort: 'extraHigh',
                supportedOutputs: ['full'],
                defaultOutput: 'full'
              }
            }
          ]}
          reasoningValue="high"
          triggerStyle={{}}
        />
      </LocaleProvider>
    )

    fireEvent.click(screen.getByRole('button', { name: 'Select model' }))

    const listbox = screen.getByRole('listbox', { name: 'Select model' })
    expect(within(listbox).getByText('Intelligence')).toBeInTheDocument()
    expect(within(listbox).getByText('Model')).toBeInTheDocument()
    expect(within(listbox).getByRole('option', { name: /xHigh/ })).toBeInTheDocument()
    expect(within(listbox).getByRole('option', { name: /Off/ })).toBeDisabled()
  })

  it('applies a thinking selection without changing the model', () => {
    const onReasoningChange = vi.fn()
    const onChange = vi.fn()

    render(
      <LocaleProvider>
        <ModelPicker
          modelName="claude-opus-4-7"
          modelOptions={['claude-opus-4-7']}
          modelCatalog={[
            {
              id: 'claude-opus-4-7',
              reasoning: {
                supportsDisable: true,
                supportedEfforts: [
                  { effort: 'high', label: 'High' },
                  { effort: 'extraHigh', label: 'Extra High' }
                ],
                defaultEffort: 'high',
                supportedOutputs: ['full'],
                defaultOutput: 'full'
              }
            }
          ]}
          reasoningValue="high"
          triggerStyle={{}}
          onChange={onChange}
          onReasoningChange={onReasoningChange}
        />
      </LocaleProvider>
    )

    fireEvent.click(screen.getByRole('button', { name: 'Select model' }))
    fireEvent.click(screen.getByRole('option', { name: /xHigh/ }))

    expect(onReasoningChange).toHaveBeenCalledWith('extraHigh')
    expect(onChange).not.toHaveBeenCalled()
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
    fireEvent.click(screen.getByRole('button', { name: 'Select model' }))

    const listbox = screen.getByRole('listbox', { name: 'Select model' })
    expect(within(listbox).queryByRole('option', { name: 'mimo-v2.5-pro' })).not.toBeInTheDocument()
    expect(within(listbox).getByRole('option', { name: 'claude-sonnet-4-5' })).toBeInTheDocument()
  })

  it('localizes reasoning options from model catalog metadata', async () => {
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
          modelCatalog={[
            {
              id: 'mimo-v2.5-pro',
              reasoning: {
                supportsDisable: true,
                supportedEfforts: [
                  { effort: 'low', label: 'Low', description: 'Faster, lighter reasoning.' },
                  { effort: 'medium', label: 'Medium', description: 'Balanced reasoning.' },
                  { effort: 'high', label: 'High', description: 'Deeper reasoning.' },
                  { effort: 'extraHigh', label: 'Extra High', description: 'Maximum depth for supported models.' }
                ],
                defaultEffort: 'extraHigh',
                supportedOutputs: ['full'],
                defaultOutput: 'full'
              }
            }
          ]}
          reasoningValue="extraHigh"
          triggerStyle={{}}
        />
      </LocaleProvider>
    )

    fireEvent.click(await screen.findByRole('button', { name: '选择模型' }))

    const listbox = screen.getByRole('listbox', { name: '选择模型' })
    expect(within(listbox).getByText('思考强度')).toBeInTheDocument()
    expect(within(listbox).getByText('低')).toBeInTheDocument()
    expect(within(listbox).getByText('中')).toBeInTheDocument()
    expect(within(listbox).getByText('高')).toBeInTheDocument()
    expect(within(listbox).getByRole('option', { name: /超高/ })).toBeInTheDocument()
    expect(within(listbox).getByText('支持模型的最高深度。')).toBeInTheDocument()
  })

  it('omits the Context (MAX) section when no context handler is provided', () => {
    render(
      <LocaleProvider>
        <ModelPicker modelName="gpt-5.5" modelOptions={['gpt-5.5']} reasoningValue="off" triggerStyle={{}} />
      </LocaleProvider>
    )

    fireEvent.click(screen.getByRole('button', { name: 'Select model' }))
    const listbox = screen.getByRole('listbox', { name: 'Select model' })
    expect(within(listbox).queryByText('Context')).not.toBeInTheDocument()
    expect(within(listbox).queryByRole('switch', { name: 'MAX Mode' })).not.toBeInTheDocument()
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

    fireEvent.click(screen.getByRole('button', { name: 'Select model' }))
    const listbox = screen.getByRole('listbox', { name: 'Select model' })
    const maxSwitch = within(listbox).getByRole('switch', { name: 'MAX Mode' })
    expect(maxSwitch).not.toBeDisabled()
    expect(maxSwitch).toHaveAttribute('aria-checked', 'false')

    fireEvent.click(maxSwitch)
    expect(onContextModeChange).toHaveBeenCalledWith('max')
  })

  it('disables MAX and explains why when the model does not support it', () => {
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

    fireEvent.click(screen.getByRole('button', { name: 'Select model' }))
    const listbox = screen.getByRole('listbox', { name: 'Select model' })
    const maxSwitch = within(listbox).getByRole('switch', { name: 'MAX Mode' })
    expect(maxSwitch).toBeDisabled()
    expect(within(listbox).getByText(/available for the current model/)).toBeInTheDocument()

    fireEvent.click(maxSwitch)
    expect(onContextModeChange).not.toHaveBeenCalled()
  })

  it('surfaces a degraded MAX thread with a reset affordance', () => {
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

    fireEvent.click(screen.getByRole('button', { name: 'Select model' }))
    const listbox = screen.getByRole('listbox', { name: 'Select model' })
    expect(within(listbox).getByRole('switch', { name: 'MAX Mode' })).toHaveAttribute('aria-checked', 'true')
    expect(within(listbox).getByText(/128K/)).toBeInTheDocument()

    fireEvent.click(within(listbox).getByRole('button', { name: 'Switch to default' }))
    expect(onContextModeChange).toHaveBeenCalledWith('default')
  })
})
