// @vitest-environment jsdom

import { render, waitFor } from '@testing-library/react'
import { afterEach, expect, it } from 'vitest'
import { installMockApi } from '../../../../docs/demo/src/mockApi'
import { ProfileView } from '../components/settings/ProfileView'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useProfileStore } from '../stores/profileStore'
import { useUsageConnectionStore } from '../stores/usageConnectionStore'

afterEach(() => {
  useProfileStore.getState().reset()
  useUsageConnectionStore.setState({ capable: null })
})

it('loads the real Profile view through the docs demo statistics bridge', async () => {
  installMockApi()
  const view = render(<LocaleProvider loadSettings={false}><ProfileView /></LocaleProvider>)

  await waitFor(() => {
    expect(useProfileStore.getState().loadedOnce).toBe(true)
    expect(useProfileStore.getState().insightsLoadedOnce).toBe(true)
  })
  const { days, insights } = useProfileStore.getState()
  expect(days.length).toBeGreaterThan(0)
  expect(insights!.lifetimeTokens).toBeGreaterThan(days.reduce((sum, day) => sum + day.totalTokens, 0))
  expect(insights!.peakDailyTokens).toBeGreaterThanOrEqual(Math.max(...days.map((day) => day.totalTokens)))
  expect(insights!.currentStreakDays).toBeGreaterThan(0)
  expect(insights!.longestStreakDays).toBeGreaterThanOrEqual(insights!.currentStreakDays)
  view.unmount()
})
