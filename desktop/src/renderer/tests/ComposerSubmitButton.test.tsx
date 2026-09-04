import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ComposerSubmitButton } from '../components/conversation/ComposerSubmitButton'

vi.mock('../contexts/LocaleContext', () => ({
  useT: () => (key: string) => key
}))

describe('ComposerSubmitButton', () => {
  it('keeps one button across modes and names it by the turn it acts on', () => {
    const { rerender } = render(<ComposerSubmitButton mode="send" disabled onClick={() => {}} />)
    const button = screen.getByRole('button', { name: 'composer.sendAriaAlt' })
    expect(button).toBeDisabled()
    expect(button.querySelector('.composer-submit-glyphs')).toHaveAttribute('data-glyph', 'send')

    rerender(<ComposerSubmitButton mode="stop" onClick={() => {}} />)
    expect(screen.getByRole('button', { name: 'composer.stopAria' })).toBe(button)
    expect(button).not.toBeDisabled()
    expect(button).not.toHaveAttribute('aria-busy')
    expect(button.querySelector('.composer-submit-glyphs')).toHaveAttribute('data-glyph', 'stop')

    rerender(<ComposerSubmitButton mode="stopping" tone="enabled" disabled onClick={() => {}} />)
    expect(screen.getByRole('button', { name: 'composer.stoppingAria' })).toBe(button)
    expect(button).toBeDisabled()
    expect(button).toHaveAttribute('aria-busy', 'true')
    expect(button.querySelector('.composer-submit-glyphs')).toHaveAttribute('data-glyph', 'stopping')
  })
})
