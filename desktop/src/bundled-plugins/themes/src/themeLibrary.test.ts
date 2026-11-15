import type { DesktopPluginHost, DesktopPluginSettings } from '@dotcraft/plugin'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  patchThemeChoices,
  type Theme, type ThemeChoices, type ThemePreviewRequest, type ThemesApi
} from '../../../shared/themes'

const svg = '<svg></svg>'
const sky: Theme = {
  id: 'test-sky', name: 'Sky', colorScheme: 'dark', revision: 1, updatedAt: '2026-11-15',
  backdrop: { kind: 'gradient', stops: ['#000000', '#101020'], angle: 180, shape: 'linear' },
  tint: '#101020', pair: 'test-day'
}
const day: Theme = { id: 'test-day', name: 'Day', colorScheme: 'light', revision: 1, updatedAt: '2026-11-15', colors: { surface: '#ffffff', ink: '#111111', accent: '#3355aa', contrast: 50 }, pair: 'test-sky' }
const scene: Theme = { id: 'test-scene', name: 'Scene', colorScheme: 'dark', revision: 1, updatedAt: '2026-11-15', backdrop: { kind: 'scene', format: 'svg', source: svg, poster: svg } }

async function setup(initial: Theme[] = [sky, day, scene]) {
  const settings = await import('./settings')
  const values = { enabled: true, animate: true, blur: 12, surfaceOpacity: 0.5 }
  const read = () => ({ value: values, schema: { fields: [] }, writableScopes: ['personal'], personal: values, workspace: {} })
  await settings.initializeSettings({ get: async () => read(), mutate: async () => read(), onChange: () => () => {} } as unknown as DesktopPluginSettings)
  let changed = () => {}
  let preview = (_request: ThemePreviewRequest) => {}
  const sessions: ((value: { threadId: string; workspacePath: string }) => void)[] = []
  let stored: ThemeChoices = { everywhere: {}, chats: {} }
  const setChoice = vi.fn<ThemesApi['setChoice']>(async (scope, patch) => (stored = patchThemeChoices(stored, scope, patch, 1)))
  const api: ThemesApi = {
    list: vi.fn(async () => initial), save: vi.fn(), remove: vi.fn(),
    choices: async () => stored, setChoice, onChoicesChanged: () => vi.fn(),
    onChanged: listener => { changed = listener; return vi.fn() },
    onPreview: listener => { preview = listener; return vi.fn() }
  }
  vi.stubGlobal('window', { api: { themes: api } })
  const request = vi.fn(async (method: string) => method === 'thread/start' ? { thread: { id: 'created' } } : {})
  const host = {
    environment: { locale: 'en', theme: 'dark' },
    session: { threadId: 'foreground', workspacePath: 'C:/project', onChange: (listener: (value: { threadId: string; workspacePath: string }) => void) => { sessions.push(listener); return vi.fn() } },
    navigation: { openSettingsPage: vi.fn(), openThread: vi.fn(async () => {}), startChat: vi.fn(async () => {}) },
    appServer: { request }
  } as unknown as DesktopPluginHost
  const choices = await import('./choices')
  const stopChoices = choices.initializeChoices(host)
  await vi.waitFor(() => expect(choices.getChoices().choices).not.toBeNull())
  const library = await import('./themeLibrary')
  const stopLibrary = library.initializeThemeLibrary(host)
  const dispose = (): void => { stopLibrary(); stopChoices() }
  await vi.waitFor(() => expect(library.getThemeLibrary().loading).toBe(false))
  return {
    library, choices, setChoice, stored: () => stored, api, host, request, dispose, changed: () => changed(),
    preview: (id: string, threadId: string) => preview({ themeId: id, threadId }),
    session: (threadId: string) => { Object.assign(host.session, { threadId }); for (const listener of sessions) listener({ threadId, workspacePath: 'C:/project' }) }
  }
}

