import { StrictMode } from 'react'
import { act, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import type { DesktopPluginHost } from '@dotcraft/plugin'
import { normalizeFeatureGuides, spaceFeatureGuideId } from '../../shared/featureGuides'
import { SpaceFeatureGuide } from '../components/featureGuides/SpaceFeatureGuide'
import { LocaleProvider } from '../contexts/LocaleContext'
import { validateActivation } from '../plugins/desktopPluginValidation'
import { useConversationStore } from '../stores/conversationStore'
import { useFeatureGuideStore } from '../stores/featureGuideStore'
import { startSpaceTracking } from '../stores/spaceStore'
import { publishDesktopPluginGeneration, withdrawDesktopPluginGeneration } from '../plugins/desktopPluginRegistry'
import { useUIStore } from '../stores/uiStore'
import { installDesktopApiMock } from './desktopApiMock'

const host = {} as DesktopPluginHost
const label = { default: 'Space' }
const component = (): null => null
const generation = validateActivation('plugin', '1.0', 'r1', host, {
  mainViews: [{ id: 'board', label, component }],
  spaces: [{ id: 'space', views: ['board'], label, sidebar: component, open: vi.fn(), guide: { title: label, body: label, action: label, media: component } }]
})
const [space] = generation.spaces
const id = spaceFeatureGuideId(space.spaceKey)

afterEach(() => {
  useFeatureGuideStore.setState(useFeatureGuideStore.getInitialState())
  withdrawDesktopPluginGeneration('plugin')
  useUIStore.setState({ sidebarCollapsed: false, activeMainView: 'conversation' })
  useConversationStore.setState({ turnStatus: 'idle' })
})

it('remembers entering a linked space view even before guide settings load', async () => {
  const saved = vi.fn().mockResolvedValue({})
  installDesktopApiMock({ settings: { get: async () => ({ featureGuides: normalizeFeatureGuides({}) }), set: saved } })
  publishDesktopPluginGeneration(generation)
  useUIStore.getState().setActiveMainView(space.viewKeys[0])
  const stop = startSpaceTracking()
  stop()
  await useFeatureGuideStore.getState().load()

  expect(saved).toHaveBeenCalledWith({
    featureGuides: expect.objectContaining({ guides: { [id]: { shown: 0, settled: true } } })
  })
})

it('waits for a visible idle sidebar, then releases the shared guide slot when its row disappears', async () => {
  installDesktopApiMock({ initialLocale: 'en', settings: { set: vi.fn().mockResolvedValue({}) } })
  useFeatureGuideStore.setState({ settings: normalizeFeatureGuides({}) })
  useFeatureGuideStore.getState().requestSpaceGuide(space.spaceKey, host)
  useConversationStore.setState({ turnStatus: 'running' })
  useUIStore.setState({ sidebarCollapsed: true })
  const view = render(<StrictMode><LocaleProvider loadSettings={false}>
    <SpaceFeatureGuide space={space} anchorName="--fixture" />
  </LocaleProvider></StrictMode>)

  await act(async () => useConversationStore.setState({ turnStatus: 'idle' }))
  expect(screen.queryByRole('dialog')).toBeNull()
  expect(useFeatureGuideStore.getState().settings?.guides[id]).toBeUndefined()
  await act(async () => useUIStore.setState({ sidebarCollapsed: false }))
  expect(screen.getByRole('dialog')).toBeInTheDocument()
  view.unmount()

  const state = useFeatureGuideStore.getState()
  expect(state.active).toBeNull()
  expect(state.settings?.guides[id]).toMatchObject({ shown: 1, lastClosedAt: expect.any(Number) })
})
