import { describe, expect, it } from 'vitest'

import { formatMicrophoneLabel } from './microphoneLabels'

describe('formatMicrophoneLabel', () => {
  it('removes only a trailing Windows USB VID:PID suffix', () => {
    expect(formatMicrophoneLabel('Microphone (Razer Kraken V3 X) (1532:0537)'))
      .toBe('Microphone (Razer Kraken V3 X)')
    expect(formatMicrophoneLabel('Microphone (Razer Kraken V3 X)')).toBe('Microphone (Razer Kraken V3 X)')
    expect(formatMicrophoneLabel('Studio microphone (input 2)')).toBe('Studio microphone (input 2)')
  })
})
