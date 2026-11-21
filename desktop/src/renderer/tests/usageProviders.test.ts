import { describe, expect, it } from 'vitest'
import { collectUsageProviderIds } from '../components/conversation/providerUsage/useUsageProviders'
import { orderBySeverity } from '../components/conversation/providerUsage/usageReading'
import { packColumns } from '../components/conversation/providerUsage/MultiProviderUsagePanel'

describe('collectUsageProviderIds', () => {
  it('orders the main provider, its subagent model provider, then subagent model choices without duplicates', () => {
    const preferences = {
      chatgpt: { providerId: 'deepseek', model: 'deepseek-chat' },
      other: { providerId: 'kimi', model: 'k2' }
    }
    const models = [
      { ProviderId: 'OpenRouter', Model: 'anthropic/claude' },
      { ProviderId: 'DeepSeek', Model: 'deepseek-reasoner' },
      { ProviderId: 'ChatGPT', Model: 'gpt-5.6-mini' }
    ]

    expect(collectUsageProviderIds('ChatGPT', preferences, models)).toEqual(['ChatGPT', 'deepseek', 'OpenRouter'])
  })
})

describe('orderBySeverity', () => {
  it('puts blocked providers first and keeps configured order within a tone', () => {
    const entries = [
      { id: 'a', reading: { tone: 'ok' as const } },
      { id: 'b', reading: { tone: 'error' as const } },
      { id: 'c', reading: { tone: 'warning' as const } },
      { id: 'd', reading: { tone: 'ok' as const } },
      { id: 'e', reading: { tone: 'error' as const } }
    ]

    expect(orderBySeverity(entries).map((entry) => entry.id)).toEqual(['b', 'e', 'c', 'a', 'd'])
  })
})

describe('packColumns', () => {
  it('starts a new card only when the next section would pass the limit', () => {
    expect(packColumns([200, 150, 100, 120], 30, 10, 520)).toEqual([[0, 1, 2], [3]])
  })
})
