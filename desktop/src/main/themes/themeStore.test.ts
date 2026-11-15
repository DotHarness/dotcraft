import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { ThemeChoiceStore } from './themeChoiceStore'
import { ThemeStore } from './themeStore'
import { themeTool } from './themeTools'
import { handleDesktopRuntimeThreadToolCall, type AppServerRequestClient } from '../desktopRuntimeThreadTools'
import { resolveThemeChoice, validateTheme, THEME_MAX_SOURCE_BYTES, type ThemeInput } from '../../shared/themes'

const svg = '<svg xmlns="http://www.w3.org/2000/svg"><circle r="20"/></svg>'
const theme = (id = 'night-sky'): ThemeInput => ({
  id, name: 'Night sky', colorScheme: 'dark',
  backdrop: { kind: 'scene', format: 'svg', source: svg, poster: svg }
})
const withPoster = (poster: string): ThemeInput => ({ ...theme(), backdrop: { kind: 'scene', format: 'svg', source: svg, poster } })

let directory: string
let path: string
let store: ThemeStore
beforeEach(async () => {
  directory = await mkdtemp(join(tmpdir(), 'dotcraft-themes-'))
  path = join(directory, 'library.json')
  store = new ThemeStore(path)
})
afterEach(async () => { await rm(directory, { recursive: true, force: true }) })

describe('theme library', () => {
  it('persists theme identity and author independently of the authoring thread', async () => {
    const saved = await store.save(theme(), 'thread-one')
    const reopened = new ThemeStore(path)
    expect(await reopened.list()).toEqual([saved])
    const updated = await reopened.save({ ...theme(), name: 'Quieter night', expectedRevision: saved.revision })
    expect(updated).toMatchObject({ id: saved.id, revision: 2, authorThreadId: 'thread-one', name: 'Quieter night' })
  })

  it('keeps an added image applied everywhere after the app restarts', async () => {
    const image = `data:image/jpeg;base64,${Buffer.alloc(3 * 1024 * 1024, 7).toString('base64')}`
    const choicesPath = join(directory, 'choices.json')
    const saved = await store.save({ id: 'image-beach', name: 'Beach', colorScheme: 'dark', backdrop: { kind: 'image', image, fit: 'cover' } })
    await store.save({ id: 'image-beach', name: 'Beach', colorScheme: 'dark', backdrop: { kind: 'image', image, fit: 'tile' }, expectedRevision: saved.revision })
    await new ThemeChoiceStore(choicesPath).set({ kind: 'everywhere' }, { dark: { kind: 'theme', id: 'image-beach' } })

    const themes = await new ThemeStore(path).list()
    const choices = await new ThemeChoiceStore(choicesPath).get()
    expect(themes).toMatchObject([{ id: 'image-beach', backdrop: { image, fit: 'tile' } }])
    const resolved = resolveThemeChoice(choices, 'dark', { threadId: null }, (choice) => choice.kind !== 'theme' || themes.some((theme) => theme.id === choice.id))
    expect(resolved).toEqual({ choice: { kind: 'theme', id: 'image-beach' }, scope: 'everywhere' })
  })

  it('serializes competing replacements and leaves the winner intact', async () => {
    await store.save(theme())
    const changes = await Promise.allSettled([
      store.save({ ...theme(), name: 'First', expectedRevision: 1 }),
      store.save({ ...theme(), name: 'Second', expectedRevision: 1 })
    ])
    expect(changes.map((r) => r.status)).toEqual(['fulfilled', 'rejected'])
    expect(await store.list()).toMatchObject([{ name: 'First', revision: 2 }])
  })

  it('does not overwrite corrupt storage or replace a valid theme with invalid input', async () => {
    await store.save(theme())
    const original = await readFile(path, 'utf8')
    const oversized = { ...theme(), backdrop: { kind: 'scene', format: 'svg', source: `<svg>${'x'.repeat(THEME_MAX_SOURCE_BYTES)}</svg>`, poster: svg }, expectedRevision: 1 }
    await expect(store.save(oversized)).rejects.toThrow('exceeds')
    expect(await readFile(path, 'utf8')).toBe(original)
    await writeFile(path, '{not json')
    await expect(store.save(theme('new'))).rejects.toThrow()
    expect(await readFile(path, 'utf8')).toBe('{not json')
  })

  it('keeps choices out of Agent save and routes preview to the calling thread', async () => {
    const previews: unknown[] = []
    const host = { store, preview: (themeId: string, threadId: string) => { previews.push({ themeId, threadId }) } }
    const client = { sendRequest: async () => { throw new Error('Theme writes do not call the AppServer.') } } as AppServerRequestClient
    const saved = await handleDesktopRuntimeThreadToolCall(client, {
      namespace: 'desktop', tool: 'Theme', threadId: 'author', arguments: { action: 'save', theme: theme() }
    }, 'workspace', { themes: host })
    expect(saved?.success).toBe(true)
    expect(saved?.structuredContent).toMatchObject({ applied: false })
    expect(previews).toEqual([])
    const listing = await themeTool(host, { action: 'list' })
    expect(JSON.stringify(listing)).not.toContain('<svg')
    await themeTool(host, { action: 'preview', id: 'night-sky' }, 'author')
    expect(previews).toEqual([{ themeId: 'night-sky', threadId: 'author' }])
    const read = await themeTool(host, { action: 'read', id: 'night-sky' })
    expect(read.structuredContent).toMatchObject({ theme: { backdrop: { source: svg }, revision: 1 } })
    await store.remove('night-sky')
    expect(await store.list()).toEqual([])
  })

  it('keeps the picture when an edit of an image theme leaves it out', async () => {
    const image = `data:image/png;base64,${Buffer.alloc(64, 7).toString('base64')}`
    const saved = await store.save({ id: 'beach', name: 'Beach', colorScheme: 'dark', backdrop: { kind: 'image', image, fit: 'cover' } })
    const host = { store, preview: () => {} }
    const edited = await themeTool(host, { action: 'save', theme: { id: 'beach', name: 'Beach', colorScheme: 'dark', tint: '#102030', backdrop: { kind: 'image', fit: 'tile' }, expectedRevision: saved.revision } }, 'author')
    expect(edited.success).toBe(true)
    expect(await store.list()).toMatchObject([{ id: 'beach', tint: '#102030', backdrop: { kind: 'image', image, fit: 'tile' } }])
  })

  it('rejects authorless writes and stale edits with actionable tool errors', async () => {
    const host = { store, preview: () => {} }
    expect(await themeTool(host, { action: 'save', theme: theme() })).toMatchObject({ success: false, errorCode: 'InvalidParams' })
    await store.save(theme())
    expect(await themeTool(host, { action: 'save', theme: theme() }, 'author')).toMatchObject({ success: false, errorMessage: expect.stringContaining('revision conflict') })
    expect(await themeTool(undefined, { action: 'list' })).toMatchObject({ success: false, errorCode: 'ThemeUnavailable' })
  })
})

