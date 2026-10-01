// @vitest-environment jsdom
import { act } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ConfirmDialogHost } from '../components/ui/ConfirmDialog'
import { SshMachinesSegment } from '../components/settings/panels/ssh/SshMachinesSegment'
import { useSshMachinesStore } from '../stores/sshMachinesStore'
import type { SshMachineView, SshMachinesPayload } from '../../shared/sshMachines'
import type { RemoteStack } from '../../shared/dockerDeployments'
import { installDesktopApiMock } from './desktopApiMock'

function machine(overrides: Partial<SshMachineView> = {}): SshMachineView {
  return {
    id: 'm1',
    name: 'build-box',
    source: 'manual',
    hostname: 'dev@10.0.0.12',
    autoConnect: true,
    projects: [],
    stacks: [],
    status: { kind: 'connected' },
    system: { os: 'Linux', arch: 'x86_64', home: '/home/dev', dotcraftVersion: '0.7.10', hasDocker: true },
    ...overrides
  }
}

const STACK: RemoteStack = {
  id: 's1',
  name: 'dotcraft-hub',
  composeDir: '/srv/dotcraft',
  appServerPort: 9100,
  oratorioPort: 5087,
  dashboardPort: 8080
}

let emitChanged: (payload: SshMachinesPayload) => void = () => undefined
const api = {
  list: vi.fn(),
  onChanged: vi.fn(),
  setAutoConnect: vi.fn(),
  installDotCraft: vi.fn(),
  updateDotCraft: vi.fn(),
  discoverHosts: vi.fn(),
  add: vi.fn(),
  listFolders: vi.fn(),
  addProject: vi.fn(),
  docker: {
    discover: vi.fn(),
    save: vi.fn(),
    status: vi.fn()
  }
}

beforeEach(() => {
  vi.clearAllMocks()
  useSshMachinesStore.setState({ machines: [], loaded: false, discovery: null, discovering: false })
  api.onChanged.mockImplementation((callback: (payload: SshMachinesPayload) => void) => {
    emitChanged = callback
    return () => undefined
  })
  api.setAutoConnect.mockResolvedValue({ ok: true })
  api.installDotCraft.mockResolvedValue({ kind: 'installing' })
  api.updateDotCraft.mockResolvedValue({ kind: 'installing' })
  api.add.mockResolvedValue({ ok: true, value: [] })
  api.addProject.mockResolvedValue({ id: 'p1', path: '/home/dev/src', label: 'src' })
  api.docker.discover.mockResolvedValue([])
  api.docker.status.mockResolvedValue({ stackId: 's1', health: 'running', services: [] })
  installDesktopApiMock({
    initialLocale: 'en',
    settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
    sshMachines: api
  })
})

async function renderSegment(machines: SshMachineView[]): Promise<void> {
  api.list.mockResolvedValue({ machines })
  render(
    <LocaleProvider>
      <SshMachinesSegment />
      <ConfirmDialogHost />
    </LocaleProvider>
  )
  await waitFor(() => expect(api.list).toHaveBeenCalled())
}

function openMenu(name: string): void {
  fireEvent.click(screen.getByRole('button', { name: `More actions for ${name}` }))
}

