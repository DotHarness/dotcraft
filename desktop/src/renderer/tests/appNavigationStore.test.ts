import { beforeEach, describe, expect, it } from 'vitest'

import {
  runWithoutAppNavigationRecording,
  startAppNavigationHistory,
  stopAppNavigationHistory,
  useAppNavigationStore,
  type AppNavigationLocation
} from '../stores/appNavigationStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { usePluginStore } from '../stores/pluginStore'

const conversationLocation: AppNavigationLocation = {
  kind: 'conversation',
  threadId: null,
  detailVisible: false,
  activeDetailTab: { kind: 'launcher' },
  selectedChangedFile: null
}

beforeEach(() => {
  stopAppNavigationHistory()
  useThreadStore.getState().reset()
  usePluginStore.setState({ plugins: [] })
  useUIStore.setState({
    activeMainView: 'conversation',
    activeSettingsTab: 'general',
    selectedChannelKey: null,
    activeDetailTab: { kind: 'launcher' },
    detailPanelPreferredVisible: false,
    selectedChangedFile: null
  })
  useAppNavigationStore.getState().reset()
})

describe('app navigation history', () => {
  it('moves backward and forward and truncates a replaced branch', () => {
    const history = useAppNavigationStore.getState()
    history.push(conversationLocation)
    history.push({ kind: 'settings', tab: 'appearance' })
    history.push({ kind: 'channels', selection: null })

    history.goBack()
    expect(useAppNavigationStore.getState()).toMatchObject({
      index: 1,
      canGoBack: true,
      canGoForward: true
    })
    expect(useUIStore.getState()).toMatchObject({
      activeMainView: 'settings',
      activeSettingsTab: 'appearance'
    })

    history.goForward()
    expect(useAppNavigationStore.getState().index).toBe(2)
    expect(useUIStore.getState().activeMainView).toBe('channels')

    history.goBack()
    useAppNavigationStore.getState().push({ kind: 'desktopPlugin', view: 'desktop-plugin:test:replacement' })
    expect(useAppNavigationStore.getState()).toMatchObject({
      index: 2,
      canGoForward: false
    })
    expect(useAppNavigationStore.getState().entries).toHaveLength(3)
  })

  it('deduplicates locations and retains only the newest 100 entries', () => {
    const history = useAppNavigationStore.getState()
    history.push(conversationLocation)
    history.push(conversationLocation)
    expect(useAppNavigationStore.getState().entries).toHaveLength(1)

    for (let index = 0; index < 105; index += 1) {
      history.push({ kind: 'desktopPlugin', view: `desktop-plugin:test:${index}` })
    }

    const state = useAppNavigationStore.getState()
    expect(state.entries).toHaveLength(100)
    expect(state.index).toBe(99)
    expect(state.entries[0]).toEqual({ kind: 'desktopPlugin', view: 'desktop-plugin:test:5' })
  })

  it('batches a multi-store navigation and ignores suppressed state repair', async () => {
    const cleanup = startAppNavigationHistory('fixture-workspace')

    useUIStore.getState().setActiveSettingsTab('appearance')
    useUIStore.getState().setActiveMainView('settings')
    await Promise.resolve()

    expect(useAppNavigationStore.getState().entries).toHaveLength(2)
    expect(useAppNavigationStore.getState().entries[1]).toEqual({
      kind: 'settings',
      tab: 'appearance'
    })

    runWithoutAppNavigationRecording(() => {
      useUIStore.getState().setActiveSettingsTab('general')
    })
    await Promise.resolve()
    expect(useAppNavigationStore.getState().entries).toHaveLength(2)

    cleanup()
  })

  it('skips a missing thread while moving through history', () => {
    const history = useAppNavigationStore.getState()
    history.push({ kind: 'settings', tab: 'general' })
    history.push({ ...conversationLocation, threadId: 'missing-thread' })
    history.push({ kind: 'channels', selection: null })

    history.goBack()

    expect(useAppNavigationStore.getState().index).toBe(0)
    expect(useUIStore.getState().activeMainView).toBe('settings')
  })

  it('restores the built-in Agents view', () => {
    const history = useAppNavigationStore.getState()
    history.push({ kind: 'settings', tab: 'general' })
    history.push({ kind: 'agents' })
    history.push({ kind: 'channels', selection: null })

    history.goBack()
    expect(useUIStore.getState().activeMainView).toBe('agents')
  })
})
