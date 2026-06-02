import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ServersPanel } from '../components/settings/panels/servers/ServersPanel'
import { useRemoteServersStore } from '../stores/remoteServersStore'
import type { LocalSshConfigInfo, RemoteHost } from '../../shared/remoteServers'

const sshConfig: LocalSshConfigInfo = {
  sshDir: '/Users/test/.ssh',
  configPath: '/Users/test/.ssh/config',
  configExists: true,
  agentAvailable: true,
  aliases: [
    {
      alias: 'prod',
      hostName: 'prod.example.com',
      user: 'deploy',
      port: '22',
      identityFiles: ['~/.ssh/prod_key']
    }
  ],
  identities: [
    {
      path: '~/.ssh/id_ed25519',
      source: 'default',
      exists: true
    },
    {
      path: '~/.ssh/prod_key',
      source: 'config',
      exists: true,
      hostAliases: ['prod']
    }
  ]
}

function resetRemoteServersStore(): void {
  useRemoteServersStore.setState({
    hosts: [],
    loaded: false,
    loading: false,
    selectedHostId: null,
    testing: {},
    testResults: {},
    discovering: {},
    statuses: {},
    statusLoading: {},
    busyStacks: {},
    activeStack: null,
    sshConfig: null,
    sshConfigLoading: false,
    error: null
  })
}

describe('ServersPanel', () => {
  beforeEach(() => {
    resetRemoteServersStore()
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        remoteServers: {
          list: vi.fn<() => Promise<RemoteHost[]>>().mockResolvedValue([]),
          sshConfig: vi.fn<() => Promise<LocalSshConfigInfo>>().mockResolvedValue(sshConfig),
          create: vi.fn(),
          update: vi.fn(),
          delete: vi.fn(),
          test: vi.fn().mockResolvedValue({ reachable: true, dockerOk: true, composeOk: true }),
          discoverStacks: vi.fn().mockResolvedValue([]),
          status: vi.fn(),
          logs: vi.fn(),
          action: vi.fn(),
          openInDesktop: vi.fn(),
          openDashboard: vi.fn(),
          disconnect: vi.fn()
        }
      }
    })
  })

  it('opens Add server as a settings page and shows local SSH choices', async () => {
    render(<ServersPanel />)

    fireEvent.click(await screen.findByRole('button', { name: /add server/i }))

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(screen.getByText('Saved SSH aliases')).toBeInTheDocument()

    await waitFor(() => {
      expect(screen.getByText('prod')).toBeInTheDocument()
      expect(screen.getByText('~/.ssh/id_ed25519')).toBeInTheDocument()
    })
  })

  it('opens Add stack as a settings page instead of a modal', async () => {
    useRemoteServersStore.setState({
      hosts: [
        {
          id: 'h_prod',
          name: 'Prod',
          sshTarget: 'prod',
          stacks: []
        }
      ],
      loaded: true,
      selectedHostId: 'h_prod'
    })

    render(<ServersPanel />)

    fireEvent.click(screen.getAllByRole('button', { name: /add stack/i })[0])

    await waitFor(() => {
      expect(window.api.remoteServers.test).toHaveBeenCalledWith({ id: 'h_prod' })
    })
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(screen.getByText('Register a DotCraft Docker Compose deployment on Prod.')).toBeInTheDocument()
    expect(screen.getByText('Deployment')).toBeInTheDocument()
    expect(screen.getByText('Ports')).toBeInTheDocument()
  })

  it('auto-tests saved servers when the panel opens', async () => {
    const host: RemoteHost = {
      id: 'h_prod',
      name: 'Prod',
      sshTarget: 'prod',
      stacks: []
    }
    window.api.remoteServers.list = vi.fn<() => Promise<RemoteHost[]>>().mockResolvedValue([host])

    render(<ServersPanel />)

    await waitFor(() => {
      expect(window.api.remoteServers.test).toHaveBeenCalledWith({ id: 'h_prod' })
    })
  })

  it('hides reachable SSH status until the user manually tests the server', async () => {
    const host: RemoteHost = {
      id: 'h_prod',
      name: 'Prod',
      sshTarget: 'prod',
      stacks: []
    }
    useRemoteServersStore.setState({
      hosts: [host],
      loaded: true,
      selectedHostId: 'h_prod'
    })

    render(<ServersPanel />)

    await waitFor(() => {
      expect(window.api.remoteServers.test).toHaveBeenCalledTimes(1)
    })
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /test ssh/i })).not.toBeDisabled()
    })
    expect(screen.queryByText('Reachable')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /test ssh/i }))

    await waitFor(() => {
      expect(window.api.remoteServers.test).toHaveBeenCalledTimes(2)
      expect(screen.getByText('Reachable')).toBeInTheDocument()
    })
  })

  it('discovers a remote DotCraft stack and fills the Add stack form', async () => {
    useRemoteServersStore.setState({
      hosts: [
        {
          id: 'h_prod',
          name: 'Prod',
          sshTarget: 'prod',
          stacks: []
        }
      ],
      loaded: true,
      selectedHostId: 'h_prod'
    })
    window.api.remoteServers.discoverStacks = vi.fn().mockResolvedValue([
      {
        name: 'demo-stack',
        composeDir: '/srv/sample/demo-stack/deploy',
        workspaceDir: '/srv/sample/demo-stack/deploy/workspace',
        projectName: 'deploy',
        appServerPort: 9100,
        dashboardPort: 8080,
        sandboxProfile: false
      }
    ])

    render(<ServersPanel />)

    await waitFor(() => {
      expect(window.api.remoteServers.test).toHaveBeenCalledWith({ id: 'h_prod' })
    })
    fireEvent.click(screen.getAllByRole('button', { name: /add stack/i })[0])
    fireEvent.click(screen.getByRole('button', { name: /discover/i }))

    await waitFor(() => {
      expect(window.api.remoteServers.discoverStacks).toHaveBeenCalledWith('h_prod')
      expect(screen.getByDisplayValue('demo-stack')).toBeInTheDocument()
      expect(screen.getByDisplayValue('/srv/sample/demo-stack/deploy')).toBeInTheDocument()
      expect(screen.getByDisplayValue('/srv/sample/demo-stack/deploy/workspace')).toBeInTheDocument()
      expect(screen.getByDisplayValue('deploy')).toBeInTheDocument()
    })
  })
})
