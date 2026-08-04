import { describe, expect, it } from 'vitest'
import {
  buildWorkspaceOpenDeepLink,
  findWorkspaceOpenDeepLink,
  parseWorkspaceOpenDeepLink
} from '../desktopDeepLink'

describe('desktop deep links', () => {
  it('round-trips a workspace and thread with characters that require encoding', () => {
    const workspacePath = 'C:\\工作区\\sample project#1?draft'
    const threadId = 'thread/α?1'

    const value = buildWorkspaceOpenDeepLink(workspacePath, threadId)

    expect(value).toContain('dotcraft://workspace/open?')
    expect(value).not.toContain('sample project')
    expect(parseWorkspaceOpenDeepLink(value)).toEqual({ workspacePath, threadId })
  })

  it('keeps workspace-only links compatible', () => {
    const value = buildWorkspaceOpenDeepLink('/workspace/sample')

    expect(parseWorkspaceOpenDeepLink(value)).toEqual({
      workspacePath: '/workspace/sample'
    })
  })

  it('finds the first valid workspace deep link in argv', () => {
    const value = buildWorkspaceOpenDeepLink('/workspace/sample', 'thread-1')

    expect(findWorkspaceOpenDeepLink(['electron', '--flag', value])).toEqual({
      workspacePath: '/workspace/sample',
      threadId: 'thread-1'
    })
  })

  it.each([
    'not a url',
    'codex://workspace/open?path=%2Fworkspace',
    'dotcraft://threads/open?path=%2Fworkspace',
    'dotcraft://workspace/other?path=%2Fworkspace',
    'dotcraft://workspace/open'
  ])('rejects unsupported or incomplete links: %s', (value) => {
    expect(parseWorkspaceOpenDeepLink(value)).toBeNull()
  })
})
