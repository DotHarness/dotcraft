import { EventEmitter } from 'events'
import { describe, expect, it, vi } from 'vitest'
import { installStandardIoErrorGuard } from '../standardIoGuards'

describe('standard I/O guards', () => {
  it.each(['EPIPE', 'EIO'])('handles a broken %s stream without throwing', (code) => {
    const stream = new EventEmitter()
    const onBrokenPipe = vi.fn()
    const dispose = installStandardIoErrorGuard(stream, onBrokenPipe)
    const error = Object.assign(new Error(`write ${code}`), { code })

    expect(() => stream.emit('error', error)).not.toThrow()
    expect(onBrokenPipe).toHaveBeenCalledOnce()

    dispose()
  })

  it('does not hide unrelated stream errors', () => {
    const stream = new EventEmitter()
    installStandardIoErrorGuard(stream)
    const error = Object.assign(new Error('write failed'), { code: 'EACCES' })

    expect(() => stream.emit('error', error)).toThrow(error)
  })
})