describe('theme library', () => {
  beforeEach(() => { vi.resetModules(); vi.unstubAllGlobals() })

  it('keeps a preview temporary, then applies a theme and its pair to the chosen scope only', async () => {
    const { library, setChoice, stored, dispose } = await setup()
    library.previewTheme(sky.id)
    expect(library.activeTheme('light')?.id).toBe(sky.id)
    expect(library.activeGlass('dark')).toEqual({ blur: 12, opacity: 0.5, tint: '#101020' })
    expect(setChoice).not.toHaveBeenCalled()
    library.cancelThemePreview()
    expect(library.activeTheme('dark')).toBeNull()

    library.applyTheme(sky, { kind: 'chat', threadId: 'foreground' })
    await vi.waitFor(() => expect(setChoice).toHaveBeenCalledOnce())
    expect(stored().chats.foreground).toEqual({
      dark: { kind: 'theme', id: sky.id },
      light: { kind: 'theme', id: day.id },
      at: 1
    })
    expect(stored().everywhere).toEqual({})
    expect(library.activeTheme('dark')?.id).toBe(sky.id)
    expect(library.activeTheme('light')?.id).toBe(day.id)
    expect(library.activeGlass('light')).toBeNull()
    dispose()
  })

  it('draws the narrowest scope that sets the appearance and lets No theme stop there', async () => {
    const { library, choices, session, dispose } = await setup()
    choices.setChoice({ kind: 'everywhere' }, { dark: { kind: 'theme', id: sky.id } })
    expect(library.activeTheme('dark')?.id).toBe(sky.id)
    choices.setChoice({ kind: 'chat', threadId: 'foreground' }, { dark: { kind: 'none' } })
    expect(library.activeTheme('dark')).toBeNull()
    session('another')
    expect(library.activeTheme('dark')?.id).toBe(sky.id)
    dispose()
  })

  it('does not draw a theme in a slot of the other appearance', async () => {
    const { library, choices, dispose } = await setup()
    choices.setChoice({ kind: 'everywhere' }, { light: { kind: 'theme', id: sky.id } })
    expect(library.activeTheme('light')).toBeNull()
    dispose()
  })

  it('waits for a scene to report before its backdrop counts as ready', async () => {
    const { library, dispose } = await setup()
    expect(library.isBackdropReady(sky)).toBe(true)
    expect(library.isBackdropReady(scene)).toBe(false)
    library.reportBackdropReady(scene, true)
    expect(library.isBackdropReady(scene)).toBe(true)
    library.reportBackdropReady(scene, false)
    expect(library.isBackdropReady(scene)).toBe(false)
    dispose()
  })

  it('ignores background thread previews and ends a preview when the foreground changes', async () => {
    const { library, preview, session, dispose } = await setup()
    preview(sky.id, 'background')
    expect(library.getThemeLibrary().preview).toBeNull()
    preview(sky.id, 'foreground')
    await vi.waitFor(() => expect(library.getThemeLibrary().preview?.id).toBe(sky.id))
    expect((await import('./composerPicker')).isComposerPickerOpen()).toBe(true)
    session('another')
    expect(library.getThemeLibrary().preview).toBeNull()
    dispose()
  })

  it('resolves a newly saved id after its library update and treats a deleted theme as unset', async () => {
    const { library, api, choices, changed, dispose } = await setup([])
    vi.mocked(api.list).mockResolvedValue([sky])
    choices.setChoice({ kind: 'chat', threadId: 'foreground' }, { dark: { kind: 'theme', id: sky.id } })
    expect(choices.resolvedChoice('dark')).toEqual({ kind: 'none' })
    changed()
    await vi.waitFor(() => expect(choices.resolvedChoice('dark')).toEqual({ kind: 'theme', id: sky.id }))
    vi.mocked(api.list).mockResolvedValue([])
    changed()
    await vi.waitFor(() => expect(choices.resolvedChoice('dark')).toEqual({ kind: 'none' }))
    dispose()
  })

  it('does not publish an in-flight refresh after disposal', async () => {
    const { library, api, changed, dispose } = await setup()
    let resolve!: (themes: Theme[]) => void
    vi.mocked(api.list).mockImplementation(() => new Promise(done => { resolve = done }))
    changed()
    dispose()
    resolve([{ ...sky, name: 'Stale', revision: 2 }])
    await Promise.resolve()
    expect(library.themeById(sky.id)?.name).toBe('Sky')
    expect(library.getThemeLibrary().loading).toBe(false)
  })

  it('stages an unsent request with the themes skill and replaces a deleted authoring conversation', async () => {
    const { library, host, request, dispose } = await setup()
    request.mockImplementation(async method => {
      if (method === 'thread/read') throw new Error('Thread not found')
      return {}
    })

    await library.startThemeAuthoring(host)
    await library.startThemeAuthoring(host, { ...sky, authorThreadId: 'deleted-author' })

    expect(host.navigation.startChat).toHaveBeenNthCalledWith(1, { prompt: expect.any(String), skill: 'themes' })
    expect(host.navigation.startChat).toHaveBeenNthCalledWith(2, { prompt: expect.stringContaining('Sky'), skill: 'themes' })
    expect(request).not.toHaveBeenCalledWith('thread/start', expect.anything())
    expect(request).not.toHaveBeenCalledWith('turn/start', expect.anything())
    expect(host.navigation.openThread).not.toHaveBeenCalled()
    dispose()
  })

  it('continues an existing authoring thread without creating a duplicate', async () => {
    const { library, host, dispose } = await setup()
    await library.startThemeAuthoring(host, { ...sky, authorThreadId: 'author' })
    expect(host.appServer.request).toHaveBeenCalledWith('thread/read', { threadId: 'author' })
    expect(host.navigation.openThread).toHaveBeenCalledWith('author', 'C:/project')
    expect(host.appServer.request).toHaveBeenCalledTimes(1)
    expect(host.navigation.startChat).not.toHaveBeenCalled()
    dispose()
  })
})
