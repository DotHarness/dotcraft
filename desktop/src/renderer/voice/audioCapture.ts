import { VOICE_SAMPLE_RATE } from '../../shared/voice'

const WORKLET_SOURCE = `
class DotCraftVoiceCapture extends AudioWorkletProcessor {
  constructor() {
    super();
    this.frames = [];
    this.frameCount = 0;
  }
  process(inputs) {
    const channel = inputs[0] && inputs[0][0];
    if (!channel || channel.length === 0) return true;
    this.frames.push(new Float32Array(channel));
    this.frameCount += channel.length;
    if (this.frames.length >= 8) {
      const batch = new Float32Array(this.frameCount);
      let offset = 0;
      for (const frame of this.frames) { batch.set(frame, offset); offset += frame.length; }
      this.frames = [];
      this.frameCount = 0;
      this.port.postMessage(batch, [batch.buffer]);
    }
    return true;
  }
}
registerProcessor('dotcraft-voice-capture', DotCraftVoiceCapture);
`

export interface CapturedVoiceAudio {
  pcm16: ArrayBuffer
  durationMs: number
}

export type CaptureErrorCode =
  | 'permission-denied'
  | 'device-missing'
  | 'device-unavailable'
  | 'invalid-audio'

export class VoiceCaptureError extends Error {
  constructor(readonly code: CaptureErrorCode, options?: ErrorOptions) {
    super(code, options)
    this.name = 'VoiceCaptureError'
  }
}

export class VoiceAudioCapture {
  private readonly chunks: Float32Array[] = []
  private closed = false

  private constructor(
    private readonly stream: MediaStream,
    private readonly context: AudioContext,
    private readonly source: MediaStreamAudioSourceNode,
    private readonly processor: CaptureProcessor,
    private readonly silentGain: GainNode,
    private readonly moduleUrl: string | null,
    private readonly onLevel: (level: number) => void
  ) {}

  static async start(
    deviceId: string | undefined,
    onLevel: (level: number) => void,
    onDefaultDeviceFallback?: () => void
  ): Promise<VoiceAudioCapture> {
    if (!navigator.mediaDevices?.getUserMedia) throw new VoiceCaptureError('device-missing')
    let stream: MediaStream
    try {
      stream = await requestStream(deviceId)
    } catch (error) {
      if (!deviceId || !isMissingSelectedDeviceError(error)) {
        throw new VoiceCaptureError(mapMediaError(error))
      }
      try {
        stream = await requestStream(undefined)
        onDefaultDeviceFallback?.()
      } catch (fallbackError) {
        throw new VoiceCaptureError(mapMediaError(fallbackError))
      }
    }

    let context: AudioContext | null = null
    try {
      context = new AudioContext({ latencyHint: 'interactive' })
      const source = context.createMediaStreamSource(stream)
      const silentGain = context.createGain()
      silentGain.gain.value = 0
      const { processor, moduleUrl } = await createCaptureProcessor(context)
      source.connect(processor.node).connect(silentGain).connect(context.destination)
      if (context.state === 'suspended') await context.resume().catch(() => {})
      const capture = new VoiceAudioCapture(stream, context, source, processor, silentGain, moduleUrl, onLevel)
      processor.setHandler((chunk) => capture.accept(chunk))
      return capture
    } catch (error) {
      for (const track of stream.getTracks()) track.stop()
      await context?.close().catch(() => {})
      throw new VoiceCaptureError('device-unavailable', { cause: error })
    }
  }

  async stop(): Promise<CapturedVoiceAudio> {
    if (this.closed) throw new VoiceCaptureError('invalid-audio')
    this.closed = true
    await new Promise<void>((resolve) => setTimeout(resolve, 20))
    const sourceSampleRate = this.context.sampleRate
    await this.disposeGraph()
    const source = merge(this.chunks)
    if (source.length === 0) throw new VoiceCaptureError('invalid-audio')
    const resampled = resampleLinear(source, sourceSampleRate, VOICE_SAMPLE_RATE)
    const pcm16 = floatToPcm16(resampled)
    return {
      pcm16: pcm16.buffer.slice(pcm16.byteOffset, pcm16.byteOffset + pcm16.byteLength) as ArrayBuffer,
      durationMs: Math.round((resampled.length / VOICE_SAMPLE_RATE) * 1_000)
    }
  }

  async abort(): Promise<void> {
    if (this.closed) return
    this.closed = true
    await this.disposeGraph()
    this.chunks.length = 0
  }

