import { getRendererTestState, setRendererTestState } from './panelTestState'
import { beforeEach, describe, expect, it } from 'vitest'

import {
  runWithoutAppNavigationRecording,
  startAppNavigationHistory,
  stopAppNavigationHistory,
  useAppNavigationStore,
  type AppNavigationLocation
} from '../stores/appNavigationStore'
import { useThreadStore } from '../stores/threadStore'
import { usePluginStore } from '../stores/pluginStore'

const conversationLocation: AppNavigationLocation = {
  kind: 'conversation',
  threadId: null,
  detailVisible: false,
  activeDetailTab: { kind: 'launcher' },
  selectedChangeKey: null
}

beforeEach(() => {
  stopAppNavigationHistory()
  useThreadStore.getState().reset()
  usePluginStore.setState({ plugins: [] })
  setRendererTestState({
    activeMainView: 'conversation',
    activeSettingsTab: 'general',
    selectedChannelKey: null,
    activeDetailTab: { kind: 'launcher' },
    detailPanelPreferredVisible: false,
    selectedChangeKey: null
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
    expect(getRendererTestState()).toMatchObject({
      activeMainView: 'settings',
      activeSettingsTab: 'appearance'
    })

    history.goForward()
    expect(useAppNavigationStore.getState().index).toBe(2)
    expect(getRendererTestState().activeMainView).toBe('channels')

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

    getRendererTestState().setActiveSettingsTab('appearance')
    getRendererTestState().setActiveMainView('settings')
    await Promise.resolve()

    expect(useAppNavigationStore.getState().entries).toHaveLength(2)
    expect(useAppNavigationStore.getState().entries[1]).toEqual({
      kind: 'settings',
      tab: 'appearance'
    })

    runWithoutAppNavigationRecording(() => {
      getRendererTestState().setActiveSettingsTab('general')
    })
    await Promise.resolve()
    expect(useAppNavigationStore.getState().entries).toHaveLength(2)

    cleanup()
  })

  it('skips a missing thread and restores the built-in Agents view while moving through history', () => {
    const history = useAppNavigationStore.getState()
    history.push({ kind: 'settings', tab: 'general' })
    history.push({ ...conversationLocation, threadId: 'missing-thread' })
    history.push({ kind: 'channels', selection: null })

    history.goBack()

    expect(useAppNavigationStore.getState().index).toBe(0)
    expect(getRendererTestState().activeMainView).toBe('settings')

    history.push({ kind: 'agents' })
    history.push({ kind: 'channels', selection: null })
    history.goBack()
    expect(getRendererTestState().activeMainView).toBe('agents')
  })
})
