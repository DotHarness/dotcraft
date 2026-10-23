import { describe, expect, it } from 'vitest'
import {
  buildWorkspaceOpenDeepLink,
  findNewThreadDeepLink,
  findSatelliteJoinDeepLink,
  findWorkspaceOpenDeepLink,
  parseNewThreadDeepLink,
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

  it('parses workspace-only links and finds the link in argv', () => {
    expect(parseWorkspaceOpenDeepLink(buildWorkspaceOpenDeepLink('/workspace/sample'))).toEqual({
      workspacePath: '/workspace/sample'
    })
    expect(findWorkspaceOpenDeepLink([
      'electron',
      '--flag',
      buildWorkspaceOpenDeepLink('/workspace/sample', 'thread-1')
    ])).toEqual({ workspacePath: '/workspace/sample', threadId: 'thread-1' })
  })

  it('rejects unsupported or incomplete links', () => {
    for (const value of [
      'not a url',
      'codex://workspace/open?path=%2Fworkspace',
      'dotcraft://threads/open?path=%2Fworkspace',
      'dotcraft://workspace/other?path=%2Fworkspace',
      'dotcraft://workspace/open'
    ]) {
      expect(parseWorkspaceOpenDeepLink(value), value).toBeNull()
    }
  })
})

describe('satellite join deep links', () => {
  const inviteUrl = 'http://192.168.1.20:47600/i/inv_1?x=1'
  const link = `dotcraft://satellite/join?invite=${encodeURIComponent(inviteUrl)}`

  it('keeps the original link alongside the decoded http or https invitation url', () => {
    expect(parseSatelliteJoinDeepLink(link)).toEqual({ link, inviteUrl })
    const secure = 'https://studio.example/i/inv_2'
    const secureLink = `dotcraft://satellite/join?invite=${encodeURIComponent(secure)}`
    expect(parseSatelliteJoinDeepLink(secureLink)).toEqual({ link: secureLink, inviteUrl: secure })
  })

  it('finds the first satellite link in argv and does not cross-parse workspace links', () => {
    expect(findSatelliteJoinDeepLink(['electron', '--flag', link])).toEqual({ link, inviteUrl })
    expect(findSatelliteJoinDeepLink([buildWorkspaceOpenDeepLink('/workspace/sample')])).toBeNull()
    expect(parseWorkspaceOpenDeepLink(link)).toBeNull()
  })

  it('rejects unsupported, incomplete, or non-http invitation links', () => {
    for (const value of [
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
    ]) {
      expect(parseSatelliteJoinDeepLink(value), value).toBeNull()
    }
  })
})

describe('new thread deep links', () => {
  it('decodes the project path and prompt, treating both as optional', () => {
    const link = `dotcraft://threads/new?path=${encodeURIComponent('/Users/me/工作区/a b')}&prompt=${encodeURIComponent('Fix #12 & ship')}`

    expect(parseNewThreadDeepLink(link)).toEqual({ workspacePath: '/Users/me/工作区/a b', prompt: 'Fix #12 & ship' })
    expect(parseNewThreadDeepLink('dotcraft://threads/new')).toEqual({})
    expect(parseNewThreadDeepLink('dotcraft://threads/new/?path=%20&prompt=%20%20')).toEqual({})
    expect(parseNewThreadDeepLink('dotcraft://threads/new?prompt=hi')).toEqual({ prompt: 'hi' })
  })

  it('finds the link in argv without matching other DotCraft links', () => {
    const workspaceLink = buildWorkspaceOpenDeepLink('/workspace/sample')

    expect(findNewThreadDeepLink(['electron', workspaceLink, 'dotcraft://threads/new?path=%2Fp'])).toEqual({ workspacePath: '/p' })
    expect(parseNewThreadDeepLink(workspaceLink)).toBeNull()
    expect(parseWorkspaceOpenDeepLink('dotcraft://threads/new?path=%2Fp')).toBeNull()
  })

  it('rejects unsupported links', () => {
    for (const value of [
      'not a url',
      'https://threads/new?path=%2Fp',
      'dotcraft://threads/open?path=%2Fp',
      'dotcraft://thread/new?path=%2Fp'
    ]) {
      expect(parseNewThreadDeepLink(value), value).toBeNull()
    }
  })
})
