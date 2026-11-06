import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useAddProjectFlow } from '../components/projects/AddProject'
import type { WorkspaceProjectSummary } from '../../shared/workspaceProjects'
import { installDesktopApiMock } from './desktopApiMock'

const project: WorkspaceProjectSummary = {
  kind: 'local', projectId: 'x:/app', path: 'X:/app', identityWorkspacePath: 'X:/app',
  name: 'sample', secondaryFolders: ['X:/lib'], state: 'foreground',
  running: true, loaded: true, threadCount: 1, threads: [], pinned: false
}

function Editor(): JSX.Element {
  const flow = useAddProjectFlow()
  return <>
    <button onClick={() => flow.beginEdit(project, true)}>Edit</button>
    {flow.dialog}
  </>
}

describe('editing local Project sources', () => {
  it('promotes a source without switching the active Project state workspace', async () => {
    const save = vi.fn().mockResolvedValue({ path: project.path })
    const switchWorkspace = vi.fn()
    installDesktopApiMock({
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
      workspace: { saveLocalProject: save, switch: switchWorkspace }
    })
    render(<LocaleProvider><Editor /></LocaleProvider>)
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }))
    fireEvent.click(screen.getByRole('button', { name: 'Make primary' }))
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(save).toHaveBeenCalledWith({
      projectPath: 'X:/app', primaryFolder: 'X:/lib', secondaryFolders: ['X:/app'], name: 'sample'
    })
    expect(switchWorkspace).not.toHaveBeenCalled()
  })
})
