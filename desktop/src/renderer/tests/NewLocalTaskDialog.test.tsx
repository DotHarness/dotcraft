import './setupPluginRuntime'
import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { NewLocalTaskDialog, normalizeLabels } from '../../bundled-plugins/oratorio/src/NewLocalTaskDialog'
import type { OratorioTask } from '../../bundled-plugins/oratorio/src/oratorio-model'
import { installDesktopApiMock } from './desktopApiMock'
import { installOratorioTestHost } from './oratorioPluginTestHost'

const tasks: OratorioTask[] = [
  task({ id: 'issue-1', repository: 'sample-org/widget-service', assignee: 'octocat', branch: 'develop', labels: ['bug', 'frontend'] }),
  task({ id: 'issue-2', repository: 'sample-org/api-service', assignee: 'maintainer', branch: 'release', labels: ['docs'] }),
]

describe('NewLocalTaskDialog', () => {
  beforeEach(() => {
    window.localStorage.clear()
    installDesktopApiMock({ settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }), set: vi.fn() } })
    installOratorioTestHost()
  })

  it('uses shared task controls without the retired product eyebrow or comma field', () => {
    renderDialog()

    expect(screen.getByRole('dialog', { name: 'New local task' })).toBeInTheDocument()
    expect(screen.queryByText('Oratorio')).not.toBeInTheDocument()
    expect(screen.queryByPlaceholderText('Comma-separated labels')).not.toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Repository' })).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Assignee' })).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Base branch' })).toHaveAttribute('placeholder', 'Repository default')
  })

  it('creates a normalized task from repository, label, assignee, and branch choices', () => {
    const onCreate = vi.fn()
    renderDialog(onCreate)

    fireEvent.change(screen.getByPlaceholderText('What needs to be done?'), { target: { value: '  Improve task creation  ' } })
    fireEvent.click(screen.getByRole('combobox', { name: 'Repository' }))
    fireEvent.click(screen.getByRole('option', { name: 'sample-org/widget-service' }))
    fireEvent.click(screen.getByRole('button', { name: 'bug' }))
    fireEvent.click(screen.getByRole('button', { name: 'Add label' }))
    const labelInput = screen.getByPlaceholderText('Label name')
    fireEvent.change(labelInput, { target: { value: ' BUG ' } })
    fireEvent.keyDown(labelInput, { key: 'Enter' })
    fireEvent.change(screen.getByRole('combobox', { name: 'Assignee' }), { target: { value: 'octocat' } })
    fireEvent.change(screen.getByRole('combobox', { name: 'Base branch' }), { target: { value: 'develop' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create task' }))

    expect(onCreate).toHaveBeenCalledWith({
      title: 'Improve task creation',
      description: 'No description provided.',
      repository: 'sample-org/widget-service',
      labels: ['bug'],
      assignee: 'octocat',
      branch: 'develop',
    })
    expect(window.localStorage.getItem('dotcraft.oratorio.localTask.repository')).toBe('sample-org/widget-service')
  })

  it('selects the only repository automatically and removes labels as pills', () => {
    render(
      <LocaleProvider>
        <NewLocalTaskDialog tasks={[tasks[0]]} onCancel={vi.fn()} onCreate={vi.fn()} />
      </LocaleProvider>
    )

    expect(screen.getByRole('combobox', { name: 'Repository' })).toHaveTextContent('sample-org/widget-service')
    fireEvent.click(screen.getByRole('button', { name: 'bug' }))
    expect(screen.getByText('bug')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Remove label bug' }))
    expect(screen.getByRole('button', { name: 'bug' })).toBeInTheDocument()
  })
})

describe('normalizeLabels', () => {
  it('trims labels and keeps the first spelling case-insensitively', () => {
    expect(normalizeLabels([' bug ', '', 'BUG', 'Docs'])).toEqual(['bug', 'Docs'])
  })
})

function renderDialog(onCreate = vi.fn()): void {
  render(
    <LocaleProvider>
      <NewLocalTaskDialog tasks={tasks} onCancel={vi.fn()} onCreate={onCreate} />
    </LocaleProvider>
  )
}

function task(overrides: Partial<OratorioTask>): OratorioTask {
  return {
    id: 'task', shortId: 'TASK-1', sourceLabel: '#1', provider: 'github', repository: 'sample-org/widget-service', kind: 'Issue',
    title: 'Fixture task', description: 'Fixture description', assignee: null, labels: [], column: 'todo', state: 'discovered',
    lifecycle: 'open', updated: 'just now', artifacts: { reviewDrafts: 0, implementationDrafts: 0, followUpDrafts: 0, comments: 0, writes: 0 },
    capabilities: {}, ...overrides,
  }
}
