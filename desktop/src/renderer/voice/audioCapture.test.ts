// @vitest-environment jsdom

import { describe, expect, it, vi } from 'vitest'

import { floatToPcm16, resampleLinear, VoiceAudioCapture } from './audioCapture'

describe('voice audio conversion', () => {
  it('resamples to the requested rate while preserving duration', () => {
    const input = Float32Array.from({ length: 48_000 }, (_, index) => Math.sin(index / 10))
    const output = resampleLinear(input, 48_000, 16_000)
    expect(output).toHaveLength(16_000)
    expect(output.some((value) => value !== 0)).toBe(true)
  })

  it('clamps float samples into signed PCM16', () => {
    expect([...floatToPcm16(new Float32Array([-2, -1, 0, 1, 2]))])
      .toEqual([-32768, -32768, 0, 32767, 32767])
  })

  it('reports audio-graph startup failures as unavailable and closes the acquired stream', async () => {
    const stop = vi.fn()
    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: { getUserMedia: vi.fn().mockResolvedValue({ getTracks: () => [{ stop }] }) }
    })
    Object.defineProperty(globalThis, 'AudioContext', {
      configurable: true,
      value: class AudioContextUnavailable {
        constructor() {
          throw new DOMException('Audio graph unavailable', 'NotSupportedError')
        }
      }
    })

    await expect(VoiceAudioCapture.start(undefined, vi.fn())).rejects.toMatchObject({
      code: 'device-unavailable'
    })
    expect(stop).toHaveBeenCalledTimes(1)
  })

  it('requests only portable mono and selected-device constraints', async () => {
    const getUserMedia = vi.fn().mockRejectedValue(new DOMException('blocked', 'NotAllowedError'))
    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: { getUserMedia }
    })

    await expect(VoiceAudioCapture.start('mic-1', vi.fn())).rejects.toMatchObject({
      code: 'permission-denied'
    })
    expect(getUserMedia).toHaveBeenCalledWith({
      audio: { channelCount: 1, deviceId: { exact: 'mic-1' } },
      video: false
    })
  })

  it('captures PCM through the compatibility processor when AudioWorklet cannot start', async () => {
    const stop = vi.fn()
    const disconnectSource = vi.fn()
    const disconnectProcessor = vi.fn()
    const disconnectGain = vi.fn()
    const gain = {
      gain: { value: 1 },
      connect: vi.fn(),
      disconnect: disconnectGain
    }
    const processor = {
      onaudioprocess: null as ((event: { inputBuffer: { getChannelData(channel: number): Float32Array } }) => void) | null,
      connect: vi.fn(() => gain),
      disconnect: disconnectProcessor
    }
    const source = {
      connect: vi.fn(() => processor),
      disconnect: disconnectSource
    }
    const close = vi.fn().mockResolvedValue(undefined)
    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: { getUserMedia: vi.fn().mockResolvedValue({ getTracks: () => [{ stop }] }) }
    })
    Object.defineProperty(URL, 'createObjectURL', {
      configurable: true,
      value: vi.fn(() => 'blob:voice-worklet')
    })
    Object.defineProperty(URL, 'revokeObjectURL', {
      configurable: true,
      value: vi.fn()
    })
    Object.defineProperty(globalThis, 'AudioContext', {
      configurable: true,
      value: class AudioContextWithFallback {
        readonly sampleRate = 48_000
        readonly state = 'running'
        readonly destination = {}
        readonly audioWorklet = { addModule: vi.fn().mockRejectedValue(new DOMException('blocked', 'SecurityError')) }
        createMediaStreamSource = vi.fn(() => source)
        createGain = vi.fn(() => gain)
        createScriptProcessor = vi.fn(() => processor)
        close = close
      }
    })

    const capture = await VoiceAudioCapture.start(undefined, vi.fn())
    processor.onaudioprocess?.({
      inputBuffer: { getChannelData: () => new Float32Array(2_048).fill(0.25) }
    })
    const audio = await capture.stop()

    expect(audio.pcm16.byteLength).toBeGreaterThan(0)
    expect(stop).toHaveBeenCalledTimes(1)
    expect(disconnectSource).toHaveBeenCalledTimes(1)
    expect(disconnectProcessor).toHaveBeenCalledTimes(1)
    expect(disconnectGain).toHaveBeenCalledTimes(1)
    expect(close).toHaveBeenCalledTimes(1)
  })
})
