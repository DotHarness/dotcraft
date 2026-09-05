import { describe, expect, it } from 'vitest'
import { shouldQuitAfterSatelliteJoin } from '../satellites/satelliteJoinLink'

const INVITE = 'http://192.168.1.20:47600/i/inv_1'
const JOIN_LINK = `dotcraft://satellite/join?invite=${encodeURIComponent(INVITE)}`
const WORKSPACE_LINK = 'dotcraft://workspace/open?path=%2Fworkspace%2Fsample'

describe('shouldQuitAfterSatelliteJoin', () => {
  it('quits when a protocol click launched the process only to hand over the link', () => {
    expect(shouldQuitAfterSatelliteJoin(['electron.exe', JOIN_LINK], true)).toBe(true)
  })

  it('keeps the window open when the link could not be forwarded', () => {
    expect(shouldQuitAfterSatelliteJoin(['electron.exe', JOIN_LINK], false)).toBe(false)
  })

  it('never quits a launch that carries no join link', () => {
    expect(shouldQuitAfterSatelliteJoin(['electron.exe'], true)).toBe(false)
    expect(shouldQuitAfterSatelliteJoin(['electron.exe', WORKSPACE_LINK], true)).toBe(false)
  })

  it.each([
    ['--workspace', 'C:/ws'],
    ['--remote', 'ws://127.0.0.1:9100/ws'],
    ['--no-workspace'],
    ['--tray']
  ])('keeps a launch that also asked for a window: %s', (...windowArgs) => {
    expect(shouldQuitAfterSatelliteJoin(['electron.exe', JOIN_LINK, ...windowArgs], true)).toBe(false)
  })

  it('keeps a launch that also carries a workspace deep link', () => {
    expect(shouldQuitAfterSatelliteJoin(
      ['electron.exe', JOIN_LINK, WORKSPACE_LINK],
      true
    )).toBe(false)
  })
})
