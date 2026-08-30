import type {
  DesktopPluginActivation,
  DesktopPluginEnvironmentSnapshot,
  DesktopPluginHost
} from '@dotcraft/plugin'
import { waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { THEME_CHANGED_EVENT } from '../../shared/theme'
import {
  clearDesktopPluginRegistry,
  resolveDesktopPluginLabel,
  useDesktopPluginRegistry
} from '../plugins/desktopPluginRegistry'
import {
  DesktopPluginRuntime,
  type DesktopPluginRuntimeDependencies
} from '../plugins/desktopPluginRuntime'
import { openDesktopPluginUrl } from '../plugins/desktopPluginOpenUrl'
import { clearDesktopPluginKernel } from '../plugins/desktopPluginKernel'
import type { PluginEntry } from '../stores/pluginStore'
import { useToastStore } from '../stores/toastStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { installDesktopApiMock } from './desktopApiMock'
import { DEFAULT_SEEDS } from '../../shared/themeSeed'
import { reapplyThemeSeed } from '../utils/appearance'

const revisionA = 'a'.repeat(64)
const revisionB = 'b'.repeat(64)
const revisionC = 'c'.repeat(64)
let runtime: DesktopPluginRuntime | null = null

beforeEach(() => {
  clearDesktopPluginRegistry()
  clearDesktopPluginKernel()
  useToastStore.setState({ toasts: [] })
})

afterEach(async () => {
  await runtime?.stop()
  runtime = null
  clearDesktopPluginRegistry()
  clearDesktopPluginKernel()
  document.head.querySelectorAll('link[data-dotcraft-desktop-plugin]').forEach((link) => link.remove())
  delete (window as Window & { __confirmDialog?: unknown }).__confirmDialog
})

function plugin(revision = revisionA, enabled = true): PluginEntry {
  return namedPlugin('fixture.desktop', revision, enabled)
}

function namedPlugin(id: string, revision = revisionA, enabled = true): PluginEntry {
  return {
    id,
    displayName: `${id} Desktop Plugin`,
    version: '1.0.0',
    enabled,
    installed: true,
    installable: false,
    removable: false,
    source: 'local',
    rootPath: `X:\\fixtures\\${id}`,
    functions: [],
    skills: [],
    apps: [],
    desktop: {
      entry: './desktop/dist/index.mjs',
      revision,
      styles: ['./desktop/dist/plugin.css']
    },
    mcpServers: [],
    lspServers: []
  }
}

function applyTheme(theme: 'light' | 'dark'): void {
  document.documentElement.dataset.theme = theme
  reapplyThemeSeed()
  window.dispatchEvent(new CustomEvent(THEME_CHANGED_EVENT, { detail: { mode: theme } }))
}

function deferred<T>(): { promise: Promise<T>; resolve(value: T): void } {
  let resolve!: (value: T) => void
  return {
    promise: new Promise<T>((accept) => { resolve = accept }),
    resolve
  }
}

function activation(id: string, dispose?: () => void): DesktopPluginActivation {
  return {
    mainViews: [{
      id,
      label: { default: id },
      order: 50,
      component: () => <div>{id}</div>
    }],
    settingsPages: [],
    dispose
  }
}

function dependencies(activate: (host: unknown) => unknown): DesktopPluginRuntimeDependencies & {
  registerModule: ReturnType<typeof vi.fn>
  removeModule: ReturnType<typeof vi.fn>
  importModule: ReturnType<typeof vi.fn>
} {
  const registerModule = vi.fn(async ({ pluginId, revision }: { pluginId: string; revision: string }) => ({
    entryUrl: `dotcraft-plugin://${pluginId}/${revision}/index.mjs`,
    styleUrls: [`dotcraft-plugin://${pluginId}/${revision}/plugin.css`]
  }))
  const removeModule = vi.fn().mockResolvedValue({ ok: true })
  const importModule = vi.fn().mockResolvedValue({ activate })
  return { registerModule, removeModule, importModule }
}

describe('DesktopPluginRuntime', () => {
  it('publishes one active generation and withdraws it when the plugin is disabled', async () => {
    const dispose = vi.fn()
    const deps = dependencies((host) => ({
      mainViews: [{
        id: 'fixture',
        label: { default: 'Fixture' },
        component: () => <div>Fixture</div>
      }],
      settingsPages: [{
        id: 'fixture-settings',
        label: { default: 'Fixture settings' },
        component: () => <div>Settings</div>
      }],
      dispose
    }))
    runtime = new DesktopPluginRuntime(deps)

    runtime.reconcile([plugin()])
    await waitFor(() => expect(useDesktopPluginRegistry.getState().mainViews).toHaveLength(1))

    const generation = useDesktopPluginRegistry.getState().generations.get('fixture.desktop')
    expect(generation?.revision).toBe(revisionA)
    expect(generation?.mainViews[0]?.host).toMatchObject({
      plugin: { id: 'fixture.desktop', version: '1.0.0', displayName: 'fixture.desktop Desktop Plugin' }
    })
    expect(document.head.querySelectorAll('link[data-dotcraft-desktop-plugin]')).toHaveLength(1)

    runtime.reconcile([plugin(revisionA, false)])
    await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.size).toBe(0))
    await waitFor(() => expect(dispose).toHaveBeenCalledOnce())
    expect(deps.removeModule).toHaveBeenCalledWith({ pluginId: 'fixture.desktop', revision: revisionA })
    expect(document.head.querySelectorAll('link[data-dotcraft-desktop-plugin]')).toHaveLength(0)
  })

  it('publishes and withdraws every contribution kind as one generation', async () => {
    const component = () => <div>Fixture</div>
    const deps = dependencies(() => ({
      mainViews: [{ id: 'main', label: { default: 'Main' }, component }],
      settingsPages: [{ id: 'settings', label: { default: 'Settings' }, component }],
      conversationViews: [{ id: 'conversation', label: { default: 'Conversation' }, component }],
      commands: [{ id: 'command', label: { default: 'Command' }, execute: () => {} }],
      toolRenderers: [{ id: 'renderer', presentationId: 'fixture.result', component }],
      messageActions: [{ id: 'message', label: { default: 'Message' }, execute: () => {} }]
    }))
    runtime = new DesktopPluginRuntime(deps)

    runtime.reconcile([plugin()])
    await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.size).toBe(1))
    expect(useDesktopPluginRegistry.getState()).toMatchObject({
      mainViews: [{ id: 'main' }],
      settingsPages: [{ id: 'settings' }],
      conversationViews: [{ id: 'conversation' }],
      commands: [{ id: 'command' }],
      toolRenderers: [{ id: 'renderer' }],
      messageActions: [{ id: 'message' }]
    })

    runtime.reconcile([plugin(revisionA, false)])
    await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.size).toBe(0))
    expect(useDesktopPluginRegistry.getState()).toMatchObject({
      mainViews: [],
      settingsPages: [],
      conversationViews: [],
      commands: [],
      toolRenderers: [],
      messageActions: []
    })
  })

  it('does not reload an unchanged revision', async () => {
    const deps = dependencies(() => ({ mainViews: [], settingsPages: [] }))
    runtime = new DesktopPluginRuntime(deps)
    const installed = plugin()

    runtime.reconcile([installed])
    await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.size).toBe(1))
    runtime.reconcile([installed])

    expect(deps.registerModule).toHaveBeenCalledOnce()
    expect(deps.importModule).toHaveBeenCalledOnce()
  })

  it('refreshes the generation when the plugin version changes at the same desktop revision', async () => {
    const stale = deferred<DesktopPluginActivation>()
    const routeRemoval = deferred<{ ok: boolean }>()
    const deps = dependencies(() => (
      deps.importModule.mock.calls.length === 1
        ? stale.promise
        : { mainViews: [], settingsPages: [] }
    ))
    deps.removeModule.mockImplementation(() => routeRemoval.promise)
    runtime = new DesktopPluginRuntime(deps)
    const first = namedPlugin('Fixture.Desktop')

    runtime.reconcile([first])
    await waitFor(() => expect(deps.importModule).toHaveBeenCalledOnce())
    runtime.reconcile([{ ...first, id: 'fixture.desktop', version: '2.0.0' }])
    await waitFor(() => expect(deps.removeModule).toHaveBeenCalledOnce())
    expect(deps.registerModule).toHaveBeenCalledOnce()

    routeRemoval.resolve({ ok: true })
    await waitFor(() => expect(deps.registerModule).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.get('fixture.desktop')?.version)
      .toBe('2.0.0'))

    stale.resolve({ mainViews: [], settingsPages: [] })
    await Promise.resolve()

    expect(deps.registerModule).toHaveBeenLastCalledWith(expect.objectContaining({ version: '2.0.0' }))
    expect(deps.registerModule).toHaveBeenNthCalledWith(1, expect.objectContaining({ pluginId: 'fixture.desktop' }))
    expect(useDesktopPluginRegistry.getState().generations.has('Fixture.Desktop')).toBe(false)
    expect(deps.removeModule.mock.invocationCallOrder[0])
      .toBeLessThan(deps.registerModule.mock.invocationCallOrder[1]!)
  })

  it('replaces a changed revision without waiting for the old activation disposer', async () => {
    const disposeA = vi.fn(() => new Promise<void>(() => {}))
    const disposeB = vi.fn()
    const deps = dependencies(() => ({
      mainViews: [],
      settingsPages: [],
      dispose: deps.importModule.mock.calls.length === 1 ? disposeA : disposeB
    }))
    runtime = new DesktopPluginRuntime(deps)

    runtime.reconcile([plugin(revisionA)])
    await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.get('fixture.desktop')?.revision)
      .toBe(revisionA))
    runtime.reconcile([plugin(revisionB)])
    await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.get('fixture.desktop')?.revision)
      .toBe(revisionB))

    expect(disposeA).toHaveBeenCalledOnce()
    expect(disposeB).not.toHaveBeenCalled()
    expect(deps.removeModule).toHaveBeenCalledWith({ pluginId: 'fixture.desktop', revision: revisionA })
    const styles = document.head.querySelectorAll<HTMLLinkElement>('link[data-dotcraft-desktop-plugin]')
    expect(styles).toHaveLength(1)
    expect(styles[0]?.dataset.dotcraftDesktopPluginRevision).toBe(revisionB)
  })

  it('rejects cross-kind duplicate owner-local ids without publishing partial state', async () => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => {})
    const deps = dependencies(() => ({
      mainViews: [{ id: 'duplicate', label: { default: 'Main' }, component: () => null }],
      settingsPages: [],
      commands: [{ id: 'duplicate', label: { default: 'Command' }, execute: () => {} }]
    }))
    runtime = new DesktopPluginRuntime(deps)

    runtime.reconcile([plugin()])
    await waitFor(() => expect(deps.removeModule).toHaveBeenCalledOnce())

    expect(useDesktopPluginRegistry.getState().generations.size).toBe(0)
    expect(document.head.querySelectorAll('link[data-dotcraft-desktop-plugin]')).toHaveLength(0)
    error.mockRestore()
  })

  it('rejects unknown activation contributions instead of silently ignoring them', async () => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => {})
    const deps = dependencies(() => ({
      commnads: []
    } as unknown as DesktopPluginActivation))
    runtime = new DesktopPluginRuntime(deps)

    runtime.reconcile([plugin()])
    await waitFor(() => expect(deps.removeModule).toHaveBeenCalledOnce())

    expect(useDesktopPluginRegistry.getState().generations.size).toBe(0)
    expect(useToastStore.getState().toasts.map((toast) => toast.message))
      .toContain("Desktop Plugin activation contains unknown contribution 'commnads'.")
    error.mockRestore()
  })

  it.each(['revision', 'disable'] as const)(
    'never publishes a stale async activation after %s changes',
    async (change) => {
      const stale = deferred<DesktopPluginActivation>()
      const staleDispose = vi.fn()
      const registerModule = vi.fn(async ({ pluginId, revision }: { pluginId: string; revision: string }) => ({
        entryUrl: `dotcraft-plugin://${pluginId}/${revision}/index.mjs`,
        styleUrls: [`dotcraft-plugin://${pluginId}/${revision}/plugin.css`]
      }))
      const removeModule = vi.fn().mockResolvedValue({ ok: true })
      const importModule = vi.fn(async (url: string) => ({
        activate: url.includes(revisionA)
          ? () => stale.promise
          : () => activation('current')
      }))
      runtime = new DesktopPluginRuntime({ registerModule, removeModule, importModule })

      runtime.reconcile([plugin(revisionA)])
      await waitFor(() => expect(importModule).toHaveBeenCalledOnce())

      if (change === 'revision') {
        runtime.reconcile([plugin(revisionB)])
        await waitFor(() => expect(registerModule).toHaveBeenCalledTimes(2))
        await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.get('fixture.desktop')?.revision)
          .toBe(revisionB))
      } else {
        runtime.reconcile([plugin(revisionA, false)])
      }

      stale.resolve(activation('stale', staleDispose))
      if (change === 'revision') {
        await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.get('fixture.desktop')?.revision)
          .toBe(revisionB))
      }
      await waitFor(() => expect(removeModule).toHaveBeenCalledWith({
        pluginId: 'fixture.desktop',
        revision: revisionA
      }))

      const current = useDesktopPluginRegistry.getState().generations.get('fixture.desktop')
      expect(current?.revision ?? null).toBe(change === 'revision' ? revisionB : null)
      expect(useDesktopPluginRegistry.getState().mainViews.some((entry) => entry.id === 'stale')).toBe(false)
      expect(staleDispose).toHaveBeenCalledOnce()
      expect([...document.head.querySelectorAll<HTMLLinkElement>('link[data-dotcraft-desktop-plugin]')]
        .some((link) => link.dataset.dotcraftDesktopPluginRevision === revisionA)).toBe(false)
    }
  )

  it('keeps contribution and stylesheet order stable regardless of activation timing', async () => {
    const pending = new Map<string, ReturnType<typeof deferred<DesktopPluginActivation>>>()
    const registerModule = vi.fn(async ({ pluginId, revision }: { pluginId: string; revision: string }) => ({
      entryUrl: `dotcraft-plugin://${pluginId}/${revision}/index.mjs`,
      styleUrls: [
        `dotcraft-plugin://${pluginId}/${revision}/second.css`,
        `dotcraft-plugin://${pluginId}/${revision}/first.css`
      ]
    }))
    const removeModule = vi.fn().mockResolvedValue({ ok: true })
    const importModule = vi.fn(async (url: string) => {
      const pluginId = new URL(url).hostname
      const next = deferred<DesktopPluginActivation>()
      pending.set(pluginId, next)
      return { activate: () => next.promise }
    })
    runtime = new DesktopPluginRuntime({ registerModule, removeModule, importModule })

    runtime.reconcile([namedPlugin('z.plugin'), namedPlugin('a.plugin')])
    await waitFor(() => expect(pending.size).toBe(2))
    pending.get('z.plugin')!.resolve(activation('view'))
    await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.has('z.plugin')).toBe(true))
    pending.get('a.plugin')!.resolve(activation('view'))
    await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.size).toBe(2))

    expect(useDesktopPluginRegistry.getState().mainViews.map((entry) => entry.pluginId))
      .toEqual(['a.plugin', 'z.plugin'])
    expect([...document.head.querySelectorAll<HTMLLinkElement>('link[data-dotcraft-desktop-plugin]')]
      .map((link) => link.href)).toEqual([
        `dotcraft-plugin://a.plugin/${revisionA}/second.css`,
        `dotcraft-plugin://a.plugin/${revisionA}/first.css`,
        `dotcraft-plugin://z.plugin/${revisionA}/second.css`,
        `dotcraft-plugin://z.plugin/${revisionA}/first.css`
      ])
  })

  it('cleans generation-owned notifications, toasts, and activation exactly once', async () => {
    const unsubscribe = vi.fn()
    installDesktopApiMock({
      initialTheme: 'light',
      appServer: {
        onNotificationRaw: vi.fn(() => unsubscribe),
        sendRequestRaw: vi.fn(async () => ({
          schema: { fields: [] },
          personal: {},
          workspace: {},
          value: {},
          writableScopes: []
        }))
      }
    })
    const dispose = vi.fn()
    const settingsChange = vi.fn()
    const sessionChange = vi.fn()
    const deps = dependencies((value) => {
      const host = value as DesktopPluginHost
      host.appServer.onNotification('plugin/snapshot/updated', () => {})
      host.settings.onChange(settingsChange)
      host.session.onChange(sessionChange)
      host.ui.showToast({ message: 'Owned toast' })
      return { mainViews: [], settingsPages: [], dispose }
    })
    runtime = new DesktopPluginRuntime(deps)

    runtime.reconcile([plugin()])
    await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.size).toBe(1))
    expect(useToastStore.getState().toasts.map((toast) => toast.message)).toContain('Owned toast')

    runtime.reconcile([plugin(revisionA, false)])
    await waitFor(() => expect(dispose).toHaveBeenCalledOnce())
    // The plugin's own subscription plus the Host's shared settings watcher.
    expect(unsubscribe).toHaveBeenCalledTimes(2)
    expect(settingsChange).not.toHaveBeenCalled()
    useWorkspaceProjectsStore.setState({ foregroundWorkspacePath: 'X:\\workspaces\\after-dispose' })
    expect(sessionChange).not.toHaveBeenCalled()
    expect(useToastStore.getState().toasts).toHaveLength(0)

    await runtime.stop()
    expect(dispose).toHaveBeenCalledOnce()
    expect(unsubscribe).toHaveBeenCalledTimes(2)
  })

  it('owns effects, services, and events even when activate returns nothing', async () => {
    const effectCleanup = vi.fn()
    const listener = vi.fn()
    let host!: DesktopPluginHost
    const deps = dependencies((value) => {
      host = value as DesktopPluginHost
      host.effect(() => effectCleanup)
      host.services.provide('fixture.review', { ready: true })
      host.events.on<string>('fixture.ready', listener)
      host.events.emit('fixture.ready', 'active')
    })
    runtime = new DesktopPluginRuntime(deps)

    runtime.reconcile([plugin()])
    await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.size).toBe(1))

    expect(host.services.use<{ ready: boolean }>('fixture.review')).toEqual({ ready: true })
    expect(listener).toHaveBeenCalledWith('active')

    runtime.reconcile([plugin(revisionA, false)])
    await waitFor(() => expect(effectCleanup).toHaveBeenCalledOnce())

    expect(host.services.use('fixture.review')).toBeUndefined()
    host.events.emit('fixture.ready', 'inactive')
    expect(listener).toHaveBeenCalledOnce()
  })

  it('notifies environment listeners on theme and locale changes until the generation is disposed', async () => {
    document.documentElement.lang = 'en'
    applyTheme('light')
    const changes: DesktopPluginEnvironmentSnapshot[] = []
    const deps = dependencies((value) => {
      (value as DesktopPluginHost).environment.onChange((environment) => changes.push(environment))
      return { mainViews: [], settingsPages: [] }
    })
    runtime = new DesktopPluginRuntime(deps)

    runtime.reconcile([plugin()])
    await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.size).toBe(1))

    applyTheme('dark')
    expect(changes).toEqual([{ locale: 'en', theme: 'dark', themeSeed: DEFAULT_SEEDS.dark }])

    applyTheme('dark')
    expect(changes).toHaveLength(1)

    document.documentElement.lang = 'zh-Hans'
    await waitFor(() => expect(changes).toEqual([
      { locale: 'en', theme: 'dark', themeSeed: DEFAULT_SEEDS.dark },
      { locale: 'zh-Hans', theme: 'dark', themeSeed: DEFAULT_SEEDS.dark }
    ]))

    runtime.reconcile([plugin(revisionA, false)])
    await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.size).toBe(0))

    applyTheme('light')
    document.documentElement.lang = 'ja'
    await waitFor(() => expect(document.documentElement.lang).toBe('ja'))
    expect(changes).toHaveLength(2)
  })

  it('hands plugins an app locale, so a zh-CN document resolves a zh-Hans label', async () => {
    document.documentElement.lang = 'zh-CN'
    let host: DesktopPluginHost | null = null
    const deps = dependencies((value) => {
      host = value as DesktopPluginHost
      return { mainViews: [], settingsPages: [] }
    })
    runtime = new DesktopPluginRuntime(deps)

    runtime.reconcile([plugin()])
    await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.size).toBe(1))

    const locale = host!.environment.locale
    expect(locale).toBe('zh-Hans')
    expect(resolveDesktopPluginLabel(
      { default: 'Board', translations: { 'zh-Hans': '看板' } },
      locale
    )).toBe('看板')

    document.documentElement.lang = 'en'
  })

  it('cancels an owned color picker when its generation is disabled', async () => {
    let result!: Promise<Awaited<ReturnType<DesktopPluginHost['ui']['pickColor']>>>
    const deps = dependencies((value) => {
      const host = value as DesktopPluginHost
      result = host.ui.pickColor({
        title: 'Choose accent',
        initialColor: '#4566cc',
        allowReset: true,
        defaultColor: '#4566cc'
      })
      return { mainViews: [], settingsPages: [] }
    })
    runtime = new DesktopPluginRuntime(deps)

    runtime.reconcile([plugin()])
    await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.size).toBe(1))
    runtime.reconcile([plugin(revisionA, false)])

    await expect(result).resolves.toEqual({ kind: 'cancel' })
  })

  it('rejects invalid color picker options as a development error', async () => {
    let result!: Promise<unknown>
    const deps = dependencies((value) => {
      const host = value as DesktopPluginHost
      result = host.ui.pickColor({ title: 'Choose accent', initialColor: 'invalid' })
      void result.catch(() => {})
      return { mainViews: [], settingsPages: [] }
    })
    runtime = new DesktopPluginRuntime(deps)

    runtime.reconcile([plugin()])
    await waitFor(() => expect(result).toBeDefined())
    await expect(result).rejects.toBeInstanceOf(TypeError)
  })

  it('layers plugin theme seeds by activation order and restores the layer below on disposal', async () => {
    applyTheme('dark')
    const deps = dependencies((value) => {
      const host = value as DesktopPluginHost
      host.appearance.setThemeSeedOverride({
        dark: { surface: host.plugin.id === 'second.plugin' ? '#334455' : '#112233' },
        light: { surface: host.plugin.id === 'second.plugin' ? '#ddeeff' : '#f4f5f6' }
      })
      return { mainViews: [], settingsPages: [] }
    })
    runtime = new DesktopPluginRuntime(deps)

    runtime.reconcile([namedPlugin('first.plugin')])
    await waitFor(() => expect(document.documentElement.style.getPropertyValue('--seed-surface')).toBe('#112233'))

    runtime.reconcile([namedPlugin('first.plugin'), namedPlugin('second.plugin')])
    await waitFor(() => expect(document.documentElement.style.getPropertyValue('--seed-surface')).toBe('#334455'))

    applyTheme('light')
    await waitFor(() => expect(document.documentElement.style.getPropertyValue('--seed-surface')).toBe('#ddeeff'))
    applyTheme('dark')

    runtime.reconcile([namedPlugin('first.plugin')])
    await waitFor(() => expect(document.documentElement.style.getPropertyValue('--seed-surface')).toBe('#112233'))

    runtime.reconcile([])
    await waitFor(() => expect(document.documentElement.style.getPropertyValue('--seed-surface')).toBe(''))
  })

  it('does not publish a duplicate theme event for the same appearance value', async () => {
    applyTheme('dark')
    const onThemeChanged = vi.fn()
    window.addEventListener(THEME_CHANGED_EVENT, onThemeChanged)
    const deps = dependencies((value) => {
      const host = value as DesktopPluginHost
      const override = { dark: { accent: '#4455aa' } }
      host.appearance.setThemeSeedOverride(override)
      host.appearance.setThemeSeedOverride(override)
      return { mainViews: [], settingsPages: [] }
    })
    runtime = new DesktopPluginRuntime(deps)

    runtime.reconcile([plugin()])
    await waitFor(() => expect(document.documentElement.style.getPropertyValue('--seed-accent')).toBe('#4455aa'))
    expect(onThemeChanged).toHaveBeenCalledTimes(1)
    window.removeEventListener(THEME_CHANGED_EVENT, onThemeChanged)
  })

  it('owns and clamps backdrop presentation for the active generation', async () => {
    const deps = dependencies((value) => {
      const host = value as DesktopPluginHost
      host.appearance.setBackdropPresentation({ surfaceOpacity: 1.4 })
      return { mainViews: [], settingsPages: [] }
    })
    runtime = new DesktopPluginRuntime(deps)

    runtime.reconcile([plugin()])
    await waitFor(() => expect(document.documentElement.dataset.desktopPluginBackdrop).toBe('true'))
    expect(document.documentElement.style.getPropertyValue('--desktop-plugin-backdrop-surface-opacity')).toBe('100%')

    runtime.reconcile([plugin(revisionA, false)])
    await waitFor(() => expect(document.documentElement.dataset.desktopPluginBackdrop).toBeUndefined())
    expect(document.documentElement.style.getPropertyValue('--desktop-plugin-backdrop-surface-opacity')).toBe('')
  })

  it('does not republish an unchanged backdrop contribution', async () => {
    const setProperty = vi.spyOn(document.documentElement.style, 'setProperty')
    const deps = dependencies((value) => {
      const host = value as DesktopPluginHost
      const presentation = { surfaceOpacity: 0.3 }
      host.appearance.setBackdropPresentation(presentation)
      host.appearance.setBackdropPresentation(presentation)
      return { mainViews: [], settingsPages: [] }
    })
    runtime = new DesktopPluginRuntime(deps)

    runtime.reconcile([plugin()])
    await waitFor(() => expect(document.documentElement.dataset.desktopPluginBackdrop).toBe('true'))
    expect(setProperty.mock.calls.filter(([name]) => name === '--desktop-plugin-backdrop-surface-opacity')).toHaveLength(1)
  })

  it('withdraws pending activation resources immediately on disable and revision replacement', async () => {
    const pendingGenerations: Array<{
      host: DesktopPluginHost
      effectCleanup: ReturnType<typeof vi.fn>
      listener: ReturnType<typeof vi.fn>
    }> = []
    const deps = dependencies(() => undefined)
    deps.importModule.mockImplementation(async (url: string) => ({
      activate: url.includes(revisionC)
        ? () => ({ mainViews: [], settingsPages: [] })
        : (value: unknown) => {
            const host = value as DesktopPluginHost
            const effectCleanup = vi.fn()
            const listener = vi.fn()
            host.ui.add('fixture.pending', () => null)
            host.effect(() => effectCleanup)
            host.services.provide('fixture.pending', { ready: true })
            host.events.on('fixture.pending', listener)
            pendingGenerations.push({ host, effectCleanup, listener })
            return new Promise<DesktopPluginActivation>(() => {})
          }
    }))
    runtime = new DesktopPluginRuntime(deps)

    runtime.reconcile([plugin(revisionA)])
    await waitFor(() => expect(pendingGenerations).toHaveLength(1))
    expect(useDesktopPluginRegistry.getState().surfaces).toHaveLength(1)

    runtime.reconcile([plugin(revisionA, false)])
    await waitFor(() => expect(pendingGenerations[0]?.effectCleanup).toHaveBeenCalledOnce())
    expect(useDesktopPluginRegistry.getState().surfaces).toHaveLength(0)
    expect(pendingGenerations[0]?.host.services.use('fixture.pending')).toBeUndefined()
    pendingGenerations[0]?.host.events.emit('fixture.pending', 'disabled')
    expect(pendingGenerations[0]?.listener).not.toHaveBeenCalled()
    expect(deps.removeModule).toHaveBeenCalledWith({ pluginId: 'fixture.desktop', revision: revisionA })
    const lateEffect = vi.fn()
    pendingGenerations[0]?.host.effect(lateEffect)
    pendingGenerations[0]?.host.ui.add('fixture.pending', () => null)
    expect(lateEffect).not.toHaveBeenCalled()
    expect(useDesktopPluginRegistry.getState().surfaces).toHaveLength(0)

    runtime.reconcile([plugin(revisionB)])
    await waitFor(() => expect(pendingGenerations).toHaveLength(2))
    expect(useDesktopPluginRegistry.getState().surfaces).toHaveLength(1)

    runtime.reconcile([plugin(revisionC)])
    await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.get('fixture.desktop')?.revision)
      .toBe(revisionC))
    expect(pendingGenerations[1]?.effectCleanup).toHaveBeenCalledOnce()
    expect(useDesktopPluginRegistry.getState().surfaces).toHaveLength(0)
    expect(pendingGenerations[1]?.host.services.use('fixture.pending')).toBeUndefined()
    expect(deps.removeModule).toHaveBeenCalledWith({ pluginId: 'fixture.desktop', revision: revisionB })
  })

  it('dismisses a generation-owned confirmation when the plugin is disabled', async () => {
    let settle!: (value: boolean) => void
    const confirmation = new Promise<boolean>((resolve) => { settle = resolve })
    const dismiss = vi.fn(() => settle(false))
    const trigger = vi.fn(() => Object.assign(confirmation, { dismiss }))
    ;(window as Window & { __confirmDialog?: typeof trigger }).__confirmDialog = trigger
    let result: Promise<boolean> | null = null
    const deps = dependencies((value) => {
      const host = value as DesktopPluginHost
      result = host.ui.confirm({ title: 'Remove mission?', message: 'This cannot be undone.', danger: true })
      return { mainViews: [], settingsPages: [] }
    })
    runtime = new DesktopPluginRuntime(deps)

    runtime.reconcile([plugin()])
    await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.size).toBe(1))
    expect(trigger).toHaveBeenCalledWith({ title: 'Remove mission?', message: 'This cannot be undone.', danger: true })

    runtime.reconcile([plugin(revisionA, false)])
    await waitFor(() => expect(dismiss).toHaveBeenCalledOnce())
    await expect(result).resolves.toBe(false)
  })

  it('routes internal URLs in stable plugin order and removes listeners with their generation', async () => {
    const pending = new Map<string, ReturnType<typeof deferred<DesktopPluginActivation>>>()
    const calls: string[] = []
    const deps = dependencies((value) => {
      const host = value as DesktopPluginHost
      const next = deferred<DesktopPluginActivation>()
      pending.set(host.plugin.id, next)
      host.navigation.onOpenUrl(() => {
        calls.push(host.plugin.id)
        return host.plugin.id === 'a.plugin'
      })
      return next.promise
    })
    runtime = new DesktopPluginRuntime(deps)

    runtime.reconcile([namedPlugin('z.plugin'), namedPlugin('a.plugin')])
    await waitFor(() => expect(pending.size).toBe(2))
    expect(openDesktopPluginUrl('fixture://open/item')).toBe(false)

    pending.get('z.plugin')!.resolve(activation('view'))
    pending.get('a.plugin')!.resolve(activation('view'))
    await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.size).toBe(2))
    expect(openDesktopPluginUrl('fixture://open/item')).toBe(true)
    expect(calls).toEqual(['a.plugin'])

    calls.length = 0
    runtime.reconcile([namedPlugin('z.plugin')])
    await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.has('a.plugin')).toBe(false))
    expect(openDesktopPluginUrl('fixture://open/item')).toBe(false)
    expect(calls).toEqual(['z.plugin'])

    calls.length = 0
    runtime.reconcile([])
    await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.size).toBe(0))
    expect(openDesktopPluginUrl('fixture://open/item')).toBe(false)
    expect(calls).toEqual([])
  })

  it('leaves no old or new generation when replacement activation fails', async () => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => {})
    const oldDispose = vi.fn()
    const registerModule = vi.fn(async ({ pluginId, revision }: { pluginId: string; revision: string }) => ({
      entryUrl: `dotcraft-plugin://${pluginId}/${revision}/index.mjs`,
      styleUrls: [`dotcraft-plugin://${pluginId}/${revision}/plugin.css`]
    }))
    const removeModule = vi.fn().mockResolvedValue({ ok: true })
    const importModule = vi.fn(async (url: string) => ({
      activate: url.includes(revisionA)
        ? () => activation('old', oldDispose)
        : () => { throw new Error('replacement failed') }
    }))
    runtime = new DesktopPluginRuntime({ registerModule, removeModule, importModule })

    runtime.reconcile([plugin(revisionA)])
    await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.size).toBe(1))
    runtime.reconcile([plugin(revisionB)])
    await waitFor(() => expect(removeModule).toHaveBeenCalledWith({
      pluginId: 'fixture.desktop',
      revision: revisionB
    }))

    expect(useDesktopPluginRegistry.getState().generations.size).toBe(0)
    expect(document.head.querySelectorAll('link[data-dotcraft-desktop-plugin]')).toHaveLength(0)
    expect(oldDispose).toHaveBeenCalledOnce()
    expect(removeModule).toHaveBeenCalledWith({ pluginId: 'fixture.desktop', revision: revisionA })
    error.mockRestore()
  })
})
