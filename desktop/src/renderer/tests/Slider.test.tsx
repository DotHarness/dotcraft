import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { Slider } from '../components/ui/Slider'

describe('Slider', () => {
  it('exposes the range contract and reports numeric changes', () => {
    const onValueChange = vi.fn()
    const onValueCommit = vi.fn()
    render(
      <Slider
        value={24}
        min={0}
        max={80}
        step={2}
        ariaLabel="Dim"
        valueText="24%"
        onValueChange={onValueChange}
        onValueCommit={onValueCommit}
      />
    )

    const slider = screen.getByRole('slider', { name: 'Dim' })
    expect(slider).toHaveAttribute('min', '0')
    expect(slider).toHaveAttribute('max', '80')
    expect(slider).toHaveAttribute('step', '2')
    expect(slider).toHaveAttribute('aria-valuetext', '24%')
    expect(screen.getByText('24%')).toBeInTheDocument()

    fireEvent.pointerUp(slider)
    expect(onValueCommit).not.toHaveBeenCalled()

    fireEvent.change(slider, { target: { value: '38' } })
    expect(onValueChange).toHaveBeenCalledWith(38)
    expect(onValueCommit).not.toHaveBeenCalled()

    fireEvent.pointerUp(slider)
    expect(onValueCommit).toHaveBeenCalledOnce()
    expect(onValueCommit).toHaveBeenCalledWith(38)

    fireEvent.blur(slider)
    expect(onValueCommit).toHaveBeenCalledOnce()

    fireEvent.change(slider, { target: { value: '40' } })
    fireEvent.change(slider, { target: { value: '38' } })
    fireEvent.pointerUp(slider)
    expect(onValueCommit).toHaveBeenCalledOnce()
  })

  it('commits keyboard changes once when the key is released', () => {
    const onValueCommit = vi.fn()
    const { rerender } = render(
      <Slider
        value={20}
        min={0}
        max={100}
        ariaLabel="Opacity"
        onValueChange={() => undefined}
        onValueCommit={onValueCommit}
      />
    )

    const slider = screen.getByRole('slider', { name: 'Opacity' })
    fireEvent.change(slider, { target: { value: '30' } })
    fireEvent.keyUp(slider, { key: 'ArrowRight' })
    fireEvent.keyUp(slider, { key: 'ArrowRight' })
    expect(onValueCommit).toHaveBeenCalledTimes(1)
    expect(onValueCommit).toHaveBeenCalledWith(30)

    rerender(
      <Slider
        value={30}
        min={0}
        max={100}
        ariaLabel="Opacity"
        onValueChange={() => undefined}
        onValueCommit={onValueCommit}
      />
    )
    fireEvent.keyUp(slider, { key: 'Home' })
    expect(onValueCommit).toHaveBeenCalledTimes(1)
  })

  it('keeps disabled sliders non-interactive', () => {
    render(
      <Slider
        value={50}
        min={0}
        max={100}
        ariaLabel="Opacity"
        disabled
        onValueChange={() => undefined}
      />
    )

    expect(screen.getByRole('slider', { name: 'Opacity' })).toBeDisabled()
  })
})
