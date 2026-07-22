import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ConfirmDialogHost } from '../components/ui/ConfirmDialog'
import { SkillsView } from '../components/skills/SkillsView'
import { useSkillsStore } from '../stores/skillsStore'
import { useSkillMarketStore } from '../stores/skillMarketStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useToastStore } from '../stores/toastStore'

const settingsGet = vi.fn()
const appServerSendRequest = vi.fn()
const skillMarketSearch = vi.fn()
const skillMarketDetail = vi.fn()
const skillMarketInstall = vi.fn()
const skillMarketPrepareDotCraftInstall = vi.fn()
const workspaceConfigGetCore = vi.fn()
const openExternal = vi.fn()
let gitLocalInstalled = true
let gitLocalHasVariant = false

function renderView(): void {
  render(
    <LocaleProvider>
      <ConfirmDialogHost />
      <SkillsView />
    </LocaleProvider>
  )
}

describe('SkillsView marketplace browse and manage modes', () => {
  afterEach(() => {
    cleanup()
    useUIStore.getState().setPendingWelcomeTurn(null)
    vi.clearAllTimers()
  })

  beforeEach(() => {
    vi.clearAllMocks()
    gitLocalInstalled = true
    gitLocalHasVariant = false
    useSkillsStore.setState({
      skills: [],
      loading: false,
      error: null,
      selectedSkillName: null,
      skillContent: null,
      contentLoading: false
    })
    useSkillMarketStore.setState({
      query: '',
      provider: 'all',
      results: [],
      loading: false,
      error: null,
      selectedSkill: null,
      detailLoading: false,
      installSlug: null,
      dotCraftInstallSlug: null
    })
    useConnectionStore.getState().reset()
    useConversationStore.getState().reset()
    useConversationStore.setState({ remoteWorkspaceActive: false })
    useConnectionStore.getState().setStatus({
      status: 'connected',
      capabilities: {
        skillsManagement: true,
        skillVariants: true
      }
    })
    useThreadStore.getState().reset()
    useThreadStore.getState().setActiveThreadId('thread-existing')
    useUIStore.setState({
      activeMainView: 'skills',
      welcomeDraft: null,
      pendingWelcomeTurn: null
    })
    useToastStore.setState({ toasts: [] })
    settingsGet.mockResolvedValue({ locale: 'en' })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'skills/list') {
        const skills = [
          {
            name: 'memory',
            displayName: 'Memory',
            shortDescription: 'Remember project facts',
            description: 'Remember project facts',
            source: 'builtin',
            available: true,
            enabled: true,
            hasVariant: true,
            path: 'E:\\Git\\dotcraft\\.craft\\skills\\memory\\SKILL.md'
          }
        ]
        if (gitLocalInstalled) {
          skills.push({
            name: 'git-local',
            displayName: 'Git Local',
            shortDescription: 'Local git workflows',
            description: 'Local git workflows',
            source: 'workspace',
            available: true,
            enabled: true,
            hasVariant: gitLocalHasVariant,
            path: 'E:\\Git\\dotcraft\\.craft\\skills\\git-local\\SKILL.md'
          })
        }
        return {
          skills
        }
      }
      if (method === 'skills/setEnabled') {
        return {
          skill: {
            name: 'memory',
            description: 'Remember project facts',
            source: 'builtin',
            available: true,
            enabled: false,
            path: 'E:\\Git\\dotcraft\\.craft\\skills\\memory\\SKILL.md'
          }
        }
      }
      if (method === 'skills/restoreOriginal') {
        return { restored: true }
      }
      if (method === 'skills/uninstall') {
        gitLocalInstalled = false
        return {
          name: 'git-local',
          uninstalled: true,
          source: 'workspace',
          removedSourcePath: 'E:\\Git\\dotcraft\\.craft\\skills\\git-local',
          removedVariantCount: 0
        }
      }
      if (method === 'thread/start') {
        return {
          thread: {
            id: 'thread-dotcraft-install',
            displayName: null,
            status: 'active',
            originChannel: 'dotcraft-desktop',
            createdAt: '2026-05-01T00:00:00.000Z',
            lastActiveAt: '2026-05-01T00:00:00.000Z'
          }
        }
      }
      return { content: '---\nname: memory\n---\n# Memory' }
    })
    skillMarketSearch.mockResolvedValue({
      skills: [
        {
          provider: 'clawhub',
          slug: 'git-helper',
          name: 'Git Helper',
          description: 'Git workflow help',
          version: '1.0.0',
          downloads: 34,
          installed: false
        }
      ]
    })
    skillMarketDetail.mockResolvedValue({
      provider: 'clawhub',
      slug: 'git-helper',
      name: 'Git Helper',
      description: 'Git workflow help',
      readme: '# Git Helper\n\nUse git safely.',
      version: '1.0.0',
      installed: false
    })
    skillMarketInstall.mockResolvedValue({
      skillName: 'git-helper',
      targetDir: 'E:\\Git\\dotcraft\\.craft\\skills\\git-helper',
      overwritten: false
    })
    skillMarketPrepareDotCraftInstall.mockResolvedValue({
      skillName: 'git-helper',
      provider: 'clawhub',
      slug: 'git-helper',
      version: '1.0.0',
      sourceUrl: 'https://clawhub.ai/skills/git-helper',
      workspacePath: 'E:\\Git\\dotcraft',
      stagingDir: 'E:\\Git\\dotcraft\\.craft\\skill-install-staging\\clawhub.git-helper.2026-05-01T00-00-00-000Z',
      candidateDir: 'E:\\Git\\dotcraft\\.craft\\skill-install-staging\\clawhub.git-helper.2026-05-01T00-00-00-000Z\\source',
      metadataPath: 'E:\\Git\\dotcraft\\.craft\\skill-install-staging\\clawhub.git-helper.2026-05-01T00-00-00-000Z\\.dotcraft-dotcraft-install.json'
    })
    workspaceConfigGetCore.mockResolvedValue({
      workspace: {
        apiKey: null,
        endPoint: null,
        welcomeSuggestionsEnabled: null,
        skillsSelfLearningEnabled: true,
        memoryAutoConsolidateEnabled: null,
        defaultApprovalPolicy: null
      },
      userDefaults: {
        apiKey: null,
        endPoint: null,
        welcomeSuggestionsEnabled: null,
        skillsSelfLearningEnabled: null,
        memoryAutoConsolidateEnabled: null,
        defaultApprovalPolicy: null
      }
    })
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet },
        appServer: { sendRequest: appServerSendRequest },
        workspaceConfig: { getCore: workspaceConfigGetCore },
        skillMarket: {
          search: skillMarketSearch,
          detail: skillMarketDetail,
          install: skillMarketInstall,
          prepareDotCraftInstall: skillMarketPrepareDotCraftInstall
        },
        shell: {
          openExternal,
          openPath: vi.fn()
        }
      }
    })
  })

  it('searches local and marketplace skills from the browse page without switches', async () => {
    renderView()

    expect(await screen.findByText('Give DotCraft the ability you need')).toBeInTheDocument()
    expect(await screen.findByText('Memory')).toBeInTheDocument()
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
    expect(screen.queryByRole('switch')).not.toBeInTheDocument()

    fireEvent.change(screen.getByPlaceholderText('Search skills or install from Marketplace'), {
      target: { value: 'git' }
    })

    await waitFor(() => {
      expect(skillMarketSearch).toHaveBeenCalledWith({
        query: 'git',
        provider: 'all',
        limit: 24
      })
    })

    expect(await screen.findByText('Git Local')).toBeInTheDocument()
    expect(await screen.findByText('Git Helper')).toBeInTheDocument()
    expect(screen.queryByRole('switch')).not.toBeInTheDocument()
  })

  it('filters browse skills with the custom source menu', async () => {
    renderView()

    expect(await screen.findByText('Memory')).toBeInTheDocument()
    expect(screen.getByText('Git Local')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Filter skills' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: 'System' }))

    expect(screen.getByText('Memory')).toBeInTheDocument()
    expect(screen.queryByText('Git Local')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Filter skills' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Personal' }))

    expect(screen.queryByText('Memory')).not.toBeInTheDocument()
    expect(screen.getByText('Git Local')).toBeInTheDocument()
  })

  it('shows the local skill detail switch, menu, and scroll body', async () => {
    renderView()

    fireEvent.click(await screen.findByText('Memory'))

    const dialog = await screen.findByRole('dialog')
    expect(dialog).toBeInTheDocument()
    const moreButton = within(dialog).getByRole('button', { name: 'More actions' })
    expect(moreButton).toBeInTheDocument()
    expect(screen.getByTestId('skill-detail-scroll-body')).toBeInTheDocument()
    expect(within(dialog).getByText('Variant')).toBeInTheDocument()

    fireEvent.click(moreButton)
    expect(await screen.findByRole('menuitem', { name: 'Open folder' })).toBeInTheDocument()
    expect(await screen.findByRole('menuitem', { name: 'Restore original skill' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('switch', { name: 'Toggle Memory skill' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('skills/setEnabled', {
        name: 'memory',
        enabled: false
      })
    })
  })

  it('restores the original skill from the detail menu', async () => {
    renderView()

    fireEvent.click(await screen.findByText('Memory'))
    const dialog = await screen.findByRole('dialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'More actions' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Restore original skill' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('skills/restoreOriginal', {
        name: 'memory'
      })
    })
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('skills/view', {
        name: 'memory'
      })
    })
    expect(useToastStore.getState().toasts.some((toast) => toast.message === 'Restored original skill')).toBe(true)
  })

  it('uninstalls workspace skills from the detail footer after confirmation', async () => {
    renderView()

    fireEvent.click(await screen.findByText('Git Local'))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByRole('button', { name: 'Uninstall' })).toBeInTheDocument()

    fireEvent.click(within(dialog).getByRole('button', { name: 'Uninstall' }))
    expect(await screen.findByText('Uninstall Git Local?')).toBeInTheDocument()
    const uninstallButtons = screen.getAllByRole('button', { name: 'Uninstall' })
    fireEvent.click(uninstallButtons[uninstallButtons.length - 1]!)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('skills/uninstall', {
        name: 'git-local'
      })
    })
    expect(useToastStore.getState().toasts.some((toast) => toast.message === 'Uninstalled Git Local')).toBe(true)
  })

  it('hides uninstall for builtin and plugin skills', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'skills/list') {
        return {
          skills: [
            {
              name: 'memory',
              displayName: 'Memory',
              shortDescription: 'Remember project facts',
              description: 'Remember project facts',
              source: 'builtin',
              available: true,
              enabled: true,
              path: 'E:\\Git\\dotcraft\\.craft\\skills\\memory\\SKILL.md'
            },
            {
              name: 'browser',
              displayName: 'Browser',
              shortDescription: 'Control the embedded browser',
              description: 'Control the embedded browser',
              source: 'plugin',
              pluginId: 'browser',
              pluginDisplayName: 'Browser',
              available: true,
              enabled: true,
              path: 'E:\\Git\\dotcraft\\.craft\\plugins\\browser\\skills\\browser\\SKILL.md'
            }
          ]
        }
      }
      return { content: '---\nname: skill\n---\n# Skill' }
    })
    renderView()

    fireEvent.click(await screen.findByText('Memory'))
    let dialog = await screen.findByRole('dialog')
    expect(within(dialog).queryByRole('button', { name: 'Uninstall' })).not.toBeInTheDocument()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Close' }))

    fireEvent.click(await screen.findByText('Browser'))
    dialog = await screen.findByRole('dialog')
    expect(within(dialog).queryByRole('button', { name: 'Uninstall' })).not.toBeInTheDocument()
  })

  it('warns that uninstalling a variant removes both variant and source', async () => {
    gitLocalHasVariant = true
    renderView()

    fireEvent.click(await screen.findByText('Git Local'))
    const dialog = await screen.findByRole('dialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Uninstall' }))

    expect(await screen.findByText(/source skill folder plus associated variants/)).toBeInTheDocument()
  })

  it('shows a restore noop toast when the skill has no workspace adaptation', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'skills/restoreOriginal') return { restored: false }
      if (method === 'skills/list') {
        return {
          skills: [
            {
              name: 'memory',
              displayName: 'Memory',
              shortDescription: 'Remember project facts',
              description: 'Remember project facts',
              source: 'builtin',
              available: true,
              enabled: true,
              path: 'E:\\Git\\dotcraft\\.craft\\skills\\memory\\SKILL.md'
            }
          ]
        }
      }
      return { content: '---\nname: memory\n---\n# Memory' }
    })
    renderView()

    fireEvent.click(await screen.findByText('Memory'))
    const dialog = await screen.findByRole('dialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'More actions' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Restore original skill' }))

    await waitFor(() => {
      expect(useToastStore.getState().toasts.some((toast) => toast.message === 'This skill is already using its original source')).toBe(true)
    })
  })

  it('shows variant badges in local skill lists and detail', async () => {
    renderView()

    expect(await screen.findByText('Memory')).toBeInTheDocument()
    expect(screen.getByText('Variant')).toBeInTheDocument()

    fireEvent.click(screen.getByText('Memory'))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Variant')).toBeInTheDocument()

    fireEvent.click(within(dialog).getByRole('button', { name: 'Close' }))
    fireEvent.click(screen.getByRole('button', { name: 'Manage' }))
    expect(await screen.findByText('Skills 2')).toBeInTheDocument()
    expect(screen.getByText('Variant')).toBeInTheDocument()
  })

  it('starts a new welcome draft with the selected skill tag from detail', async () => {
    renderView()

    fireEvent.click(await screen.findByText('Memory'))
    const dialog = await screen.findByRole('dialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Try in chat' }))

    expect(useThreadStore.getState().activeThreadId).toBeNull()
    expect(useUIStore.getState().activeMainView).toBe('conversation')
    expect(useUIStore.getState().welcomeDraft).toMatchObject({
      text: '$memory',
      segments: [{ type: 'skill', skillName: 'memory' }],
      selectionStart: 1,
      selectionEnd: 1,
      mode: 'agent',
      model: 'Default',
      approvalPolicy: 'default'
    })
  })

  it('shows switches only after entering manage mode', async () => {
    renderView()

    expect(await screen.findByText('Memory')).toBeInTheDocument()
    expect(screen.queryByRole('switch')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Manage' }))

    expect(await screen.findByText('Skills 2')).toBeInTheDocument()
    expect(screen.getAllByRole('switch')).toHaveLength(2)
    expect(screen.getByPlaceholderText('Search installed skills')).toBeInTheDocument()
    expect(screen.queryByPlaceholderText('Search skills or install from Marketplace')).not.toBeInTheDocument()
  })

  it('refreshes from the toolbar refresh action', async () => {
    renderView()
    expect(await screen.findByText('Memory')).toBeInTheDocument()
    const initialCalls = appServerSendRequest.mock.calls.length

    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))

    await waitFor(() => {
      expect(appServerSendRequest.mock.calls.length).toBeGreaterThan(initialCalls)
    })
  })

  it('installs a marketplace skill and refreshes installed skills', async () => {
    renderView()
    fireEvent.change(await screen.findByPlaceholderText('Search skills or install from Marketplace'), {
      target: { value: 'git' }
    })
    fireEvent.click(await screen.findByText('Git Helper'))
    fireEvent.click(await screen.findByRole('button', { name: 'Install' }))

    await waitFor(() => {
      expect(skillMarketInstall).toHaveBeenCalledWith({
        provider: 'clawhub',
        slug: 'git-helper',
        version: '1.0.0',
        overwrite: false
      })
    })
    expect(appServerSendRequest).toHaveBeenCalledWith('skills/list', { includeUnavailable: true })
  })

  it('keeps marketplace summary metadata after detail loads without those fields', async () => {
    skillMarketDetail.mockResolvedValueOnce({
      provider: 'clawhub',
      slug: 'git-helper',
      name: 'Git Helper',
      description: 'Git workflow help',
      readme: '# Git Helper\n\nUse git safely.',
      installed: false
    })

    renderView()
    fireEvent.change(await screen.findByPlaceholderText('Search skills or install from Marketplace'), {
      target: { value: 'git' }
    })
    fireEvent.click(await screen.findByText('Git Helper'))

    const dialog = await screen.findByRole('dialog')
    await waitFor(() => {
      expect(within(dialog).getByText('1.0.0')).toBeInTheDocument()
      expect(within(dialog).getByText('34')).toBeInTheDocument()
    })
    expect(within(dialog).queryByText('Version unknown')).not.toBeInTheDocument()
  })

  it('starts a DotCraft install thread from marketplace detail', async () => {
    renderView()
    fireEvent.change(await screen.findByPlaceholderText('Search skills or install from Marketplace'), {
      target: { value: 'git' }
    })
    fireEvent.click(await screen.findByText('Git Helper'))
    fireEvent.click(await screen.findByRole('button', { name: 'Install with DotCraft' }))

    await waitFor(() => {
      expect(skillMarketPrepareDotCraftInstall).toHaveBeenCalledWith({
        provider: 'clawhub',
        slug: 'git-helper',
        version: '1.0.0'
      })
    })
    expect(appServerSendRequest).toHaveBeenCalledWith('thread/start', {
      identity: {
        channelName: 'dotcraft-desktop',
        userId: 'local',
        channelContext: 'workspace:E:\\Git\\dotcraft',
        workspacePath: 'E:\\Git\\dotcraft'
      },
      historyMode: 'server'
    })
    expect(useThreadStore.getState().activeThreadId).toBe('thread-dotcraft-install')
    expect(useUIStore.getState().activeMainView).toBe('conversation')
    expect(useUIStore.getState().pendingWelcomeTurn).toMatchObject({
      threadId: 'thread-dotcraft-install',
      inputParts: [
        { type: 'skillRef', name: 'skill-installer' },
        expect.objectContaining({
          type: 'text',
          text: expect.stringContaining('Candidate: E:\\Git\\dotcraft\\.craft\\skill-install-staging')
        })
      ],
      mode: 'agent',
      approvalPolicy: 'default'
    })
    expect(useUIStore.getState().pendingWelcomeTurn?.inputParts[1]).toEqual(expect.objectContaining({
      type: 'text',
      text: expect.stringContaining('Local skill name: git-helper')
    }))
    expect(useUIStore.getState().pendingWelcomeTurn?.inputParts[1]).toEqual(expect.objectContaining({
      type: 'text',
      text: expect.stringContaining('Do not rewrite the candidate bundle.')
    }))
  })

  it('disables local marketplace installs in remote workspaces', async () => {
    useConversationStore.setState({ remoteWorkspaceActive: true })
    renderView()

    fireEvent.change(await screen.findByPlaceholderText('Search skills or install from Marketplace'), {
      target: { value: 'git' }
    })
    fireEvent.click(await screen.findByText('Git Helper'))
    const dialog = await screen.findByRole('dialog', { name: 'Git Helper' })

    expect(within(dialog).getByRole('button', { name: 'Install' })).toBeDisabled()
    expect(within(dialog).getByRole('button', { name: 'Install with DotCraft' })).toBeDisabled()
    expect(skillMarketInstall).not.toHaveBeenCalled()
    expect(skillMarketPrepareDotCraftInstall).not.toHaveBeenCalled()
  })

  it('disables DotCraft install when self-learning is disabled', async () => {
    workspaceConfigGetCore.mockResolvedValueOnce({
      workspace: {
        apiKey: null,
        endPoint: null,
        welcomeSuggestionsEnabled: null,
        skillsSelfLearningEnabled: false,
        memoryAutoConsolidateEnabled: null,
        defaultApprovalPolicy: null
      },
      userDefaults: {
        apiKey: null,
        endPoint: null,
        welcomeSuggestionsEnabled: null,
        skillsSelfLearningEnabled: null,
        memoryAutoConsolidateEnabled: null,
        defaultApprovalPolicy: null
      }
    })

    renderView()
    fireEvent.change(await screen.findByPlaceholderText('Search skills or install from Marketplace'), {
      target: { value: 'git' }
    })
    fireEvent.click(await screen.findByText('Git Helper'))

    const button = await screen.findByRole('button', { name: 'Install with DotCraft' })
    await waitFor(() => {
      expect(button).toBeDisabled()
    })
  })
})
