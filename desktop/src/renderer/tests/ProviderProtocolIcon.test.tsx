import { describe, expect, it } from 'vitest'
import { render } from '@testing-library/react'
import {
  getProviderProtocolMarkKind,
  ProviderProtocolIcon
} from '../components/settings/panels/ProviderProtocolIcon'

describe('ProviderProtocolIcon', () => {
  it('maps OpenAI protocols to the OpenAI provider mark', () => {
    expect(getProviderProtocolMarkKind('openai-responses')).toBe('openai')
    expect(getProviderProtocolMarkKind('openai-chat-completions')).toBe('openai')
    expect(getProviderProtocolMarkKind('openai')).toBe('openai')
  })

  it('maps Anthropic protocol to the Anthropic provider mark', () => {
    expect(getProviderProtocolMarkKind('anthropic')).toBe('anthropic')
  })

  it('renders a decorative inline protocol mark', () => {
    const { container } = render(<ProviderProtocolIcon protocol="openai-responses" />)

    const image = container.querySelector('img')
    const mark = container.querySelector('svg[data-provider-mark="openai"]')
    expect(image).toBeNull()
    expect(mark).not.toBeNull()
    expect(mark).toHaveAttribute('aria-hidden', 'true')
    expect(mark).toHaveAttribute('focusable', 'false')
    expect(mark).toHaveAttribute('fill', 'currentColor')
  })
})
