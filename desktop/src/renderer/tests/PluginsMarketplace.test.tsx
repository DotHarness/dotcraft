import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { PluginsView } from '../components/plugins/PluginsView'
import { looksLikeLocalPath, parseSparsePaths } from '../components/plugins/AddMarketplaceDialog'
import { useConnectionStore } from '../stores/connectionStore'
import { useAppBindingStore } from '../stores/appBindingStore'
import { usePluginStore, type MarketplaceEntry, type PluginEntry } from '../stores/pluginStore'
import { useSkillsStore, type SkillEntry } from '../stores/skillsStore'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'

const appServerSendRequest = vi.fn()
const settingsGet = vi.fn()
const workspacePickFolder = vi.fn()
const confirmDialog = vi.fn()

const marketplacePlugin: PluginEntry = {
  id: 'example-plugin',
  displayName: 'Example Plugin',
  description: 'An example marketplace plugin',
  enabled: false,
  installed: false,
  installable: true,
  removable: false,
  source: 'builtin',
  rootPath: '',
  marketplaceName: 'example-marketplace',
  interface: {
    displayName: 'Example Plugin',
    shortDescription: 'An example marketplace plugin',
    developerName: 'Example Labs'
  },
  functions: [],
  skills: [],
  mcpServers: [],
  lspServers: []
}

const marketplace: MarketplaceEntry = {
  name: 'example-marketplace',
  displayName: 'Example Plugins',
  sourceType: 'git',
  source: 'https://example.com/team/plugins.git',
  ref: 'main',
  sparsePaths: [],
  root: '/home/user/.craft/marketplaces/example-marketplace',
  removable: true,
  pluginIds: ['example-plugin']
}

const pluginCreatorSkill: SkillEntry = {
  name: 'plugin-creator',
  description: 'Scaffold DotCraft local plugins',
  source: 'builtin',
  enabled: true
} as SkillEntry

function renderPluginsView(): void {
  render(
    <LocaleProvider>
      <PluginsView />
    </LocaleProvider>
  )
}

function catalogResponse(overrides?: {
  plugins?: PluginEntry[]
  marketplaces?: MarketplaceEntry[]
}): unknown {
  return {
    plugins: overrides?.plugins ?? [marketplacePlugin],
    marketplaces: overrides?.marketplaces ?? [marketplace],
    diagnostics: []
  }
}

