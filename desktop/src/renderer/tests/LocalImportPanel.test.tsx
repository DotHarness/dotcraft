import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import type { LocalImportItem, LocalImportRunParams, LocalImportSource, LocalImportState } from '../../shared/localImport'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ImportPanel } from '../components/settings/panels/ImportPanel'
import { useUIStore } from '../stores/uiStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { installDesktopApiMock } from './desktopApiMock'

const now = '2026-11-19T12:00:00.000Z'
const setup = { all: false, workspace: ['skills'], user: ['plugins'], sessions: false }

function item(
  key: string,
  destination: LocalImportItem['destination'],
  projectName: string | null,
  category: string,
  title: string
): LocalImportItem {
  const projectPath = projectName === null ? null : `C:/projects/${projectName}`
  return {
    key,
    destination,
    projectName,
    projectPath,
    candidate: {
      source: 'claude-code', sourceId: title, category, title,
      cwd: projectPath ?? '', sourcePath: '', targetPath: '', fallbackText: '', turnCount: 1, updatedAt: now,
      fingerprint: `digest-${key}`,
      scope: destination === 'personal' ? 'user' : 'workspace',
      state: destination === 'unavailable' ? 'unsupported' : 'new',
      reason: destination === 'unavailable' ? 'import_project_unavailable' : ''
    }
  }
}

const tools = item('personal-plugin', 'personal', null, 'plugins', 'Notes')
const projectA = item('a-skill', 'project', 'Project A', 'skills', 'Formatter')
const projectB = item('b-skill', 'newProject', 'Project B', 'skills', 'Formatter')
const recentChat = item('recent-chat', 'chats', null, 'sessions', 'Release notes')
const missing = item('missing', 'unavailable', 'Missing project', 'sessions', 'Missing chat')
const detection: LocalImportSource = {
  source: 'claude-code', available: true, items: [tools, projectA, projectB, recentChat, missing], importableCount: 4
}

function state(overrides: Partial<LocalImportState> = {}): LocalImportState {
  return {
    enabled: true,
    settings: { hasImported: false, selection: setup, sources: [], syncEnabled: false, syncIntervalMinutes: 720, workspaceOptOut: false },
    running: null,
    imports: [],
    attention: [],
    ...overrides
  }
}

function panel(workspacePath: string, local = true): JSX.Element {
  return <LocaleProvider loadSettings={false}><ImportPanel local={local} workspacePath={workspacePath} /></LocaleProvider>
}

