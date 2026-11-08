// @vitest-environment jsdom

import { afterEach, expect, it, vi } from 'vitest'
import { useProfileStore, type ProfileInsightsWire } from '../stores/profileStore'
import { useUsageConnectionStore } from '../stores/usageConnectionStore'
import { installDesktopApiMock } from './desktopApiMock'

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => { resolve = done })
  return { promise, resolve }
}

const insights: ProfileInsightsWire = {
  lifetimeTokens: 800_000, peakDailyTokens: 100_000, currentStreakDays: 2, longestStreakDays: 400,
  topModel: null, topReasoning: null, skillsExplored: 1, totalSkillsUsed: 4,
  totalThreads: 3, longestTaskMs: 20_000, skills: []
}

afterEach(() => {
  useProfileStore.getState().reset()
  useUsageConnectionStore.setState({ capable: null })
})

it('does not let a superseded history or insights response replace a newer result', async () => {
  const oldHistory = deferred<unknown>()
  const oldInsights = deferred<unknown>()
  let histories = 0
  let summaries = 0
  installDesktopApiMock({ appServer: {
    getUsageConnection: async () => ({ connectionKey: 'local-chats', usageTelemetry: true }),
    sendConnectionRequest: vi.fn((_key: string, method: string) => {
      if (method === 'profile/insights') return ++summaries === 1 ? oldInsights.promise : Promise.resolve(insights)
      return ++histories === 1 ? oldHistory.promise : Promise.resolve({ days: [{ date: '2026-11-08', total: 200 }] })
    })
  } })
  const first = useProfileStore.getState().load()
  await vi.waitFor(() => expect(summaries).toBe(1))
  await useProfileStore.getState().load()
  oldHistory.resolve({ days: [{ date: '2026-11-07', total: 1 }] })
  oldInsights.resolve({ ...insights, lifetimeTokens: 1 })
  await first

  expect(useProfileStore.getState().days).toEqual([{ date: '2026-11-08', totalTokens: 200 }])
  expect(useProfileStore.getState().insights?.lifetimeTokens).toBe(800_000)
})
