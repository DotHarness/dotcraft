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
      if (request.timing === null || request.timing.generationMs < MIN_SAMPLE_MS || request.outputTokens <= 0) continue
      tokens += request.outputTokens
      generationMs += request.timing.generationMs
    }
    return generationMs > 0 ? tokens / (generationMs / 1000) : null
  }

  firstTokenLatencyMs(): number | null {
    const timed = this.requests.flatMap((request) => request.timing === null ? [] : [request.timing])
    return timed.length > 0
      ? timed.reduce((total, timing) => total + timing.firstOutputLatencyMs, 0) / timed.length
      : null
  }
}
