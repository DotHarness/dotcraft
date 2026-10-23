import { EventEmitter } from 'events'
import { describe, expect, it, vi } from 'vitest'
import { installStandardIoErrorGuard } from '../standardIoGuards'

describe('standard I/O guards', () => {
  it('swallows broken EPIPE and EIO streams but not unrelated errors', () => {
    for (const code of ['EPIPE', 'EIO']) {
      const stream = new EventEmitter()
      const onBrokenPipe = vi.fn()
      const dispose = installStandardIoErrorGuard(stream, onBrokenPipe)

      expect(() => stream.emit('error', Object.assign(new Error(`write ${code}`), { code }))).not.toThrow()
      expect(onBrokenPipe).toHaveBeenCalledOnce()
      dispose()
    }

    const stream = new EventEmitter()
    installStandardIoErrorGuard(stream)
    const error = Object.assign(new Error('write failed'), { code: 'EACCES' })
    expect(() => stream.emit('error', error)).toThrow(error)
  })
})
