import '../../../renderer/tests/setupPluginRuntime'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { DesktopPluginComposerSurfaceContext } from '@dotcraft/plugin'
import type { OwnedThreadRecord } from '../../../shared/ownedThreads'
import {
  buildConductorMenuEntries,
  confirmAndArchiveConductor,
  createConductorComposerGate
} from './conductor-lifecycle'
import { DesktopPluginSurface } from '../../../renderer/components/desktopPlugins/DesktopPluginSurface'
import { ConfirmDialogHost, requestConfirmDialog } from '../../../renderer/components/ui/ConfirmDialog'
import { LocaleProvider } from '../../../renderer/contexts/LocaleContext'
import {
  clearDesktopPluginRegistry,
  registerDesktopPluginSurface
} from '../../../renderer/plugins/desktopPluginRegistry'
import { createDesktopPluginThreads } from '../../../renderer/plugins/desktopPluginThreads'
import { useOwnedThreadsStore } from '../../../renderer/stores/ownedThreadsStore'
import { useThreadStore } from '../../../renderer/stores/threadStore'
import { installDesktopApiMock } from '../../../renderer/tests/desktopApiMock'
import { installOratorioTestHost } from './oratorioPluginTestHost'

const CONDUCTOR_RECORD: OwnedThreadRecord = {
  pluginId: 'oratorio',
  key: 'conductor',
  space: 'conductor',
  title: 'Oratorio',
  threadId: 'thread-conductor-1',
  workspacePath: '/chats',
  fingerprint: '{}',
  paused: true
}

const COMPOSER_CONTEXT: DesktopPluginComposerSurfaceContext = {
  workspacePath: '/chats',
  threadId: CONDUCTOR_RECORD.threadId,
  mode: 'agent',
  busy: false,
  awaitingApproval: false,
  variant: 'default',
  minimalChrome: false,
  provider: null
}

describe('Oratorio conductor lifecycle', () => {
  beforeEach(() => {
    clearDesktopPluginRegistry()
    useOwnedThreadsStore.getState().setRecords([CONDUCTOR_RECORD])
    useThreadStore.setState({ activeThreadId: CONDUCTOR_RECORD.threadId, activeThread: null })
  })

  afterEach(() => {
    cleanup()
    clearDesktopPluginRegistry()
    useOwnedThreadsStore.getState().setRecords([])
    useThreadStore.setState({ activeThreadId: null, activeThread: null })
  })

  it('builds Pause/Resume and Archive entries and triggers resume check-in and archive reset', async () => {
    const setOwnedPaused = vi.fn(async () => ({ ...CONDUCTOR_RECORD, paused: undefined }))
    const archiveOwned = vi.fn(async () => true)
    const runCheckIn = vi.fn(async () => true)
    const reset = vi.fn()
    installDesktopApiMock({
      settings: { get: async () => ({ locale: 'en' }) as never },
      threadTasks: { setOwnedPaused, archiveOwned }
    })
    const threads = createDesktopPluginThreads('oratorio', (cleanupFn) => cleanupFn)
    const host = installOratorioTestHost({
      threads,
      ui: {
        showToast: () => () => undefined,
        confirm: (options) => requestConfirmDialog(options).result,
        requestSpaceGuide: () => undefined,
        addThreadMenuItems: () => () => undefined,
        add: () => () => undefined,
        wrap: () => () => undefined
      } as never
    })
    const hooks = { proactive: { runCheckIn, reset } }

    const entries = buildConductorMenuEntries(host, hooks)
    expect(entries.map((entry) => ('label' in entry ? entry.label : ''))).toEqual(['Resume', 'Archive'])

    const disposeComposer = registerDesktopPluginSurface(
      'oratorio',
      host,
      'composer',
      'wrap',
      createConductorComposerGate(hooks)
    )

    render(
      <LocaleProvider>
        <DesktopPluginSurface name="composer" context={COMPOSER_CONTEXT}>
          <div data-testid="underlying-composer">Composer</div>
        </DesktopPluginSurface>
        <ConfirmDialogHost />
      </LocaleProvider>
    )

    fireEvent.click(screen.getByTestId('conductor-paused-bar-resume'))
    expect(setOwnedPaused).toHaveBeenCalledWith(CONDUCTOR_RECORD.threadId, false)
    await vi.waitFor(() => expect(runCheckIn).toHaveBeenCalledWith('resume'))

    const archivePromise = confirmAndArchiveConductor(host, hooks)
    const confirmButton = await screen.findByRole('button', { name: 'Archive' })
    fireEvent.click(confirmButton)
    await expect(archivePromise).resolves.toBe(true)
    expect(archiveOwned).toHaveBeenCalledWith(CONDUCTOR_RECORD.threadId)
    expect(reset).toHaveBeenCalledTimes(1)
    expect(useOwnedThreadsStore.getState().records).toEqual([])
    disposeComposer()
  })
})
