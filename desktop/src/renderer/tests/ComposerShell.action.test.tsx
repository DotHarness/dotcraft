import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ComposerSendButton, SendIcon, StopIcon } from '../components/conversation/ComposerShell'

describe('ComposerShell action buttons', () => {
  it.each([
    { label: 'Send message', icon: <SendIcon /> },
    { label: 'Stop turn', icon: <StopIcon /> }
  ])('keeps the $label button stationary on hover', ({ label, icon }) => {
    render(
      <ComposerSendButton tone="enabled" aria-label={label}>
        {icon}
      </ComposerSendButton>
    )
    const button = screen.getByRole('button', { name: label })

    fireEvent.mouseEnter(button)

    expect(button.style.transform).toBe('')
    expect(button.style.transition).toBe('background-color 100ms ease')
  })
})
