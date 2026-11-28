import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import type { DesktopPluginHost } from '@dotcraft/plugin'
import { normalizeFeatureGuides } from '../../shared/featureGuides'
import type { WorkspaceProjectSummary } from '../../shared/workspaceProjects'
import { ORATORIO_GUIDE_ID, OratorioFeatureGuide } from '../components/featureGuides/OratorioFeatureGuide'
import { LocaleProvider } from '../contexts/LocaleContext'
import { publishDesktopPluginGeneration, withdrawDesktopPluginGeneration } from '../plugins/desktopPluginRegistry'
import { validateActivation } from '../plugins/desktopPluginValidation'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useFeatureGuideStore } from '../stores/featureGuideStore'
import { usePluginStore, type PluginEntry } from '../stores/pluginStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { installDesktopApiMock } from './desktopApiMock'

const open = vi.fn()
const label = { default: 'Oratorio' }
const component = (): null => null
const generation = validateActivation('oratorio', '1.0', 'r1', {} as DesktopPluginHost, {
  mainViews: [{ id: 'board', label, component }],
  spaces: [{ id: 'conductor', views: ['board'], label, sidebar: component, open, guide: { title: label, body: label, action: label, media: component } }]
})

function project(path: string): WorkspaceProjectSummary {
  return { kind: 'local', path, name: path, lastOpenedAt: new Date().toISOString() } as WorkspaceProjectSummary
}

function oratorio(installed: boolean): PluginEntry {
  return { id: 'oratorio', key: 'oratorio@dotcraft-bundled', installed, installable: !installed, enabled: installed } as PluginEntry
}

function renderGuide(): void {
  render(<LocaleProvider loadSettings={false}><OratorioFeatureGuide /></LocaleProvider>)
}

async function switchProject(path: string): Promise<void> {
  await act(async () => useWorkspaceProjectsStore.setState({ foregroundWorkspacePath: path }))
}

beforeEach(() => {
  installDesktopApiMock({ initialLocale: 'en', settings: { set: vi.fn().mockResolvedValue({}) } })
  useFeatureGuideStore.setState({ settings: normalizeFeatureGuides({}), initializedThisSession: false })
  useConnectionStore.setState({ status: 'connected' })
  usePluginStore.setState({ hasCompleteCatalog: true, catalogStatus: 'ready', plugins: [oratorio(false)] })
  useWorkspaceProjectsStore.setState({ foregroundWorkspacePath: '/a', projects: [project('/a'), project('/b'), project('/c')] })
})

afterEach(() => {
  cleanup()
  useFeatureGuideStore.setState(useFeatureGuideStore.getInitialState())
  usePluginStore.setState(usePluginStore.getInitialState())
  useWorkspaceProjectsStore.getState().reset()
  useConversationStore.setState({ turnStatus: 'idle' })
  withdrawDesktopPluginGeneration('oratorio')
  open.mockReset()
})

it('offers Oratorio after a project switch, never at app start', async () => {
  renderGuide()
  expect(screen.queryByRole('dialog')).toBeNull()
  await switchProject('/b')
  expect(screen.getByRole('dialog')).toBeInTheDocument()
})

it('waits for the plugin catalog after a switch', async () => {
  usePluginStore.setState({ hasCompleteCatalog: false, catalogStatus: 'pending', plugins: [] })
  renderGuide()
  await switchProject('/b')
  expect(screen.queryByRole('dialog')).toBeNull()
  await act(async () => usePluginStore.setState({ hasCompleteCatalog: true, catalogStatus: 'ready', plugins: [oratorio(false)] }))
  expect(screen.getByRole('dialog')).toBeInTheDocument()
})

it('stays quiet when Oratorio is installed, even if disabled', async () => {
  usePluginStore.setState({ plugins: [oratorio(true)] })
  renderGuide()
  await switchProject('/b')
  expect(screen.queryByRole('dialog')).toBeNull()
  expect(useFeatureGuideStore.getState().settings?.guides[ORATORIO_GUIDE_ID]).toBeUndefined()
})

it('installs Oratorio, enters its space, and settles the shared guide', async () => {
  usePluginStore.setState({
    installPlugin: vi.fn(async () => {
      publishDesktopPluginGeneration(generation)
      return { outcome: 'applied', snapshotRevision: 1 }
    })
  } as Partial<ReturnType<typeof usePluginStore.getState>>)
  renderGuide()
  await switchProject('/b')
  await act(async () => fireEvent.click(screen.getByRole('button', { name: 'Install Oratorio' })))

  expect(open).toHaveBeenCalledTimes(1)
  expect(screen.queryByRole('dialog')).toBeNull()
  expect(useFeatureGuideStore.getState().settings?.guides[ORATORIO_GUIDE_ID]).toMatchObject({ shown: 1, settled: true })
})

it('lets the guide return later when the install is refused', async () => {
  usePluginStore.setState({
    installPlugin: vi.fn(async () => ({ outcome: 'notApplied', snapshotRevision: 1 }))
  } as Partial<ReturnType<typeof usePluginStore.getState>>)
  renderGuide()
  await switchProject('/b')
  await act(async () => fireEvent.click(screen.getByRole('button', { name: 'Install Oratorio' })))

  expect(open).not.toHaveBeenCalled()
  const record = useFeatureGuideStore.getState().settings?.guides[ORATORIO_GUIDE_ID]
  expect(record?.settled).toBeUndefined()
  expect(record?.lastClosedAt).toEqual(expect.any(Number))
})
