import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { ScrollToBottomButton } from '../components/conversation/ScrollToBottomButton'

describe('ScrollToBottomButton', () => {
  it('uses the default bottom offset when no dock needs to be avoided', () => {
    render(<ScrollToBottomButton onClick={vi.fn()} />)

    const wrapper = screen.getByRole('button', { name: 'Scroll to bottom' }).parentElement

    expect(wrapper?.getAttribute('style')).toContain('bottom: 10px')
  })

  it('accepts a raised bottom offset so it can clear the background activity dock', () => {
    render(<ScrollToBottomButton onClick={vi.fn()} bottomOffsetPx={82} />)

    const wrapper = screen.getByRole('button', { name: 'Scroll to bottom' }).parentElement

    expect(wrapper?.getAttribute('style')).toContain('bottom: 82px')
  })
})
