import { describe, expect, it } from 'vitest'
import { shouldQuitAfterSatelliteJoin } from '../satellites/satelliteJoinLink'

const INVITE = 'http://192.168.1.20:47600/i/inv_1'
const JOIN_LINK = `dotcraft://satellite/join?invite=${encodeURIComponent(INVITE)}`
const WORKSPACE_LINK = 'dotcraft://workspace/open?path=%2Fworkspace%2Fsample'

describe('shouldQuitAfterSatelliteJoin', () => {
  it('quits only when a join-only launch handed over its link', () => {
    expect(shouldQuitAfterSatelliteJoin(['electron.exe', JOIN_LINK], true)).toBe(true)
    expect(shouldQuitAfterSatelliteJoin(['electron.exe', JOIN_LINK], false)).toBe(false)
    expect(shouldQuitAfterSatelliteJoin(['electron.exe'], true)).toBe(false)
    expect(shouldQuitAfterSatelliteJoin(['electron.exe', WORKSPACE_LINK], true)).toBe(false)
  })

  it('keeps a launch that also asked for a window or a workspace', () => {
    for (const windowArgs of [
      ['--workspace', 'C:/ws'],
      ['--remote', 'ws://127.0.0.1:9100/ws'],
      ['--no-workspace'],
      ['--tray'],
      [WORKSPACE_LINK]
    ]) {
      expect(shouldQuitAfterSatelliteJoin(['electron.exe', JOIN_LINK, ...windowArgs], true), windowArgs.join(' ')).toBe(false)
    }
  })
})
