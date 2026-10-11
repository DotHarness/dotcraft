import { mkdtemp, readdir, rm, stat, symlink, writeFile, mkdir } from 'fs/promises'
import { tmpdir } from 'os'
import { basename, dirname, join } from 'path'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { chatFolderSlug, createChatFolder, knownChatsFolderRoots, rememberChatsFolder, resolveChatsFolderRoot } from '../chatFolders'
import { chatFolderFor, setLocalChatsFolderRootsResolver } from '../chatFolderAccess'
import { resolveDefaultChatWorkspacePath } from '../defaultChatWorkspace'

describe('chatFolderSlug', () => {
  it('joins lowercase ASCII words with dashes', () => {
    expect(chatFolderSlug('Write a release note for v2!')).toBe('write-a-release-note-for-v2')
  })

  it('falls back to new-chat when no ASCII letters or digits remain', () => {
    expect(chatFolderSlug('帮我写一份周报')).toBe('new-chat')
    expect(chatFolderSlug('   ')).toBe('new-chat')
  })

  it('suffixes Windows reserved device names', () => {
    expect(chatFolderSlug('con')).toBe('con-chat')
    expect(chatFolderSlug('COM1')).toBe('com1-chat')
    expect(chatFolderSlug('Lpt9?')).toBe('lpt9-chat')
    expect(chatFolderSlug('console')).toBe('console')
  })

  it('keeps only the ASCII runs of mixed text', () => {
    expect(chatFolderSlug('修复 bug 123')).toBe('bug-123')
  })

  it('keeps the first six words', () => {
    expect(chatFolderSlug('one two three four five six seven eight')).toBe('one-two-three-four-five-six')
  })

  it('caps the slug at 80 characters', () => {
    const slug = chatFolderSlug(`${'a'.repeat(50)} ${'b'.repeat(50)}`)
    expect(slug.length).toBeLessThanOrEqual(80)
    expect(slug).toBe(`${'a'.repeat(50)}-${'b'.repeat(29)}`)
  })
})

describe('earlier Chats folders', () => {
  afterEach(() => setLocalChatsFolderRootsResolver(() => []))

  it('keeps chats under every earlier Chats folder reachable after the setting changes', () => {
    const documents = join(tmpdir(), 'Documents')
    const first = join(tmpdir(), 'FirstChats')
    const second = join(tmpdir(), 'SecondChats')
    let settings: { chatsFolder?: string; chatsFolderHistory?: string[] } = {}
    for (const chatsFolder of [first, second, undefined]) {
      settings = { chatsFolder, chatsFolderHistory: rememberChatsFolder(settings.chatsFolderHistory, chatsFolder) }
    }
    setLocalChatsFolderRootsResolver(() => knownChatsFolderRoots(settings, documents))
    const chats = resolveDefaultChatWorkspacePath()

    for (const root of [first, second, join(documents, 'DotCraft', 'Chats')]) {
      const folder = join(root, '2026-10-11', 'plan-a-trip')
      expect(chatFolderFor(chats, join(folder, 'outputs', 'report.md'))).toBe(folder)
    }
    expect(chatFolderFor(chats, join(tmpdir(), 'Elsewhere', '2026-10-11', 'plan-a-trip'))).toBeNull()
  })
})

describe('resolveChatsFolderRoot', () => {
  it('defaults to DotCraft under Documents and honours an absolute custom folder', () => {
    const documents = join(tmpdir(), 'Documents')
    const custom = join(tmpdir(), 'MyChats')
    expect(resolveChatsFolderRoot(undefined, documents)).toBe(join(documents, 'DotCraft', 'Chats'))
    expect(resolveChatsFolderRoot('relative/path', documents)).toBe(join(documents, 'DotCraft', 'Chats'))
    expect(resolveChatsFolderRoot(custom, documents)).toBe(custom)
  })
})

describe('createChatFolder', () => {
  let root: string
  const now = new Date(2026, 9, 11, 23, 30)

  beforeEach(async () => {
    root = await mkdtemp(join(tmpdir(), 'chat-folders-'))
  })

  afterEach(async () => {
    await rm(root, { recursive: true, force: true })
  })

  it('creates a dated folder with work and outputs', async () => {
    const folder = await createChatFolder(root, 'Write a release note for v2', now)

    expect(folder).toBe(join(root, '2026-10-11', 'write-a-release-note-for-v2'))
    expect((await readdir(folder)).sort()).toEqual(['outputs', 'work'])
  })

  it('numbers later chats with the same name', async () => {
    const first = await createChatFolder(root, '你好', now)
    const second = await createChatFolder(root, '再见', now)

    expect(basename(first)).toBe('new-chat')
    expect(basename(second)).toBe('new-chat-2')
  })

  it('never reuses an existing file or symbolic link at the path', async () => {
    const dateFolder = join(root, '2026-10-11')
    await mkdir(dateFolder, { recursive: true })
    await writeFile(join(dateFolder, 'hello'), '')
    const target = await mkdtemp(join(tmpdir(), 'chat-folders-target-'))
    try {
      let linked = true
      try {
        await symlink(target, join(dateFolder, 'hello-2'), 'junction')
      } catch {
        linked = false
      }

      const folder = await createChatFolder(root, 'hello', now)

      expect(basename(folder)).toBe(linked ? 'hello-3' : 'hello-2')
      expect(await readdir(target)).toEqual([])
    } finally {
      await rm(target, { recursive: true, force: true })
    }
  })

  it('switches to a random suffix after -100', async () => {
    const dateFolder = join(root, '2026-10-11')
    await mkdir(join(dateFolder, 'busy'), { recursive: true })
    for (let suffix = 2; suffix <= 100; suffix++) {
      await mkdir(join(dateFolder, `busy-${suffix}`))
    }

    const folder = await createChatFolder(root, 'busy', now)

    expect(dirname(folder)).toBe(dateFolder)
    expect(basename(folder)).toMatch(/^busy-[0-9a-f]{6}$/)
    expect((await stat(folder)).isDirectory()).toBe(true)
  })
})