describe('local Import', () => {
  beforeEach(() => {
    useWorkspaceProjectsStore.getState().reset()
    useUIStore.getState().clearPendingProjectThreadOpen()
  })

  afterEach(() => vi.useRealTimers())

  it('keeps an opted-in project selected across a foreground switch and submits only confirmed items', async () => {
    const run = vi.fn(async () => ({ importId: 'batch' }))
    const sendRequest = vi.fn()
    installDesktopApiMock({
      imports: { detect: async () => ({ sources: [detection] }), run, getState: async () => state() },
      appServer: { sendRequest }
    })
    const { rerender } = render(panel('C:/projects/Project A'))

    fireEvent.click(await screen.findByRole('button', { name: 'Import from Claude Code' }))
    const checkbox = (name: string) => within(screen.getByRole('dialog')).getByRole('checkbox', { name })
    expect(checkbox('Project B')).not.toBeChecked()
    fireEvent.click(checkbox('Project B'))
    rerender(panel('remote:other-project'))
    expect(checkbox('Project B')).toBeChecked()
    expect(run).not.toHaveBeenCalled()

    const dialog = within(screen.getByRole('dialog'))
    fireEvent.click(checkbox('Project A'))
    fireEvent.click(dialog.getByRole('button', { name: 'Import to DotCraft' }))
    await waitFor(() => expect(run).toHaveBeenCalledExactlyOnceWith({
      source: 'claude-code',
      selection: { ...setup, sessions: true },
      offered: { ...setup, sessions: true },
      items: [tools, projectB, recentChat].map(({ key, candidate }) => ({ key, fingerprint: candidate.fingerprint }))
    }))
    expect(sendRequest).not.toHaveBeenCalled()
  })

  it('disables import when a source has no selectable items', async () => {
    installDesktopApiMock({ imports: {
      detect: async () => ({ sources: [{ ...detection, items: [missing], importableCount: 0 }] }),
      getState: async () => state()
    } })
    render(panel('C:/projects/Foreground'))
    expect(await screen.findByRole('button', { name: 'Import from Claude Code' })).toBeDisabled()
  })

  it('polls one aggregate pass and opens a result through the recorded project', async () => {
    vi.useFakeTimers()
    let current = state()
    const run = vi.fn(async ({ source }: LocalImportRunParams) => {
      current = state({ running: { importId: 'one-pass', source, completed: 1, total: 2 } })
      return { importId: 'one-pass' }
    })
    const switchProject = vi.fn(async () => {})
    installDesktopApiMock({
      imports: { detect: async () => ({ sources: [detection] }), run, getState: async () => current },
      workspace: { switch: switchProject }
    })
    useWorkspaceProjectsStore.getState().setPayload({ foregroundWorkspacePath: 'C:/projects/Foreground', projects: [], secondaryLimit: 8 })
    await act(async () => { render(panel('C:/projects/Foreground')) })
    const importButton = screen.getByRole('button', { name: 'Import from Claude Code' })
    fireEvent.click(importButton)
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Import to DotCraft' })) })
    expect(importButton).toBeDisabled()

    current = state({
      imports: [{
        importId: 'one-pass', trigger: 'manual', completedAt: now, startedAt: now,
        outcomes: [
          { category: 'sessions', scope: 'workspace', source: 'claude-code', sourceId: 'id1', status: 'imported',
            targetPath: '', title: 'Imported chat', threadId: 'open-this-chat', projectName: 'Project A', projectPath: 'C:/projects/Project A' },
          { category: 'skills', scope: 'workspace', source: 'claude-code', sourceId: 'id2', status: 'failed',
            targetPath: '', title: 'Another item', projectName: 'Project B', projectPath: 'C:/projects/Project B', errorCode: 'import_pass_failed' }
        ]
      }]
    })
    await act(async () => { await vi.runOnlyPendingTimersAsync() })
    expect(importButton).toBeEnabled()
    fireEvent.click(screen.getByRole('button', { name: /^Skills\b/i }))
    const failedRow = screen.getByText('Another item').closest('[role=listitem]') as HTMLElement
    expect(within(failedRow).queryByRole('button')).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /^Chats\b/i }))
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: /Open chat.*Imported chat/i })) })
    expect(switchProject).toHaveBeenCalledExactlyOnceWith('C:/projects/Project A', undefined, { existingProjectOnly: true })
    expect(useUIStore.getState().pendingProjectThreadOpen).toEqual(expect.objectContaining({ threadId: 'open-this-chat' }))
  })

  it.each(['unavailable', 'superseded'])('keeps history open when switching for a settings outcome is %s', async reason => {
    const path = 'C:/projects/Target'
    const switchProject = reason === 'superseded'
      ? vi.fn().mockResolvedValue(false)
      : vi.fn().mockRejectedValue(new Error('Project is no longer available.'))
    useUIStore.getState().setActiveSettingsTab('import')
    installDesktopApiMock({
      imports: { detect: async () => ({ sources: [] }), getState: async () => state({
        imports: [{
          importId: 'finished', trigger: 'manual', completedAt: now, startedAt: now, outcomes: [{
            category: 'mcp', scope: 'workspace', source: 'claude-code', sourceId: 'mcp-item', status: 'imported',
            targetPath: '', title: 'Tool server', projectName: 'Target', projectPath: path
          }]
        }]
      }) },
      workspace: { switch: switchProject }
    })
    render(panel('C:/projects/Foreground'))

    fireEvent.click(await screen.findByRole('button', { name: 'MCP servers' }))
    const action = screen.getByRole('button', { name: /Open MCP settings.*Tool server/i })
    await act(async () => { fireEvent.click(action) })

    expect(switchProject).toHaveBeenCalledExactlyOnceWith(path, undefined, { existingProjectOnly: true })
    expect(useUIStore.getState().activeSettingsTab).toBe('import')
  })

  it('keeps a remote-only window scoped to the connected server', async () => {
    const getState = vi.fn(async () => state())
    const sendRequest = vi.fn(async (method: string) => {
      if (method === 'import/detect') return { sources: [{ ...detection, items: [projectA.candidate] }] }
      if (method === 'import/settings/get') return { settings: state().settings }
      if (method === 'import/history/list') return { imports: [], attention: [] }
      throw new Error(method)
    })
    installDesktopApiMock({ imports: { getState }, appServer: { sendRequest } })
    render(panel('/remote/project', false))
    expect(await screen.findByRole('button', { name: 'Import from Claude Code' })).toBeEnabled()
    expect(getState).not.toHaveBeenCalled()
  })
})
