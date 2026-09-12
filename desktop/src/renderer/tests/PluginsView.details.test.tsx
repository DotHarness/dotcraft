import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import {
  appServerSendRequest,
  browserUsePlugin,
  dotnetPlugin,
  localPlugin,
  lspOnlyPlugin,
  mcpOnlyPlugin,
  renderPluginsView,
  setupPluginsViewTest
} from './pluginsViewTestFixtures'

describe('PluginsView details', () => {
  beforeEach(setupPluginsViewTest)

  // Frameless: a section is marked by a rule under its heading, not a box around
  // its rows, so stacked groups read as one column instead of a stack of cards.
  it('draws detail sections without framing them', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [localPlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: localPlugin, snapshotRevision: 1 }
      return {}
    })

    renderPluginsView()
    fireEvent.click(await screen.findByText('External Process Echo'))

    const heading = await screen.findByText('Info')
    expect(heading.style.borderBottom).toContain('var(--border-subtle)')

    const rows = heading.parentElement!.querySelector('div')!
    expect(rows.style.border).toBe('')
    expect(rows.style.borderRadius).toBe('')
  })

  it('shows a concise error when an uninstalled plugin skill cannot be read', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [browserUsePlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: browserUsePlugin, snapshotRevision: 1 }
      if (method === 'plugin/skill/read') throw new Error('Skill not found: browser')
      return {}
    })

    renderPluginsView()
    fireEvent.click(await screen.findByText('Control the in-app browser with DotCraft'))
    fireEvent.click(await screen.findByText('browser'))

    const dialog = await screen.findByRole('dialog')
    expect(await within(dialog).findByText('Unable to load skill contents.')).toBeInTheDocument()
    expect(within(dialog).queryByText('Skill not found: browser')).not.toBeInTheDocument()
  })

  it('leaves runtime wiring rows inert on plugin details', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [mcpOnlyPlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: mcpOnlyPlugin, snapshotRevision: 1 }
      return {}
    })

    renderPluginsView()
    fireEvent.click(await screen.findByText('Review Tools MCP'))

    const row = await screen.findByText('review-tools-mcp:review')
    expect(row.closest('button')).toBeNull()
  })

  it('shows plugin-bundled MCP content on plugin details', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [mcpOnlyPlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: mcpOnlyPlugin, snapshotRevision: 1 }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByText('Review Tools MCP'))

    expect(await screen.findByText('review-tools-mcp:review')).toBeInTheDocument()
    expect(screen.getByText('MCP server')).toBeInTheDocument()
    expect(screen.getByText('STDIO · Active')).toBeInTheDocument()
  })

  it('shows plugin-bundled LSP content on plugin details', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [lspOnlyPlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: lspOnlyPlugin, snapshotRevision: 1 }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByText('C# LSP'))

    expect(await screen.findByText('csharp-lsp:csharp')).toBeInTheDocument()
    expect(screen.getByText('LSP server')).toBeInTheDocument()
    expect(screen.getByText('STDIO · Inactive · .cs')).toBeInTheDocument()
  })

  it.each([
    ['before installation', dotnetPlugin],
    ['while disabled', { ...dotnetPlugin, installed: true, installable: false }],
    ['while active', {
      ...dotnetPlugin,
      installed: true,
      installable: false,
      enabled: true,
      functions: [
        { name: 'inspect', namespace: 'review', description: 'Inspect a change.' },
        { name: 'apply', namespace: 'review', description: 'Apply a review fix.' }
      ]
    }]
  ])('shows stable declared .NET content %s', async (_state, plugin) => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [plugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin, snapshotRevision: 1 }
      return {}
    })

    renderPluginsView()
    fireEvent.click(await screen.findByText('Review Core'))

    expect(await screen.findByText('Review integration')).toBeInTheDocument()
    expect(screen.getByText('.NET extension')).toBeInTheDocument()
    expect(screen.getByText('Provides native review capabilities.')).toBeInTheDocument()
    expect(document.querySelector('[data-plugin-content-icon="dotnet"]')).toBeInTheDocument()
    expect(screen.queryByText('Acme.Review.dll')).not.toBeInTheDocument()
    expect(screen.queryByText('inspect')).not.toBeInTheDocument()
    expect(screen.queryByText('apply')).not.toBeInTheDocument()
  })


  it('enables LSP explicitly from plugin details', async () => {
    let lspEnabled = false
    const activeLspPlugin = {
      ...lspOnlyPlugin,
      lspServers: lspOnlyPlugin.lspServers.map((server) => ({ ...server, active: true }))
    }
    appServerSendRequest.mockImplementation(async (method: string) => {
      const plugin = lspEnabled ? activeLspPlugin : lspOnlyPlugin
      if (method === 'plugin/list') return { plugins: [plugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin, snapshotRevision: 1 }
      if (method === 'workspace/config/update') {
        lspEnabled = true
        return { toolsLspEnabled: true }
      }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByText('C# LSP'))
    fireEvent.click(await screen.findByRole('button', { name: 'Enable LSP' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('workspace/config/update', { toolsLspEnabled: true })
    })
    expect(await screen.findByText('STDIO · Active · .cs')).toBeInTheDocument()
  })
})
