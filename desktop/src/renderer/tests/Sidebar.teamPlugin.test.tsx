import './setupPluginRuntime'
import type { DesktopPluginHost } from '@dotcraft/plugin'
import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { Sidebar } from '../components/layout/Sidebar'
import { LocaleProvider } from '../contexts/LocaleContext'
import {
  buildDesktopPluginMainViewKey,
  clearDesktopPluginRegistry,
  publishDesktopPluginGeneration,
  withdrawDesktopPluginGeneration
} from '../plugins/desktopPluginRegistry'
import { useConnectionStore } from '../stores/connectionStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { installDesktopApiMock } from './desktopApiMock'

const settingsGet = vi.fn()
const pluginId = 'agent-teams'
const revision = 'a'.repeat(64)
const contributionId = 'teams'

function publishTeamView(): void {
  const host = {} as DesktopPluginHost
  publishDesktopPluginGeneration({
    pluginId,
    version: '0.1.0',
    revision,
    mainViews: [{
      pluginId,
      revision,
      id: contributionId,
      label: {
        default: 'Team',
        translations: {
          'zh-Hans': '团队',
          ja: 'チーム',
          ko: '팀',
          es: 'Equipo',
          fr: 'Équipe',
          de: 'Team'
        }
      },
      order: 40,
      component: () => null,
      viewKey: buildDesktopPluginMainViewKey(pluginId, contributionId),
      host
    }],
    settingsPages: [],
    conversationViews: [],
    commands: [],
    toolRenderers: [],
    messageActions: []
  })
}

function renderSidebar(): void {
  render(
    <LocaleProvider>
      <Sidebar workspaceName="dotcraft" workspacePath="X:\\fixtures\\workspace" />
    </LocaleProvider>
  )
}

describe('Sidebar Desktop Plugin contributions', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    clearDesktopPluginRegistry()
    settingsGet.mockResolvedValue({ locale: 'en' })
    installDesktopApiMock({
      settings: { get: settingsGet },
      workspace: {
        getRecent: vi.fn().mockResolvedValue([]),
        clearSelection: vi.fn().mockResolvedValue(undefined),
        switch: vi.fn().mockResolvedValue(undefined),
        clearRecent: vi.fn().mockResolvedValue(undefined)
      },
      shell: { openPath: vi.fn().mockResolvedValue(undefined) }
    })

    useConnectionStore.getState().reset()
    useConnectionStore.getState().setStatus({
      status: 'connected',
      capabilities: { pluginManagement: true }
    })
    useThreadStore.getState().reset()
    useUIStore.setState({
      activeMainView: 'conversation',
      sidebarCollapsed: false,
      sidebarPreferredCollapsed: false
    })
  })

  it('hides a contribution until its generation is active', () => {
    renderSidebar()
    expect(screen.queryByRole('button', { name: 'Team' })).not.toBeInTheDocument()
  })

  it('shows an active main-view contribution', () => {
    publishTeamView()
    renderSidebar()
    expect(screen.getByRole('button', { name: 'Team' })).toBeInTheDocument()
  })

  it('uses the contribution translation for the active locale', async () => {
    settingsGet.mockResolvedValue({ locale: 'zh-Hans' })
    publishTeamView()
    renderSidebar()
    expect(await screen.findByRole('button', { name: '团队' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Team' })).not.toBeInTheDocument()
  })

  it('resolves another supported locale from the same label', async () => {
    settingsGet.mockResolvedValue({ locale: 'ja' })
    publishTeamView()
    renderSidebar()
    expect(await screen.findByRole('button', { name: 'チーム' })).toBeInTheDocument()
  })

  it('hides the contribution after its generation is withdrawn', () => {
    publishTeamView()
    withdrawDesktopPluginGeneration(pluginId)
    useUIStore.setState({ sidebarCollapsed: true, sidebarPreferredCollapsed: true })
    renderSidebar()
    expect(screen.queryByRole('button', { name: 'Team' })).not.toBeInTheDocument()
  })
})
