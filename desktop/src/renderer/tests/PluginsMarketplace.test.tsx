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
import { stringifyComposerDraftSegments } from '../components/conversation/richInputSerialization'
import { installDesktopApiMock } from './desktopApiMock'

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

/**
 * Browse groups by category by default, so the marketplace grouping — and the
 * refresh/remove actions that live on it — is reached through the publisher
 * filter's marketplace mode.
 */
async function showMarketplaceGrouping(): Promise<void> {
  fireEvent.click(await screen.findByRole('button', { name: 'Filter plugin publisher' }))
  fireEvent.click(await screen.findByRole('menuitem', { name: 'Marketplaces' }))
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
    installDesktopApiMock({
        settings: { get: settingsGet },
        appServer: { sendRequest: appServerSendRequest },
        shell: { openExternal: vi.fn(), getProtocolHandlerName: vi.fn().mockResolvedValue('') },
        workspace: { pickFolder: workspacePickFolder }
      })
    workspacePickFolder.mockResolvedValue(null)
    ;(window as Window & { __confirmDialog?: unknown }).__confirmDialog = confirmDialog
    confirmDialog.mockResolvedValue(true)
  })

  // A marketplace says how an entry arrived, not what it does, so browse keeps
  // grouping by category and the entry appears there like any other.
  it('leaves marketplace entries in the category grouping by default', async () => {
    appServerSendRequest.mockResolvedValue(catalogResponse())

    renderPluginsView()

    expect(await screen.findByText('Example Plugin')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Example Plugins' })).not.toBeInTheDocument()
    expect(screen.queryByText('https://example.com/team/plugins.git')).not.toBeInTheDocument()
  })

  it('groups by marketplace once the publisher filter asks for it', async () => {
    appServerSendRequest.mockResolvedValue(catalogResponse())

    renderPluginsView()
    await screen.findByText('Example Plugin')
    await showMarketplaceGrouping()

    expect(await screen.findByRole('heading', { name: 'Example Plugins' })).toBeInTheDocument()
    expect(screen.getByText('Example Plugin')).toBeInTheDocument()
  })

  // The source identifies the group but does not earn a place in the layout.
  it('keeps the marketplace source out of the group header layout', async () => {
    appServerSendRequest.mockResolvedValue(catalogResponse())

    renderPluginsView()
    await screen.findByText('Example Plugin')
    await showMarketplaceGrouping()
    const heading = await screen.findByRole('heading', { name: 'Example Plugins' })
    expect(screen.queryByText('https://example.com/team/plugins.git')).not.toBeInTheDocument()

    fireEvent.mouseEnter(heading)
    expect(await screen.findByText('https://example.com/team/plugins.git')).toBeInTheDocument()
  })

  // A section title carries the column geometry that lines a heading up with the
  // grid beneath it, so the marketplace header row has to take that geometry over
  // rather than sit in the full width of the scroll area.
  it('aligns the group header with the ordinary section column', async () => {
    appServerSendRequest.mockResolvedValue(catalogResponse())

    renderPluginsView()
    const ordinary = await screen.findByRole('heading', { name: 'Other' })
    const column = {
      maxWidth: ordinary.style.maxWidth,
      marginLeft: ordinary.style.marginLeft,
      marginRight: ordinary.style.marginRight
    }
    expect(column.maxWidth).not.toBe('')

    await showMarketplaceGrouping()
    const header = (await screen.findByRole('heading', { name: 'Example Plugins' })).closest('div')!

    expect(header.style.maxWidth).toBe(column.maxWidth)
    expect(header.style.marginLeft).toBe(column.marginLeft)
    expect(header.style.marginRight).toBe(column.marginRight)
  })

  // Revealed by opacity, not by mounting, so the row does not shift under the pointer;
  // keyboard focus has to reveal it too or the actions are pointer-only.
  it('reveals the group actions on hover and on keyboard focus', async () => {
    appServerSendRequest.mockResolvedValue(catalogResponse())

    renderPluginsView()
    await screen.findByText('Example Plugin')
    await showMarketplaceGrouping()

    const header = (await screen.findByRole('heading', { name: 'Example Plugins' })).closest('div')!
    // The tooltip wraps the button, so reach the reveal wrapper by its own style.
    const actions = screen.getByRole('button', { name: 'Marketplace actions' })
      .closest('span[style*="opacity"]') as HTMLElement
    expect(actions.style.opacity).toBe('0')

    fireEvent.mouseEnter(header)
    expect(actions.style.opacity).toBe('1')

    fireEvent.mouseLeave(header)
    expect(actions.style.opacity).toBe('0')

    fireEvent.focus(screen.getByRole('button', { name: 'Marketplace actions' }))
    expect(actions.style.opacity).toBe('1')
  })

  it('offers the marketplace mode only while a marketplace is configured', async () => {
    appServerSendRequest.mockResolvedValue(catalogResponse({ marketplaces: [] }))

    renderPluginsView()
    await screen.findByText('Example Plugin')
    fireEvent.click(await screen.findByRole('button', { name: 'Filter plugin publisher' }))

    expect(await screen.findByRole('menuitem', { name: 'All publishers' })).toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: 'Marketplaces' })).not.toBeInTheDocument()
  })

  // The segments are the composer's whole content, so the prompt needs a segment of
  // its own — a lone skill segment renders as a bare chip with the prompt dropped.
  it('stages a plugin authoring draft with the creator skill mention and the prompt', async () => {
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
      { type: 'skill', skillName: 'plugin-creator' },
      { type: 'text', value: ' help me create a plugin' }
    ])
  })

  // The plain text is only the serialization of the segments; if they can disagree,
  // whichever one a consumer reads decides what the user sees.
  it('keeps the staged text and segments in agreement', async () => {
    appServerSendRequest.mockImplementation((method: string) => {
      if (method === 'skills/list') return Promise.resolve({ skills: [pluginCreatorSkill] })
      return Promise.resolve(catalogResponse())
    })

    renderPluginsView()
    fireEvent.click(await screen.findByRole('button', { name: 'Create' }))

    await waitFor(() => {
      expect(useUIStore.getState().welcomeDraft?.segments?.length).toBe(2)
    })
    const draft = useUIStore.getState().welcomeDraft
    expect(stringifyComposerDraftSegments(draft?.segments ?? [])).toBe(draft?.text)
    expect(draft?.selectionStart).toBe(draft?.text.length)
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
    expect(useUIStore.getState().welcomeDraft?.segments).toEqual([
      { type: 'text', value: 'help me create a plugin' }
    ])
  })

  it('adds a marketplace with the reference and sparse paths from the dialog', async () => {
    appServerSendRequest.mockImplementation((method: string) => {
      if (method === 'marketplace/add') {
        return Promise.resolve({ marketplace, alreadyAdded: false })
      }
      return Promise.resolve(catalogResponse())
    })

    renderPluginsView()
    await screen.findByText('Example Plugin')
    await showMarketplaceGrouping()
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
    await screen.findByText('Example Plugin')
    await showMarketplaceGrouping()
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
    await screen.findByText('Example Plugin')
    await showMarketplaceGrouping()
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
    await screen.findByText('Example Plugin')
    await showMarketplaceGrouping()
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
