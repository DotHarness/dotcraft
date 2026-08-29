import { describe, expect, it } from 'vitest'
import { stripRemoteDebuggingPortArgs } from '../remoteDebuggingArgs'

describe('stripRemoteDebuggingPortArgs', () => {
  it('removes equals-form remote debugging port arguments', () => {
    expect(stripRemoteDebuggingPortArgs([
      'desktop.js',
      '--remote-debugging-port=9222',
      '--workspace',
      'workspace'
    ])).toEqual(['desktop.js', '--workspace', 'workspace'])
  })

  it('removes pair-form remote debugging port arguments', () => {
    expect(stripRemoteDebuggingPortArgs([
      'desktop.js',
      '--remote-debugging-port',
      '9333',
      '--tray'
    ])).toEqual(['desktop.js', '--tray'])
  })

  it('preserves unrelated arguments', () => {
    expect(stripRemoteDebuggingPortArgs(['desktop.js', '--inspect=0', '--remote'])).toEqual([
      'desktop.js',
      '--inspect=0',
      '--remote'
    ])
  })
})
