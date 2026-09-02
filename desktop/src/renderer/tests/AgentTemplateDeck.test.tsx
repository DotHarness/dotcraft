import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { AgentTemplateDeck } from '../components/agents/AgentTemplateDeck'
import { avatarFromSeed } from '../components/agents/agentAvatar'
import { installDesktopApiMock } from './desktopApiMock'

const templates = ['leader', 'builder', 'explorer', 'operator', 'reviewer'].map((id) => ({
  key: id,
  name: id,
  description: `${id} description`,
  avatar: avatarFromSeed(id)
}))

function renderDeck(onPick = vi.fn()): HTMLElement[] {
  render(
    <LocaleProvider>
      <AgentTemplateDeck templates={templates} onPick={onPick} />
    </LocaleProvider>
  )
  return screen.getAllByTestId('agent-template-card')
}

describe('AgentTemplateDeck', () => {
  beforeEach(() => {
    installDesktopApiMock({
      settings: { get: async () => ({ locale: 'en' }) }
    })
  })

  it('fans one card per template around the middle and reports the picked key', () => {
    const onPick = vi.fn()
    const cards = renderDeck(onPick)

    expect(cards).toHaveLength(5)
    expect(cards[0].style.getPropertyValue('--deck-rot')).toBe('-12deg')
    expect(cards[0].style.getPropertyValue('--deck-y')).toBe('28px')
    expect(cards[2].style.getPropertyValue('--deck-rot')).toBe('0deg')
    expect(cards[4].style.getPropertyValue('--deck-rot')).toBe('12deg')

    fireEvent.click(cards[3])
    expect(onPick).toHaveBeenCalledWith('operator')
  })

  it('lifts the focused card and parts its neighbours', () => {
    const cards = renderDeck()

    act(() => cards[1].focus())

    expect(cards[1].classList.contains('is-active')).toBe(true)
    expect(cards[0].style.getPropertyValue('--deck-x')).toBe('-14px')
    expect(cards[1].style.getPropertyValue('--deck-x')).toBe('0px')
    expect(cards[2].style.getPropertyValue('--deck-x')).toBe('14px')
  })
})
