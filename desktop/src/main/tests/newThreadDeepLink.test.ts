import { mkdtempSync, rmSync, writeFileSync } from 'fs'
import { tmpdir } from 'os'
import { join } from 'path'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { openNewThreadDeepLink, type NewThreadDeepLinkHost } from '../newThreadDeepLink'

let root = ''

beforeEach(() => {
  root = mkdtempSync(join(tmpdir(), 'dotcraft-new-thread-link-'))
})

afterEach(() => {
  rmSync(root, { recursive: true, force: true })
})

function host(overrides: Partial<NewThreadDeepLinkHost> = {}): NewThreadDeepLinkHost & {
  calls: string[]
} {
  const calls: string[] = []
  return {
    calls,
    canOpenLocalProject: () => true,
    isForegroundWorkspace: () => false,
    switchWorkspace: vi.fn(async (path: string) => { calls.push(`switch:${path}`) }),
    openNewChat: vi.fn((prompt: string | undefined) => { calls.push(`new-chat:${prompt ?? ''}`) }),
    ...overrides
  }
}

describe('new thread deep link', () => {
  it('selects an existing directory before opening an unsent new chat', async () => {
    const target = host()

    await openNewThreadDeepLink({ workspacePath: root, prompt: 'hi' }, target)

    expect(target.calls).toEqual([`switch:${root}`, 'new-chat:hi'])
  })

  it('keeps the current project for a missing directory, a file, or a relative path', async () => {
    const file = join(root, 'notes.txt')
    writeFileSync(file, '')

    for (const path of [join(root, 'missing'), file, 'relative/project']) {
      const target = host()
      await openNewThreadDeepLink({ workspacePath: path, prompt: 'hi' }, target)
      expect(target.calls, path).toEqual(['new-chat:hi'])
    }
  })

  it('does not switch to the foreground project or while local projects are unavailable', async () => {
    const foreground = host({ isForegroundWorkspace: () => true })
    const remote = host({ canOpenLocalProject: () => false })

    await openNewThreadDeepLink({ workspacePath: root }, foreground)
    await openNewThreadDeepLink({ workspacePath: root }, remote)

    expect(foreground.calls).toEqual(['new-chat:'])
    expect(remote.calls).toEqual(['new-chat:'])
  })
})
