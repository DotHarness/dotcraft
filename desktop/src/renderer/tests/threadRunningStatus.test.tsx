import { renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { useThreadShowsRunning } from '../components/sidebar/threadRunningStatus'
import { useThreadStore } from '../stores/threadStore'
import type { ThreadRuntimeSnapshot, ThreadSummary } from '../types/thread'

const idle: ThreadRuntimeSnapshot = { running: false, waitingOnApproval: false, waitingOnPlanConfirmation: false }

function thread(id: string, runtime?: ThreadRuntimeSnapshot): ThreadSummary {
  return {
    id, displayName: id, status: 'active', originChannel: 'dotcraft-desktop',
    createdAt: '2026-11-20T00:00:00Z', lastActiveAt: '2026-11-20T00:00:00Z', runtime
  }
}

const showsRunning = (summary: ThreadSummary) => renderHook(() => useThreadShowsRunning(summary)).result.current

beforeEach(() => useThreadStore.getState().reset())

describe('sidebar running status', () => {
  it('counts background work for threads of another workspace', () => {
    expect(showsRunning(thread('other', { ...idle, hasBackgroundWork: true }))).toBe(true)
    expect(showsRunning(thread('idle', idle))).toBe(false)
  })

  it('uses the same rule for foreground threads from the live store', () => {
    const summary = thread('fg', idle)
    useThreadStore.getState().applyRuntimeSnapshot('fg', { ...idle, hasBackgroundWork: true }, { isDesktopOrigin: true })
    expect(showsRunning(summary)).toBe(true)
    useThreadStore.getState().applyRuntimeSnapshot('fg', idle, { isDesktopOrigin: true })
    expect(showsRunning(summary)).toBe(false)
  })

  it('shows a turn marked started before the server runtime arrives', () => {
    useThreadStore.getState().markTurnStarted('fg')
    expect(showsRunning(thread('fg', idle))).toBe(true)
  })
})