describe('plugin marketplace surface', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    useConnectionStore.getState().reset()
    useAppBindingStore.getState().reset()
    useConversationStore.setState({ remoteWorkspaceActive: false })
    useThreadStore.getState().reset()
    useConnectionStore.getState().setStatus({
      status: 'connected',
      capabilities: { pluginManagement: true, pluginMarketplaces: true }
    })
    usePluginStore.setState({
      plugins: [],
      marketplaces: [],
      diagnostics: [],
      loading: false,
      error: null,
      selectedPluginId: null,
      selectedPlugin: null,
      detailLoading: false
    })
    useSkillsStore.setState({
      skills: [],
      loading: false,
      error: null,
      selectedSkillName: null,
      skillContent: null,
      contentLoading: false
    })
    useUIStore.setState({ welcomeDraft: null, activeMainView: 'conversation' })
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet },
        appServer: { sendRequest: appServerSendRequest },
        shell: { openExternal: vi.fn(), getProtocolHandlerName: vi.fn().mockResolvedValue('') },
        workspace: { pickFolder: workspacePickFolder }
      }
    })
    workspacePickFolder.mockResolvedValue(null)
    ;(window as Window & { __confirmDialog?: unknown }).__confirmDialog = confirmDialog
    confirmDialog.mockResolvedValue(true)
  })

  it('groups catalog entries under their marketplace', async () => {
    appServerSendRequest.mockResolvedValue(catalogResponse())

    renderPluginsView()

    expect(await screen.findByRole('heading', { name: 'Example Plugins' })).toBeInTheDocument()
    expect(screen.getByText('https://example.com/team/plugins.git')).toBeInTheDocument()
    expect(screen.getByText('Example Plugin')).toBeInTheDocument()
  })

  it('stages a plugin authoring draft with the creator skill mention', async () => {
    appServerSendRequest.mockImplementation((method: string) => {
      if (method === 'skills/list') return Promise.resolve({ skills: [pluginCreatorSkill] })
      return Promise.resolve(catalogResponse())
    })

    renderPluginsView()
    fireEvent.click(await screen.findByRole('button', { name: 'Create' }))

    await waitFor(() => {
      expect(useUIStore.getState().welcomeDraft?.text).toBe('$plugin-creator help me create a plugin')
    })
    expect(useUIStore.getState().welcomeDraft?.segments).toEqual([
      { type: 'skill', skillName: 'plugin-creator' }
    ])
  })

  it('stages plain text when the creator skill is unavailable', async () => {
    appServerSendRequest.mockImplementation((method: string) => {
      if (method === 'skills/list') return Promise.resolve({ skills: [] })
      return Promise.resolve(catalogResponse())
    })

    renderPluginsView()
    fireEvent.click(await screen.findByRole('button', { name: 'Create' }))

    await waitFor(() => {
      expect(useUIStore.getState().welcomeDraft?.text).toBe('help me create a plugin')
    })
    expect(useUIStore.getState().welcomeDraft?.segments).toEqual([])
  })

  it('adds a marketplace with the reference and sparse paths from the dialog', async () => {
    appServerSendRequest.mockImplementation((method: string) => {
      if (method === 'marketplace/add') {
        return Promise.resolve({ marketplace, alreadyAdded: false })
      }
      return Promise.resolve(catalogResponse())
    })

    renderPluginsView()
    await screen.findByRole('heading', { name: 'Example Plugins' })
    fireEvent.click(screen.getByRole('button', { name: 'More create options' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Add marketplace' }))

    fireEvent.change(await screen.findByLabelText('Source'), { target: { value: 'owner/repo' } })
    fireEvent.change(screen.getByLabelText('Git ref'), { target: { value: 'release' } })
    fireEvent.change(screen.getByLabelText('Sparse paths'), {
      target: { value: 'plugins/example\n\n plugins/other ' }
    })
    fireEvent.click(screen.getByRole('button', { name: 'Add marketplace' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('marketplace/add', {
        source: 'owner/repo',
        ref: 'release',
        sparsePaths: ['plugins/example', 'plugins/other']
      })
    })
  })

  it('omits repository-only fields for a local directory source', async () => {
    appServerSendRequest.mockImplementation((method: string) => {
      if (method === 'marketplace/add') {
        return Promise.resolve({ marketplace: { ...marketplace, sourceType: 'local' }, alreadyAdded: false })
      }
      return Promise.resolve(catalogResponse())
    })
    workspacePickFolder.mockResolvedValue('/home/user/plugins')

    renderPluginsView()
    await screen.findByRole('heading', { name: 'Example Plugins' })
    fireEvent.click(screen.getByRole('button', { name: 'More create options' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Add marketplace' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Browse' }))

    await waitFor(() => {
      expect(screen.getByLabelText('Git ref')).toBeDisabled()
    })
    expect(screen.getByLabelText('Sparse paths')).toBeDisabled()

    fireEvent.click(screen.getByRole('button', { name: 'Add marketplace' }))
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('marketplace/add', { source: '/home/user/plugins' })
    })
  })

  it('reports an add failure inline and keeps the dialog open', async () => {
    appServerSendRequest.mockImplementation((method: string) => {
      if (method === 'marketplace/add') {
        return Promise.reject(new Error('The requested reference does not exist.'))
      }
      return Promise.resolve(catalogResponse())
    })

    renderPluginsView()
    await screen.findByRole('heading', { name: 'Example Plugins' })
    fireEvent.click(screen.getByRole('button', { name: 'More create options' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Add marketplace' }))
    fireEvent.change(await screen.findByLabelText('Source'), { target: { value: 'owner/repo' } })
    fireEvent.click(screen.getByRole('button', { name: 'Add marketplace' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('The requested reference does not exist.')
    expect(screen.getByLabelText('Source')).toBeInTheDocument()
  })

  it('removes a marketplace after confirmation', async () => {
    appServerSendRequest.mockImplementation((method: string) => {
      if (method === 'marketplace/remove') return Promise.resolve({ name: 'example-marketplace' })
      return Promise.resolve(catalogResponse())
    })

    renderPluginsView()
    await screen.findByRole('heading', { name: 'Example Plugins' })
    fireEvent.click(screen.getByRole('button', { name: 'Marketplace actions' }))
    fireEvent.click(await screen.findByText('Remove'))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('marketplace/remove', { name: 'example-marketplace' })
    })
  })

  it('hides marketplace commands when the server does not support them', async () => {
    useConnectionStore.getState().setStatus({
      status: 'connected',
      capabilities: { pluginManagement: true }
    })
    appServerSendRequest.mockResolvedValue(catalogResponse({ marketplaces: [] }))

    renderPluginsView()

    expect(await screen.findByRole('button', { name: 'Create' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'More create options' }))

    expect(await screen.findByRole('menuitem', { name: 'Create plugin' })).toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: 'Add marketplace' })).not.toBeInTheDocument()
  })

  it('collapses the create control to a plain button when only one action is available', async () => {
    useConnectionStore.getState().setStatus({
      status: 'connected',
      capabilities: { pluginManagement: true }
    })
    useConversationStore.setState({ remoteWorkspaceActive: true })
    appServerSendRequest.mockResolvedValue(catalogResponse({ marketplaces: [] }))

    renderPluginsView()

    expect(await screen.findByRole('button', { name: 'Create' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'More create options' })).not.toBeInTheDocument()
  })
})

describe('marketplace source input parsing', () => {
  it('splits sparse paths per line and drops blanks', () => {
    expect(parseSparsePaths('plugins/a\n\n  plugins/b  \n')).toEqual(['plugins/a', 'plugins/b'])
  })

  it.each([
    ['/home/user/plugins', true],
    ['~/plugins', true],
    ['./plugins', true],
    ['C:\\Users\\me\\plugins', true],
    ['owner/repo', false],
    ['https://example.com/team/repo.git', false],
    ['git@example.com:team/repo.git', false],
    ['', false]
  ])('classifies %s as local=%s', (source, expected) => {
    expect(looksLikeLocalPath(source)).toBe(expected)
  })
})
