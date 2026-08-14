import { describe, expect, it } from 'vitest'
import {
  isRemoteProjectKey,
  normalizeWorkspaceProjectKey,
  sameWorkspaceProjectKey
} from '../workspaceProjectKey'

describe('workspace project key normalization', () => {
  it('canonicalizes local workspace path variants', () => {
    expect(normalizeWorkspaceProjectKey(' X:\\fixtures\\workspace\\ ')).toBe('x:/fixtures/workspace')
    expect(normalizeWorkspaceProjectKey('x:/fixtures/./workspace')).toBe('x:/fixtures/workspace')
    expect(normalizeWorkspaceProjectKey('X:/fixtures/sibling/../workspace')).toBe('x:/fixtures/workspace')
  })

  it('keeps remote project ids stable', () => {
    expect(isRemoteProjectKey(' remote:servers:host-1:stack-1 ')).toBe(true)
    expect(normalizeWorkspaceProjectKey(' remote:servers:host-1:stack-1 ')).toBe('remote:servers:host-1:stack-1')
  })

  it('compares path variants by canonical key', () => {
    expect(sameWorkspaceProjectKey('X:\\fixtures\\workspace', 'x:/fixtures/workspace/')).toBe(true)
    expect(sameWorkspaceProjectKey('remote:manual:ws://example.test', 'remote:manual:ws://example.test')).toBe(true)
    expect(sameWorkspaceProjectKey('remote:manual:ws://example.test', 'remote:manual:ws://other.test')).toBe(false)
  })
})
