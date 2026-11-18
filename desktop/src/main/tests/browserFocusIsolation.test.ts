import { EventEmitter } from 'node:events'
import { expect, it, vi } from 'vitest'

vi.mock('electron', () => ({ app: {} }))
import { BrowserFocusIsolation } from '../browserFocusIsolation'

function fixture() {
  const hook = { setIsolation: vi.fn(() => true), unregister: vi.fn(() => true) }
  const isolation = new BrowserFocusIsolation(() => hook)
  const page = Object.assign(new EventEmitter(), { mainFrame: { processId: 1, routingId: 2 } })
  return { hook, isolation, page, wc: page as unknown as Electron.WebContents }
}

it('rearms input and cleans the latest frame identity after navigation and destruction', () => {
  const { hook, isolation, page, wc } = fixture()
  isolation.arm(wc)
  page.mainFrame = { processId: 3, routingId: 4 }
  isolation.arm(wc)
  expect(hook.setIsolation.mock.calls).toEqual([[1, 2, true], [3, 4, true]])
  expect(page.listenerCount('destroyed')).toBe(1)
  Object.defineProperty(page, 'mainFrame', { get: () => { throw new Error('Destroyed') } })
  page.emit('destroyed')
  expect(hook.unregister).toHaveBeenCalledExactlyOnceWith(3, 4)
})

it('releases automation without accumulating cleanup listeners on later input', () => {
  const { hook, isolation, page, wc } = fixture()
  isolation.arm(wc)
  isolation.release(wc)
  isolation.release(wc)
  expect(hook.unregister).toHaveBeenCalledOnce()
  isolation.arm(wc)
  expect(page.listenerCount('destroyed')).toBe(1)
  page.emit('destroyed')
  expect(hook.unregister).toHaveBeenCalledTimes(2)
})

it('rejects input when loading or registering the native hook fails', () => {
  const { wc } = fixture()
  for (const load of [
    () => { throw new Error('Missing native module') },
    () => ({ setIsolation: () => { throw new Error('Unsupported runtime') }, unregister: () => true })
  ]) {
    expect(() => new BrowserFocusIsolation(load).arm(wc)).toThrow('IabBackendUnavailable')
  }
})
