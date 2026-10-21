export const MIN_SAMPLE_MS = 300

export type ContentKind = 'text' | 'reasoning' | 'toolArguments'

export interface UsageSample {
  readonly llmCall: number | null
  readonly outputTokens: number
  readonly reasoningTokens: number
}

interface Segment {
  readonly startMs: number
  readonly latencyMs: number | null
  sawReasoning: boolean
}

interface RequestSample {
  readonly llmCall: number | null
  startMs: number | null
  endMs: number
  outputTokens: number
  reasoningTokens: number
  sawReasoning: boolean
}

export class ThroughputMeter {
  private settledTokens = 0
  private settledDurationMs = 0
  private latencyTotalMs = 0
  private latencySamples = 0
  private waitStartedAtMs: number | null = null
  private segment: Segment | null = null
  private request: RequestSample | null = null

  startTurn(nowMs: number): void {
    this.settledTokens = 0
    this.settledDurationMs = 0
    this.latencyTotalMs = 0
    this.latencySamples = 0
    this.segment = null
    this.request = null
    this.waitStartedAtMs = nowMs
  }

  endTurn(): void {
    this.settle()
    this.request = null
    this.segment = null
    this.waitStartedAtMs = null
  }

  content(nowMs: number, kind: ContentKind): void {
    if (this.segment === null) {
      const latencyMs = this.waitStartedAtMs === null ? null : Math.max(0, nowMs - this.waitStartedAtMs)
      this.segment = { startMs: nowMs, latencyMs, sawReasoning: false }
      if (latencyMs !== null) {
        this.latencyTotalMs += latencyMs
        this.latencySamples += 1
      }
      this.waitStartedAtMs = null
    }
    if (kind === 'reasoning') this.segment.sawReasoning = true
  }

  /** Local work finishing while the next request is pending restarts the wait, so tool time is not latency. */
  itemCompleted(nowMs: number): void {
    if (this.waitStartedAtMs !== null) this.waitStartedAtMs = nowMs
  }

  usage(nowMs: number, sample: UsageSample): void {
    const segment = this.segment
    const current = this.request
    if (current !== null && sample.llmCall !== null && current.llmCall === sample.llmCall) {
      if (segment !== null) {
        if (current.startMs === null) {
          current.startMs = segment.startMs
        } else if (segment.latencyMs !== null) {
          // Content between two usage reports of one request is not a new first token.
          this.latencyTotalMs -= segment.latencyMs
          this.latencySamples -= 1
        }
        current.sawReasoning ||= segment.sawReasoning
      }
      current.endMs = nowMs
      current.outputTokens += sample.outputTokens
      current.reasoningTokens += sample.reasoningTokens
    } else {
      this.settle()
      this.request = {
        llmCall: sample.llmCall,
        startMs: segment?.startMs ?? null,
        endMs: nowMs,
        outputTokens: sample.outputTokens,
        reasoningTokens: sample.reasoningTokens,
        sawReasoning: segment?.sawReasoning ?? false
      }
    }
    // A report that precedes any content (such as a request-start usage event) does not end a request.
    if (segment !== null) this.waitStartedAtMs = nowMs
    this.segment = null
  }

  tokensPerSecond(): number | null {
    const open = this.request === null ? null : measure(this.request)
    const tokens = this.settledTokens + (open?.tokens ?? 0)
    const durationMs = this.settledDurationMs + (open?.durationMs ?? 0)
    return durationMs > 0 ? tokens / (durationMs / 1000) : null
  }

  firstTokenLatencyMs(): number | null {
    return this.latencySamples > 0 ? this.latencyTotalMs / this.latencySamples : null
  }

  private settle(): void {
    const measured = this.request === null ? null : measure(this.request)
    if (measured === null) return
    this.settledTokens += measured.tokens
    this.settledDurationMs += measured.durationMs
  }
}

function measure(request: RequestSample): { tokens: number; durationMs: number } | null {
  if (request.startMs === null) return null
  const durationMs = request.endMs - request.startMs
  // Reasoning that never streamed was generated before the observed window opened.
  const hiddenReasoning = request.sawReasoning ? 0 : request.reasoningTokens
  const tokens = request.outputTokens - hiddenReasoning
  return durationMs >= MIN_SAMPLE_MS && tokens > 0 ? { tokens, durationMs } : null
}
