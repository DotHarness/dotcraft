import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { Sidebar } from '../components/layout/Sidebar'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConnectionStore } from '../stores/connectionStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { installDesktopApiMock } from './desktopApiMock'

vi.mock('../components/desktopPlugins/DesktopPluginIcon', () => ({
  resolveDesktopPluginIcon: () => () => <svg aria-hidden />
}))

const settingsGet = vi.fn()

function renderSidebar(): void {
  render(
    <LocaleProvider>
      <Sidebar workspaceName="dotcraft" workspacePath="X:\\fixtures\\workspace" />
    </LocaleProvider>
  )
}

describe('Sidebar Agent Builder navigation', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    installDesktopApiMock({
      settings: { get: settingsGet },
      workspace: {
        getRecent: vi.fn().mockResolvedValue([]),
        clearSelection: vi.fn().mockResolvedValue(undefined),
        switch: vi.fn().mockResolvedValue(undefined),
        clearRecent: vi.fn().mockResolvedValue(undefined)
      },
      shell: { openPath: vi.fn().mockResolvedValue(undefined) }
    })
    useConnectionStore.getState().reset()
    useThreadStore.getState().reset()
    useUIStore.setState({
      activeMainView: 'conversation',
      sidebarCollapsed: false,
      sidebarPreferredCollapsed: false
    })
  })

  it('keeps Agents available in expanded navigation', () => {
    renderSidebar()

    fireEvent.click(screen.getByRole('button', { name: 'Agents' }))
    expect(useUIStore.getState().activeMainView).toBe('agents')
  })

  it('keeps Agents available in collapsed navigation', () => {
    useUIStore.setState({ sidebarCollapsed: true, sidebarPreferredCollapsed: true })
    renderSidebar()

    expect(screen.getByRole('button', { name: 'Agents' })).toBeInTheDocument()
  })

  it('uses the localized Agent Builder label', async () => {
    settingsGet.mockResolvedValue({ locale: 'ja' })
    renderSidebar()

    expect(await screen.findByRole('button', { name: 'エージェント' })).toBeInTheDocument()
  })
})
