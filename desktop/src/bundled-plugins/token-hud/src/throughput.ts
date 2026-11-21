export const MIN_SAMPLE_MS = 300

export interface RequestTiming {
  readonly firstOutputLatencyMs: number
  readonly generationMs: number
}

export interface UsageSample {
  readonly llmCall: number | null
  readonly outputTokens: number
  readonly timing: RequestTiming | null
}

interface RequestSample {
  readonly llmCall: number | null
  outputTokens: number
  timing: RequestTiming | null
}

export class ThroughputMeter {
  private requests: RequestSample[] = []

  startTurn(): void {
    this.requests = []
  }

  usage(sample: UsageSample): void {
    const last = this.requests.at(-1)
    if (last !== undefined && sample.llmCall !== null && last.llmCall === sample.llmCall) {
      last.outputTokens += sample.outputTokens
      last.timing = sample.timing ?? last.timing
      return
    }
    this.requests.push({ ...sample })
  }

  tokensPerSecond(): number | null {
    let tokens = 0
    let generationMs = 0
    for (const request of this.requests) {
      const sample = speedSample(request)
      if (sample === null) continue
      tokens += sample.tokens
      generationMs += sample.generationMs
    }
    return generationMs > 0 ? tokens / (generationMs / 1000) : null
  }

  latestRequestTokensPerSecond(): number | null {
    for (let index = this.requests.length - 1; index >= 0; index--) {
      const sample = speedSample(this.requests[index])
      if (sample !== null) return sample.tokens / (sample.generationMs / 1000)
    }
    return null
  }

  firstTokenLatencyMs(): number | null {
    const timed = this.requests.flatMap((request) => request.timing === null ? [] : [request.timing])
    return timed.length > 0
      ? timed.reduce((total, timing) => total + timing.firstOutputLatencyMs, 0) / timed.length
      : null
  }
}

function speedSample(request: RequestSample): { tokens: number; generationMs: number } | null {
  const { timing } = request
  if (timing === null || timing.generationMs < MIN_SAMPLE_MS || request.outputTokens <= 0) return null
  return { tokens: request.outputTokens, generationMs: timing.generationMs }
}
