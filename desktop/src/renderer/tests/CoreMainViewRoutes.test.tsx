import { act, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

const channelsModule = vi.hoisted(() => {
  let release!: () => void
  const ready = new Promise<void>((resolve) => {
    release = resolve
  })
  return { ready, release }
})

vi.mock('../components/channels/ChannelsView', async () => {
  await channelsModule.ready
  return { ChannelsView: () => <div data-testid="channels-route" /> }
})
vi.mock('../components/agents/AgentBuilderView', () => ({
  AgentBuilderView: () => <div data-testid="agents-route" />
}))
vi.mock('../components/automations/AutomationsView', () => ({
  AutomationsView: () => <div data-testid="automations-route" />
}))
vi.mock('../components/plugins/PluginsView', () => ({
  PluginsView: () => <div data-testid="plugins-route" />
}))
vi.mock('../components/settings/SettingsView', () => ({
  SettingsView: () => <div data-testid="settings-route" />
}))

import {
  CoreMainViewBoundary,
  coreMainViews
} from '../core/coreMainViewRoutes'

describe('Core main view routes', () => {
  it('keeps the route container mounted while a view loads', async () => {
    const ChannelsView = coreMainViews.channels
    render(
      <div data-testid="view-channels">
        <CoreMainViewBoundary>
          <ChannelsView />
        </CoreMainViewBoundary>
      </div>
    )

    expect(screen.getByTestId('view-channels')).toBeEmptyDOMElement()

    await act(async () => {
      channelsModule.release()
      await channelsModule.ready
    })

    expect(await screen.findByTestId('channels-route')).toBeInTheDocument()
  })

  it('loads Agent Builder through the Core route boundary', async () => {
    const AgentBuilderView = coreMainViews.agents
    render(
      <CoreMainViewBoundary>
        <AgentBuilderView />
      </CoreMainViewBoundary>
    )

    expect(await screen.findByTestId('agents-route')).toBeInTheDocument()
  })
})
