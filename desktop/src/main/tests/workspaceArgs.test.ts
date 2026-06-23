import { describe, expect, it, vi } from 'vitest'
import {
  NO_WORKSPACE_ARG,
  resolveWorkspacePathFromArgs,
  shouldOpenDefaultChatWorkspaceOnStartup
} from '../workspaceArgs'

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

  it('uses the default chat workspace only for ordinary local startup with no workspace', () => {
    expect(shouldOpenDefaultChatWorkspaceOnStartup(
      {},
      ['electron', 'main.js'],
      () => false,
      'local'
    )).toBe(true)

    expect(shouldOpenDefaultChatWorkspaceOnStartup(
      { lastWorkspacePath: 'E:/recent' },
      ['electron', 'main.js'],
      () => true,
      'local'
    )).toBe(false)

    expect(shouldOpenDefaultChatWorkspaceOnStartup(
      { lastWorkspacePath: 'E:/recent' },
      ['electron', 'main.js', NO_WORKSPACE_ARG],
      () => true,
      'local'
    )).toBe(false)

    expect(shouldOpenDefaultChatWorkspaceOnStartup(
      {},
      ['electron', 'main.js'],
      () => false,
      'remote'
    )).toBe(false)

    expect(shouldOpenDefaultChatWorkspaceOnStartup(
      {},
      ['electron', 'main.js', '--remote', 'ws://127.0.0.1:9100/ws'],
      () => false,
      'local'
    )).toBe(false)
  })
})
