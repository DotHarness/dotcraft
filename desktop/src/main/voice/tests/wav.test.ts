import { describe, expect, it } from 'vitest'

import { VOICE_SAMPLE_RATE } from '../../../shared/voice'
import { encodeMonoPcm16Wav } from '../wav'

describe('encodeMonoPcm16Wav', () => {
  it('writes a mono 16 kHz PCM header and preserves the samples', () => {
    const pcm = new Uint8Array([1, 2, 3, 4])
    const wav = encodeMonoPcm16Wav(pcm)
    const view = new DataView(wav.buffer)

    expect(new TextDecoder().decode(wav.slice(0, 4))).toBe('RIFF')
    expect(new TextDecoder().decode(wav.slice(8, 12))).toBe('WAVE')
    expect(view.getUint16(22, true)).toBe(1)
    expect(view.getUint32(24, true)).toBe(VOICE_SAMPLE_RATE)
    expect(view.getUint16(34, true)).toBe(16)
    expect([...wav.slice(44)]).toEqual([...pcm])
  })

  it('rejects empty and odd-length PCM', () => {
    expect(() => encodeMonoPcm16Wav(new Uint8Array())).toThrow('invalid-audio')
    expect(() => encodeMonoPcm16Wav(new Uint8Array([1]))).toThrow('invalid-audio')
  })
})