describe('theme validation', () => {
  it.each(['<svg><animate attributeName="x"/></svg>', '<svg><style>@keyframes move{}</style></svg>', '<svg onload="fetch(1)"></svg>', '<svg><foreignObject/></svg>'])(
    'rejects an active poster: %s', (poster) => { expect(() => validateTheme(withPoster(poster))).toThrow('static') }
  )

  it('refuses reserved ids and unsafe host theme values', () => {
    expect(() => validateTheme(theme('builtin-night'))).toThrow('builtin-')
    expect(() => validateTheme({ ...theme(), colors: { surface: 'url(https://example.com)', ink: '#ffffff', accent: '#abcdef', contrast: 50 } })).toThrow('hex')
    expect(() => validateTheme({ ...theme(), tint: 'var(--bg-primary)' })).toThrow('tint')
    expect(() => validateTheme({ id: 'sky', name: 'Sky', colorScheme: 'dark', backdrop: { kind: 'gradient', stops: ['#000000', 'url(https://example.com)'] } })).toThrow('stops')
    expect(() => validateTheme({ id: 'photo', name: 'Photo', colorScheme: 'dark', backdrop: { kind: 'image', image: 'https://example.com/a.png' } })).toThrow('data URL')
  })

  it('accepts a color-only theme and rejects ink that is unreadable on its surface', () => {
    const colors = { surface: '#f7f1e3', ink: '#2b2620', accent: '#8a5a2b', contrast: 50 }
    expect(validateTheme({ id: 'paper', name: 'Paper', colorScheme: 'light', colors })).toMatchObject({ colors })
    expect(() => validateTheme({ id: 'paper', name: 'Paper', colorScheme: 'light', colors: { ...colors, ink: '#d8d2c4' } })).toThrow('4.5:1')
    expect(() => validateTheme({ id: 'empty', name: 'Empty', colorScheme: 'light' })).toThrow('colors, a backdrop')
    expect(() => validateTheme({ id: 'paper', name: 'Paper', colorScheme: 'light', colors, tint: '#ffffff' })).toThrow('backdrop')
  })
})
