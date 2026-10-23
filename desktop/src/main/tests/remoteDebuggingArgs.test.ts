import { describe, expect, it } from 'vitest'
import { stripRemoteDebuggingPortArgs } from '../remoteDebuggingArgs'

describe('stripRemoteDebuggingPortArgs', () => {
  it('removes equals-form and pair-form remote debugging ports and keeps unrelated arguments', () => {
    expect(stripRemoteDebuggingPortArgs([
      'desktop.js',
      '--remote-debugging-port=9222',
      '--inspect=0',
      '--remote-debugging-port',
      '9333',
      '--remote',
      '--tray'
    ])).toEqual(['desktop.js', '--inspect=0', '--remote', '--tray'])
  })
})
