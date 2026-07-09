import { describe, expect, it, vi } from 'vitest'
import {
  NO_WORKSPACE_ARG,
  resolveStartupWorkspacePath,
  resolveWorkspacePathFromArgs
} from '../workspaceArgs'

const defaultChatsPath = 'C:/Users/me/.craft/workspaces/chats'

describe('workspace argument resolution', () => {
  it('uses the last workspace when no explicit workspace mode is provided', () => {
    const exists = vi.fn(() => true)

    expect(resolveWorkspacePathFromArgs(
      { lastWorkspacePath: 'E:/recent' },
      ['electron', 'main.js'],
      exists
    )).toBe('E:/recent')
    expect(exists).toHaveBeenCalledWith('E:/recent')
  })

  it('suppresses last workspace restoration with no-workspace', () => {
    expect(resolveWorkspacePathFromArgs(
      { lastWorkspacePath: 'E:/recent' },
      ['electron', 'main.js', NO_WORKSPACE_ARG],
      () => true
    )).toBeNull()
  })

  it('lets explicit workspace override no-workspace', () => {
    expect(resolveWorkspacePathFromArgs(
      { lastWorkspacePath: 'E:/recent' },
      ['electron', 'main.js', NO_WORKSPACE_ARG, '--workspace', 'E:/arg'],
      () => true
    )).toBe('E:/arg')
  })

  it('lets workspace deep links override no-workspace', () => {
    expect(resolveWorkspacePathFromArgs(
      { lastWorkspacePath: 'E:/recent' },
      ['electron', 'main.js', NO_WORKSPACE_ARG, 'dotcraft://workspace/open?path=E%3A%2Flinked'],
      () => true
    )).toBe('E:/linked')
  })

  it('shows welcome on first launch with no foreground entry or legacy workspace', () => {
    expect(resolveStartupWorkspacePath(
      {},
      ['electron', 'main.js'],
      () => false,
      defaultChatsPath,
      'local'
    )).toBeNull()
  })

  it('restores a legacy last workspace when no foreground entry has been recorded yet', () => {
    const exists = vi.fn(() => true)

    expect(resolveStartupWorkspacePath(
      { lastWorkspacePath: 'E:/recent' },
      ['electron', 'main.js'],
      exists,
      defaultChatsPath,
      'local'
    )).toBe('E:/recent')
    expect(exists).toHaveBeenCalledWith('E:/recent')
  })

  it('restores Chats when it was the last foreground entry', () => {
    const exists = vi.fn(() => true)

    expect(resolveStartupWorkspacePath(
      { lastForegroundEntry: 'chats', lastWorkspacePath: 'E:/recent' },
      ['electron', 'main.js'],
      exists,
      defaultChatsPath,
      'local'
    )).toBe(defaultChatsPath)
    expect(exists).not.toHaveBeenCalled()
  })

  it('keeps the welcome chooser when it was the last foreground entry', () => {
    const exists = vi.fn(() => true)

    expect(resolveStartupWorkspacePath(
      { lastForegroundEntry: 'welcome', lastWorkspacePath: 'E:/recent' },
      ['electron', 'main.js'],
      exists,
      defaultChatsPath,
      'local'
    )).toBeNull()
    expect(exists).not.toHaveBeenCalled()
  })

  it('lets explicit startup targets override the recorded foreground entry', () => {
    expect(resolveStartupWorkspacePath(
      { lastForegroundEntry: 'welcome', lastWorkspacePath: 'E:/recent' },
      ['electron', 'main.js', '--workspace', 'E:/arg'],
      () => true,
      defaultChatsPath,
      'local'
    )).toBe('E:/arg')

    expect(resolveStartupWorkspacePath(
      { lastForegroundEntry: 'welcome', lastWorkspacePath: 'E:/recent' },
      ['electron', 'main.js', 'dotcraft://workspace/open?path=E%3A%2Flinked'],
      () => true,
      defaultChatsPath,
      'local'
    )).toBe('E:/linked')
  })

  it('lets no-workspace suppress the recorded foreground entry', () => {
    expect(resolveStartupWorkspacePath(
      { lastForegroundEntry: 'chats', lastWorkspacePath: 'E:/recent' },
      ['electron', 'main.js', NO_WORKSPACE_ARG],
      () => true,
      defaultChatsPath,
      'local'
    )).toBeNull()
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
})
