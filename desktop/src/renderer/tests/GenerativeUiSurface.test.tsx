import { fireEvent, render, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { GenerativeUiSurface } from '../components/conversation/generativeUi/GenerativeUiView'
import type { GenerativeUiNode } from '../components/conversation/generativeUi/generativeUiProtocol'
import type { GenerativeUiRunnerState } from '../components/conversation/generativeUi/useGenerativeUiRunner'

const tree: GenerativeUiNode[] = [
  { type: 'Card', props: {}, children: [
    { type: 'Input', props: { value: 'Ada', onChange: { $callback: 'r.0:onChange' } }, children: [] },
    { type: 'Button', props: { label: 'Export', onClick: { $callback: 'r.1:onClick' } }, children: [] }
  ] }
]

function renderSurface(state: GenerativeUiRunnerState) {
  const invoke = vi.fn()
  const onRetry = vi.fn()
  const view = render(
    <LocaleProvider loadSettings={false}>
      <GenerativeUiSurface state={state} invoke={invoke} onHostError={vi.fn()} onRetry={onRetry} />
    </LocaleProvider>
  )
  return { ...view, invoke, onRetry }
}

describe('GenerativeUiSurface', () => {
  it('keeps the last frame inert after a post-render failure', () => {
    const { container, invoke, onRetry } = renderSurface({
      status: 'failed',
      tree,
      failure: { stage: 'callback_error', message: 'Export target is not configured', line: 5 }
    })

    const frame = container.querySelector<HTMLElement>('[aria-disabled="true"]')!
    expect(frame).toHaveAttribute('inert')
    const button = within(frame).getByRole('button')
    const input = within(frame).getByRole('textbox')
    expect(button).toBeDisabled()
    expect(input).toBeDisabled()

    fireEvent.click(button)
    fireEvent.change(input, { target: { value: 'Grace' } })
    expect(invoke).not.toHaveBeenCalled()

    fireEvent.click(within(screen.getByRole('status')).getByRole('button'))
    expect(onRetry).toHaveBeenCalledTimes(1)
  })

  it('delivers callbacks while the frame is live', () => {
    const { invoke } = renderSurface({ status: 'ready', tree, failure: null })

    fireEvent.click(screen.getByRole('button'))

    expect(screen.queryByRole('status')).toBeNull()
    expect(invoke).toHaveBeenCalledWith('r.1:onClick', [])
  })
})
