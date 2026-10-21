import { describe, expect, it } from 'vitest'
import { ThroughputMeter } from './throughput'

describe('ThroughputMeter', () => {
  it('excludes reasoning tokens that never streamed inside the measured window', () => {
    const meter = new ThroughputMeter()
    meter.startTurn(0)
    meter.content(10_000, 'text')
    meter.usage(11_000, { llmCall: 1, outputTokens: 1_250, reasoningTokens: 1_200 })

    expect(meter.tokensPerSecond()).toBe(50)
  })

  it('keeps reasoning tokens when the reasoning itself streamed', () => {
    const meter = new ThroughputMeter()
    meter.startTurn(0)
    meter.content(1_000, 'reasoning')
    meter.content(3_000, 'text')
    meter.usage(5_000, { llmCall: 1, outputTokens: 400, reasoningTokens: 300 })

    expect(meter.tokensPerSecond()).toBe(100)
  })

  it('drops burst samples shorter than the minimum window', () => {
    const meter = new ThroughputMeter()
    meter.startTurn(0)
    meter.content(1_000, 'text')
    meter.usage(3_000, { llmCall: 1, outputTokens: 100, reasoningTokens: 0 })
    meter.itemCompleted(4_000)
    meter.content(5_000, 'toolArguments')
    meter.usage(5_020, { llmCall: 2, outputTokens: 900, reasoningTokens: 0 })
    meter.endTurn()

    expect(meter.tokensPerSecond()).toBe(50)
  })

  it('merges usage reports of one request and counts its first token once', () => {
    const meter = new ThroughputMeter()
    meter.startTurn(0)
    meter.usage(100, { llmCall: 1, outputTokens: 1, reasoningTokens: 0 })
    meter.content(1_000, 'text')
    meter.usage(2_000, { llmCall: 1, outputTokens: 40, reasoningTokens: 0 })
    meter.content(2_200, 'text')
    meter.usage(3_000, { llmCall: 1, outputTokens: 39, reasoningTokens: 0 })

    expect(meter.tokensPerSecond()).toBe(40)
    expect(meter.firstTokenLatencyMs()).toBe(1_000)
  })

  it('times the next request from the end of local tool work', () => {
    const meter = new ThroughputMeter()
    meter.startTurn(0)
    meter.content(2_000, 'text')
    meter.usage(3_000, { llmCall: 1, outputTokens: 50, reasoningTokens: 0 })
    meter.itemCompleted(9_000)
    meter.content(9_600, 'text')

    expect(meter.firstTokenLatencyMs()).toBe(1_300)
  })
})
