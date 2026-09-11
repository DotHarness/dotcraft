import { describe, expect, it } from 'vitest'
import {
  screenViewStateLabelKey,
  screenViewSurfaceKind
} from '../components/conversation/screenView/screenViewLabels'

describe('screenViewStateLabelKey', () => {
  it('separates a paused view that needs the machine owner', () => {
    expect(screenViewStateLabelKey({ kind: 'paused' })).toBe('screenView.state.paused')
    expect(screenViewStateLabelKey({ kind: 'paused', needsAuthorization: true })).toBe(
      'screenView.state.needsAuthorization'
    )
  })

  it('names the capture reason, and falls back when there is none', () => {
    expect(screenViewStateLabelKey({ kind: 'unavailable', reason: 'noDisplayServer' })).toBe(
      'screenView.state.noDisplayServer'
    )
    expect(screenViewStateLabelKey({ kind: 'unavailable' })).toBe('screenView.state.unavailable')
  })
})

describe('screenViewSurfaceKind', () => {
  it('groups the states by what the picture shows', () => {
    expect(screenViewSurfaceKind({ kind: 'live' })).toBe('live')
    expect(screenViewSurfaceKind({ kind: 'connecting' })).toBe('connecting')
    expect(screenViewSurfaceKind({ kind: 'stalled' })).toBe('waiting')
    expect(screenViewSurfaceKind({ kind: 'reconnecting' })).toBe('waiting')
    expect(screenViewSurfaceKind({ kind: 'paused' })).toBe('idle')
    expect(screenViewSurfaceKind({ kind: 'offline' })).toBe('idle')
    expect(screenViewSurfaceKind({ kind: 'unavailable', reason: 'captureFailed' })).toBe('idle')
  })
})
