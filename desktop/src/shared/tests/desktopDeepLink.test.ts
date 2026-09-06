import { describe, expect, it } from 'vitest'
import {
  buildWorkspaceOpenDeepLink,
  findSatelliteJoinDeepLink,
  findWorkspaceOpenDeepLink,
  parseSatelliteJoinDeepLink,
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

describe('satellite join deep links', () => {
  const inviteUrl = 'http://192.168.1.20:47600/i/inv_1?x=1'
  const link = `dotcraft://satellite/join?invite=${encodeURIComponent(inviteUrl)}`

  it('keeps the original link alongside the decoded invitation url', () => {
    expect(parseSatelliteJoinDeepLink(link)).toEqual({ link, inviteUrl })
  })

  it('accepts an https invitation endpoint', () => {
    const secure = 'https://studio.example/i/inv_2'
    expect(parseSatelliteJoinDeepLink(
      `dotcraft://satellite/join?invite=${encodeURIComponent(secure)}`
    )).toEqual({
      link: `dotcraft://satellite/join?invite=${encodeURIComponent(secure)}`,
      inviteUrl: secure
    })
  })

  it('finds the first satellite link in argv and ignores workspace links', () => {
    expect(findSatelliteJoinDeepLink(['electron', '--flag', link])).toEqual({ link, inviteUrl })
    expect(findSatelliteJoinDeepLink([
      buildWorkspaceOpenDeepLink('/workspace/sample')
    ])).toBeNull()
  })

  it.each([
    'not a url',
    'codex://satellite/join?invite=http%3A%2F%2Fh%2Fi',
    'dotcraft://satellites/join?invite=http%3A%2F%2Fh%2Fi',
    'dotcraft://satellite/pair?invite=http%3A%2F%2Fh%2Fi',
    'dotcraft://satellite/join',
    'dotcraft://satellite/join?invite=',
    'dotcraft://satellite/join?invite=not-a-url',
    'dotcraft://satellite/join?invite=file%3A%2F%2F%2FC%3A%2Fsecret',
    'dotcraft://satellite/join?invite=dotcraft%3A%2F%2Fsatellite%2Fjoin',
    'dotcraft://workspace/open?path=%2Fworkspace'
  ])('rejects unsupported or incomplete links: %s', (value) => {
    expect(parseSatelliteJoinDeepLink(value)).toBeNull()
  })

  it('does not let a satellite link parse as a workspace link', () => {
    expect(parseWorkspaceOpenDeepLink(link)).toBeNull()
  })
})
