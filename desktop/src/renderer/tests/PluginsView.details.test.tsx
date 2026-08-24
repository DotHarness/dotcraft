import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import {
  agentTeamsPlugin,
  appServerSendRequest,
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

  // A skill is the one plugin content with a document behind it, so its row opens
  // the shared preview instead of being inert text like the runtime wiring rows.
  it('opens the skill preview from a plugin content row', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [localPlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: localPlugin, snapshotRevision: 1 }
      if (method === 'skills/list') {
        return {
          skills: [{
            name: 'external-process-echo',
            displayName: 'Echo',
            description: 'Echo plugin skill',
            source: 'plugin',
            enabled: true,
            path: '/ws/skills/echo/SKILL.md'
          }]
        }
      }
      if (method === 'skills/view') {
        return { content: '# Echo\n\nEchoes text back.' }
      }
      return {}
    })

    renderPluginsView()
    fireEvent.click(await screen.findByText('External Process Echo'))

    fireEvent.click(await screen.findByText('external-process-echo'))

    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveAttribute('aria-labelledby', 'skill-detail-title')
    expect(document.getElementById('skill-detail-title')).toHaveTextContent('Echo')
    await waitFor(() => {
      expect(within(dialog).getByText('Echoes text back.')).toBeInTheDocument()
    })
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

  it('shows Agent Teams as a desktop extension on plugin details', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [agentTeamsPlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: agentTeamsPlugin, snapshotRevision: 1 }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByText('Agent Teams'))

    expect(await screen.findByText('Team Board')).toBeInTheDocument()
    expect(screen.getByText('Desktop Extension')).toBeInTheDocument()
    expect(screen.getByText('Unlocks the card board for Agent Team.')).toBeInTheDocument()
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
