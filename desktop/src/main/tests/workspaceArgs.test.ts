import { mkdtempSync, rmSync } from 'fs'
import { tmpdir } from 'os'
import { join } from 'path'
import { describe, expect, it, vi } from 'vitest'
import {
  NO_WORKSPACE_ARG,
  resolveStartupWorkspacePath,
  resolveWorkspacePathFromArgs
} from '../workspaceArgs'

const defaultChatsPath = 'C:/Users/me/.craft/workspaces/chats'

describe('workspace argument resolution', () => {
  it('restores the last workspace unless no-workspace is passed, and lets explicit targets override both', () => {
    const settings = { lastWorkspacePath: 'E:/recent' }
    const exists = vi.fn(() => true)

    expect(resolveWorkspacePathFromArgs(settings, ['electron', 'main.js'], exists)).toBe('E:/recent')
    expect(exists).toHaveBeenCalledWith('E:/recent')
    expect(resolveWorkspacePathFromArgs(settings, ['electron', 'main.js', NO_WORKSPACE_ARG], exists)).toBeNull()
    expect(resolveWorkspacePathFromArgs(
      settings,
      ['electron', 'main.js', NO_WORKSPACE_ARG, '--workspace', 'E:/arg'],
      exists
    )).toBe('E:/arg')
    expect(resolveWorkspacePathFromArgs(
      settings,
      ['electron', 'main.js', NO_WORKSPACE_ARG, 'dotcraft://workspace/open?path=E%3A%2Flinked'],
      exists
    )).toBe('E:/linked')
  })

  it('shows welcome on first launch and restores a legacy last workspace without a foreground entry', () => {
    expect(resolveStartupWorkspacePath({}, ['electron', 'main.js'], () => false, defaultChatsPath, 'local')).toBeNull()
    expect(resolveStartupWorkspacePath(
      { lastWorkspacePath: 'E:/recent' },
      ['electron', 'main.js'],
      () => true,
      defaultChatsPath,
      'local'
    )).toBe('E:/recent')
  })

  it.each([
    ['chats', defaultChatsPath],
    ['welcome', null]
  ] as const)('restores the %s foreground entry without probing the last workspace', (entry, expected) => {
    const exists = vi.fn(() => true)

    expect(resolveStartupWorkspacePath(
      { lastForegroundEntry: entry, lastWorkspacePath: 'E:/recent' },
      ['electron', 'main.js'],
      exists,
      defaultChatsPath,
      'local'
    )).toBe(expected)
    expect(exists).not.toHaveBeenCalled()
  })

  it('lets explicit startup targets and no-workspace override the recorded foreground entry', () => {
    const resolve = (argv: string[]) => resolveStartupWorkspacePath(
      { lastForegroundEntry: 'chats', lastWorkspacePath: 'E:/recent' },
      ['electron', 'main.js', ...argv],
      () => true,
      defaultChatsPath,
      'local'
    )

    expect(resolve(['--workspace', 'E:/arg'])).toBe('E:/arg')
    expect(resolve(['dotcraft://workspace/open?path=E%3A%2Flinked'])).toBe('E:/linked')
    expect(resolve([NO_WORKSPACE_ARG])).toBeNull()
  })

  it('does not restore Chats for remote endpoint startup', () => {
    expect(resolveStartupWorkspacePath(
      { lastForegroundEntry: 'chats' },
      ['electron', 'main.js', '--remote', 'ws://127.0.0.1:9100/ws'],
      () => false,
      defaultChatsPath,
      'local'
    )).toBeNull()
  })

  it('opens the project named by a new chat link when it is a local directory', () => {
    const project = mkdtempSync(join(tmpdir(), 'dotcraft-startup-link-'))
    const link = `dotcraft://threads/new?path=${encodeURIComponent(project)}&prompt=hi`
    try {
      const settings = { lastForegroundEntry: 'workspace' as const, lastWorkspacePath: 'E:/recent' }
      expect(resolveStartupWorkspacePath(settings, ['electron', link], () => true, defaultChatsPath, 'local')).toBe(project)
      expect(resolveStartupWorkspacePath(settings, ['electron', link], () => true, defaultChatsPath, 'remote')).toBe('E:/recent')
      expect(resolveStartupWorkspacePath(
        settings,
        ['electron', `dotcraft://threads/new?path=${encodeURIComponent(join(project, 'missing'))}`],
        () => true,
        defaultChatsPath,
        'local'
      )).toBe('E:/recent')
    } finally {
      rmSync(project, { recursive: true, force: true })
    }
  })
})