describe('SSH machines segment', () => {
  it('follows machine changes pushed from the main process', async () => {
    await renderSegment([])
    expect(screen.queryByRole('switch')).not.toBeInTheDocument()

    act(() => emitChanged({ machines: [machine()] }))

    expect(await screen.findByRole('switch', { name: 'Connect build-box' })).toBeChecked()
  })

  it('turns keep-connected off through the switch', async () => {
    await renderSegment([machine()])
    fireEvent.click(await screen.findByRole('switch', { name: 'Connect build-box' }))
    await waitFor(() => expect(api.setAutoConnect).toHaveBeenCalledWith('m1', false))
  })

  it('installs DotCraft from the inline action', async () => {
    await renderSegment([machine({ status: { kind: 'notInstalled' } })])
    fireEvent.click(await screen.findByRole('button', { name: 'Install' }))
    await waitFor(() => expect(api.installDotCraft).toHaveBeenCalledWith('m1'))
  })

  it('updates only after the stop-work confirmation', async () => {
    await renderSegment([machine({ status: { kind: 'updateRequired', installedVersion: '0.6.2' } })])
    fireEvent.click(await screen.findByRole('button', { name: 'Update' }))

    const dialog = await screen.findByRole('dialog')
    expect(api.updateDotCraft).not.toHaveBeenCalled()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Update' }))

    await waitFor(() => expect(api.updateDotCraft).toHaveBeenCalledWith('m1'))
  })

  it('adds the hosts selected from SSH config', async () => {
    api.discoverHosts.mockResolvedValue({
      sshDir: '~/.ssh',
      configPath: '~/.ssh/config',
      configExists: true,
      agentAvailable: true,
      identities: [],
      hosts: [
        { alias: 'build-box', resolvedHost: 'dev@10.0.0.12', added: true },
        { alias: 'gpu-lab', resolvedHost: 'ml@10.0.0.31', added: false },
        { alias: 'nas', resolvedHost: 'admin@nas.local', added: false }
      ]
    })
    await renderSegment([machine()])
    fireEvent.click(screen.getByRole('button', { name: 'Add' }))

    const dialog = await screen.findByRole('dialog')
    expect(await within(dialog).findByRole('checkbox', { name: 'build-box' })).toBeDisabled()
    fireEvent.click(within(dialog).getByRole('checkbox', { name: 'nas' }))
    fireEvent.click(within(dialog).getByRole('button', { name: 'Add' }))

    await waitFor(() => expect(api.add).toHaveBeenCalledWith([{ source: 'sshConfig', alias: 'nas' }]))
  })

  it('blocks a manual connection with invalid fields', async () => {
    api.discoverHosts.mockResolvedValue({
      sshDir: '~/.ssh',
      configPath: '~/.ssh/config',
      configExists: false,
      agentAvailable: true,
      identities: [],
      hosts: []
    })
    await renderSegment([machine()])
    fireEvent.click(screen.getByRole('button', { name: 'Add' }))
    const dialog = await screen.findByRole('dialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Add manually' }))

    fireEvent.change(within(dialog).getByLabelText('Display name'), { target: { value: 'build-box' } })
    fireEvent.change(within(dialog).getByLabelText('Hostname'), { target: { value: 'dev@10.0.0.70' } })
    fireEvent.change(within(dialog).getByLabelText('SSH port'), { target: { value: '70000' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Add' }))

    expect(await within(dialog).findAllByRole('alert')).toHaveLength(2)
    expect(within(dialog).getByLabelText('SSH port')).toHaveAttribute('aria-invalid', 'true')
    expect(api.add).not.toHaveBeenCalled()
  })

  it('adds the folder chosen in the remote project picker', async () => {
    api.listFolders.mockImplementation((_id: string, path?: string) =>
      Promise.resolve(
        path === '/home/dev/src'
          ? { path: '/home/dev/src', home: '/home/dev', folders: [] }
          : { path: '/home/dev', home: '/home/dev', folders: [{ name: 'src', path: '/home/dev/src', saved: false }] }
      )
    )
    await renderSegment([machine()])
    openMenu('build-box')
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Add remote project…' }))

    const dialog = await screen.findByRole('dialog')
    fireEvent.click(await within(dialog).findByRole('button', { name: /src/ }))
    await waitFor(() => expect(api.listFolders).toHaveBeenLastCalledWith('m1', '/home/dev/src'))
    const use = within(dialog).getByRole('button', { name: 'Use this folder' })
    await waitFor(() => expect(use).toBeEnabled())
    fireEvent.click(use)

    await waitFor(() => expect(api.addProject).toHaveBeenCalledWith('m1', '/home/dev/src'))
  })

  it('shows Docker deployments only when the machine has some', async () => {
    await renderSegment([machine(), machine({ id: 'm2', name: 'staging', stacks: [STACK] })])

    openMenu('build-box')
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Settings' }))
    let dialog = await screen.findByRole('dialog')
    expect(within(dialog).queryByRole('heading', { name: 'DotCraft in Docker' })).not.toBeInTheDocument()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Close' }))

    openMenu('staging')
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Settings' }))
    dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByRole('heading', { name: 'DotCraft in Docker' })).toBeInTheDocument()
  })
})
