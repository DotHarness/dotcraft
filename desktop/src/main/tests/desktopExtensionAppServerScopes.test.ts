import { describe, expect, it } from 'vitest'
import { desktopExtensionAppServerMethodAllowed } from '../desktopExtensionGrants'

describe('desktopExtensionAppServerMethodAllowed', () => {
  it('matches a trailing wildcard prefix and exact methods', () => {
    const scopes = ['agent/profiles/*', 'thread/start']
    expect(desktopExtensionAppServerMethodAllowed(scopes, 'agent/profiles/list')).toBe(true)
    expect(desktopExtensionAppServerMethodAllowed(scopes, 'agent/profiles/upsert')).toBe(true)
    expect(desktopExtensionAppServerMethodAllowed(scopes, 'thread/start')).toBe(true)
  })

  it('rejects methods outside the declared scopes', () => {
    const scopes = ['agent/profiles/*']
    expect(desktopExtensionAppServerMethodAllowed(scopes, 'thread/start')).toBe(false)
    expect(desktopExtensionAppServerMethodAllowed(scopes, 'workspace/delete')).toBe(false)
    // The wildcard prefix itself (no subpath) does not match.
    expect(desktopExtensionAppServerMethodAllowed(scopes, 'agent/profiles')).toBe(false)
  })

  it('is default-closed for an empty allow-list', () => {
    expect(desktopExtensionAppServerMethodAllowed([], 'agent/profiles/list')).toBe(false)
  })

  it('trims the method and ignores blank scopes', () => {
    expect(desktopExtensionAppServerMethodAllowed(['agent/profiles/*'], '  agent/profiles/list  ')).toBe(true)
    expect(desktopExtensionAppServerMethodAllowed([''], 'anything')).toBe(false)
    expect(desktopExtensionAppServerMethodAllowed(['agent/profiles/*'], '   ')).toBe(false)
  })
})
