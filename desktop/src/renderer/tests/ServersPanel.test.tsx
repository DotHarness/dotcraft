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
          test: vi.fn(),
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
})
