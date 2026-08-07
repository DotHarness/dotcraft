import { writeFile } from 'fs/promises'

import { VOICE_SAMPLE_RATE } from '../../shared/voice'

export function encodeMonoPcm16Wav(pcm: Uint8Array): Uint8Array {
  if (pcm.byteLength === 0 || pcm.byteLength % 2 !== 0) {
    throw new Error('invalid-audio')
  }

  const headerSize = 44
  const wav = new Uint8Array(headerSize + pcm.byteLength)
  const view = new DataView(wav.buffer)
  writeAscii(wav, 0, 'RIFF')
  view.setUint32(4, 36 + pcm.byteLength, true)
  writeAscii(wav, 8, 'WAVE')
  writeAscii(wav, 12, 'fmt ')
  view.setUint32(16, 16, true)
  view.setUint16(20, 1, true)
  view.setUint16(22, 1, true)
  view.setUint32(24, VOICE_SAMPLE_RATE, true)
  view.setUint32(28, VOICE_SAMPLE_RATE * 2, true)
  view.setUint16(32, 2, true)
  view.setUint16(34, 16, true)
  writeAscii(wav, 36, 'data')
  view.setUint32(40, pcm.byteLength, true)
  wav.set(pcm, headerSize)
  return wav
}

export async function writeMonoPcm16Wav(path: string, pcm: Uint8Array): Promise<void> {
  await writeFile(path, encodeMonoPcm16Wav(pcm), { flag: 'wx' })
}

function writeAscii(target: Uint8Array, offset: number, value: string): void {
  for (let index = 0; index < value.length; index += 1) {
    target[offset + index] = value.charCodeAt(index)
  }
}
