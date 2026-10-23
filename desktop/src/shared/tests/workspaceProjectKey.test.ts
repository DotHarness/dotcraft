import { describe, expect, it } from 'vitest'
import {
  isRemoteProjectKey,
  normalizeWorkspaceProjectKey,
  sameWorkspaceProjectKey
} from '../workspaceProjectKey'

describe('workspace project key normalization', () => {
  it('canonicalizes local path variants, keeps remote ids stable, and compares by canonical key', () => {
    expect(normalizeWorkspaceProjectKey(' X:\\fixtures\\workspace\\ ')).toBe('x:/fixtures/workspace')
    expect(normalizeWorkspaceProjectKey('x:/fixtures/./workspace')).toBe('x:/fixtures/workspace')
    expect(normalizeWorkspaceProjectKey('X:/fixtures/sibling/../workspace')).toBe('x:/fixtures/workspace')
    expect(isRemoteProjectKey(' remote:servers:host-1:stack-1 ')).toBe(true)
    expect(normalizeWorkspaceProjectKey(' remote:servers:host-1:stack-1 ')).toBe('remote:servers:host-1:stack-1')
    expect(sameWorkspaceProjectKey('X:\\fixtures\\workspace', 'x:/fixtures/workspace/')).toBe(true)
    expect(sameWorkspaceProjectKey('remote:manual:ws://example.test', 'remote:manual:ws://example.test')).toBe(true)
    expect(sameWorkspaceProjectKey('remote:manual:ws://example.test', 'remote:manual:ws://other.test')).toBe(false)
  })
})