  private accept(chunk: Float32Array): void {
    if (this.closed || !(chunk instanceof Float32Array) || chunk.length === 0) return
    this.chunks.push(chunk)
    let energy = 0
    for (let index = 0; index < chunk.length; index += 1) energy += chunk[index] * chunk[index]
    const rms = Math.sqrt(energy / chunk.length)
    const normalized = Math.max(0, Math.min(1, (rms - 0.008) / 0.18))
    this.onLevel(Math.pow(normalized, 0.65))
  }

  private async disposeGraph(): Promise<void> {
    this.processor.clearHandler()
    this.source.disconnect()
    this.processor.node.disconnect()
    this.silentGain.disconnect()
    for (const track of this.stream.getTracks()) track.stop()
    if (this.moduleUrl) URL.revokeObjectURL(this.moduleUrl)
    await this.context.close().catch(() => {})
  }
}

interface CaptureProcessor {
  node: AudioNode
  setHandler(handler: (chunk: Float32Array) => void): void
  clearHandler(): void
}

async function createCaptureProcessor(
  context: AudioContext
): Promise<{ processor: CaptureProcessor; moduleUrl: string | null }> {
  let moduleUrl: string | null = null
  try {
    moduleUrl = URL.createObjectURL(new Blob([WORKLET_SOURCE], { type: 'text/javascript' }))
    await context.audioWorklet.addModule(moduleUrl)
    const worklet = new AudioWorkletNode(context, 'dotcraft-voice-capture', {
      numberOfInputs: 1,
      numberOfOutputs: 1,
      outputChannelCount: [1]
    })
    return {
      moduleUrl,
      processor: {
        node: worklet,
        setHandler(handler) {
          worklet.port.onmessage = (event: MessageEvent<Float32Array>) => handler(event.data)
        },
        clearHandler() {
          worklet.port.onmessage = null
        }
      }
    }
  } catch {
    if (moduleUrl) URL.revokeObjectURL(moduleUrl)
    const processor = context.createScriptProcessor(2048, 1, 1)
    return {
      moduleUrl: null,
      processor: {
        node: processor,
        setHandler(handler) {
          processor.onaudioprocess = (event) => handler(event.inputBuffer.getChannelData(0).slice())
        },
        clearHandler() {
          processor.onaudioprocess = null
        }
      }
    }
  }
}

function merge(chunks: Float32Array[]): Float32Array {
  const total = chunks.reduce((sum, chunk) => sum + chunk.length, 0)
  const merged = new Float32Array(total)
  let offset = 0
  for (const chunk of chunks) {
    merged.set(chunk, offset)
    offset += chunk.length
  }
  return merged
}

export function resampleLinear(input: Float32Array, sourceRate: number, targetRate: number): Float32Array {
  if (input.length === 0 || sourceRate <= 0 || targetRate <= 0) return new Float32Array()
  if (sourceRate === targetRate) return input.slice()
  const outputLength = Math.max(1, Math.round(input.length * targetRate / sourceRate))
  const output = new Float32Array(outputLength)
  const ratio = sourceRate / targetRate
  for (let index = 0; index < outputLength; index += 1) {
    const position = index * ratio
    const left = Math.min(input.length - 1, Math.floor(position))
    const right = Math.min(input.length - 1, left + 1)
    const mix = position - left
    output[index] = input[left] * (1 - mix) + input[right] * mix
  }
  return output
}

export function floatToPcm16(input: Float32Array): Int16Array {
  const output = new Int16Array(input.length)
  for (let index = 0; index < input.length; index += 1) {
    const sample = Math.max(-1, Math.min(1, input[index]))
    output[index] = sample < 0 ? Math.round(sample * 0x8000) : Math.round(sample * 0x7fff)
  }
  return output
}

async function requestStream(deviceId: string | undefined): Promise<MediaStream> {
  return navigator.mediaDevices.getUserMedia({
    audio: {
      channelCount: 1,
      ...(deviceId ? { deviceId: { exact: deviceId } } : {})
    },
    video: false
  })
}

function isMissingSelectedDeviceError(error: unknown): boolean {
  const name = mediaErrorName(error)
  return name === 'NotFoundError' || name === 'OverconstrainedError'
}

export function mapMediaError(error: unknown): CaptureErrorCode {
  const name = mediaErrorName(error)
  if (name === 'NotAllowedError' || name === 'SecurityError') return 'permission-denied'
  if (name === 'NotFoundError' || name === 'OverconstrainedError') return 'device-missing'
  if (name === 'NotReadableError' || name === 'TrackStartError') return 'device-unavailable'
  return 'device-missing'
}

function mediaErrorName(error: unknown): string {
  if (typeof error !== 'object' || error == null || !('name' in error)) return ''
  return typeof error.name === 'string' ? error.name : ''
}
