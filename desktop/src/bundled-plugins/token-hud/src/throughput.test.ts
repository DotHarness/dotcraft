import { describe, expect, it } from 'vitest'
import { ThroughputMeter } from './throughput'

describe('ThroughputMeter', () => {
  it('divides output tokens by host-measured generation time', () => {
    const meter = new ThroughputMeter()
    meter.startTurn()
    meter.usage({ llmCall: 1, outputTokens: 1_250, timing: { firstOutputLatencyMs: 900, generationMs: 25_000 } })

    expect(meter.tokensPerSecond()).toBe(50)
    expect(meter.firstTokenLatencyMs()).toBe(900)
  })

  it('merges usage reports of one request and keeps its latest timing', () => {
    const meter = new ThroughputMeter()
    meter.startTurn()
    meter.usage({ llmCall: 1, outputTokens: 1, timing: { firstOutputLatencyMs: 800, generationMs: 0 } })
    meter.usage({ llmCall: 1, outputTokens: 79, timing: { firstOutputLatencyMs: 800, generationMs: 2_000 } })

    expect(meter.tokensPerSecond()).toBe(40)
    expect(meter.firstTokenLatencyMs()).toBe(800)
  })

  it('weights requests by generation time and averages first-output latency per request', () => {
    const meter = new ThroughputMeter()
    meter.startTurn()
    meter.usage({ llmCall: 1, outputTokens: 100, timing: { firstOutputLatencyMs: 1_000, generationMs: 1_000 } })
    meter.usage({ llmCall: 2, outputTokens: 300, timing: { firstOutputLatencyMs: 3_000, generationMs: 9_000 } })

    expect(meter.tokensPerSecond()).toBe(40)
    expect(meter.firstTokenLatencyMs()).toBe(2_000)
  })

  it('drops samples shorter than the minimum generation window', () => {
    const meter = new ThroughputMeter()
    meter.startTurn()
    meter.usage({ llmCall: 1, outputTokens: 100, timing: { firstOutputLatencyMs: 500, generationMs: 2_000 } })
    meter.usage({ llmCall: 2, outputTokens: 900, timing: { firstOutputLatencyMs: 500, generationMs: 20 } })

    expect(meter.tokensPerSecond()).toBe(50)
  })

  it('reports nothing for usage without host timing', () => {
    const meter = new ThroughputMeter()
    meter.startTurn()
    meter.usage({ llmCall: 1, outputTokens: 100, timing: null })

    expect(meter.tokensPerSecond()).toBeNull()
    expect(meter.firstTokenLatencyMs()).toBeNull()
  })
})
