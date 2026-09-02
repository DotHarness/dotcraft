import { describe, expect, it } from 'vitest'
import { findSubAgentChild, type SubAgentLookupSources } from '../utils/subAgentIdentity'
import { makeSubAgent, makeSubAgentThread } from './subAgentFixtures'

function lookup(): SubAgentLookupSources {
  return {
    sourceThreadId: 'parent-B', threadList: [],
    childrenByParent: new Map([
      ['parent-A', [makeSubAgent({ childThreadId: 'child-A', parentThreadId: 'parent-A' })]],
      ['parent-B', [makeSubAgent()]]
    ])
  }
}

describe('subagent identity scope', () => {
  it('resolves a real path-only result within its direct parent', () => {
    expect(findSubAgentChild(lookup(), null, '/root/review_core')?.childThreadId).toBe('child-B')
  })
  it('gives an explicit ID priority over a different matching path', () => {
    const source = lookup()
    source.childrenByParent.get('parent-B')!.push(makeSubAgent({ childThreadId: 'explicit', agentPath: '/root/other' }))
    expect(findSubAgentChild(source, 'explicit', '/root/review_core')?.childThreadId).toBe('explicit')
    expect(findSubAgentChild(source, 'missing', '/root/review_core')).toBeNull()
    expect(findSubAgentChild(source, 'child-A', '/root/review_core')).toBeNull()
  })
  it('does not search an unrelated parent when the source has no cached children', () => {
    const source = lookup()
    source.childrenByParent.delete('parent-B')
    expect(findSubAgentChild(source, null, '/root/review_core', 'tree')).toBeNull()
  })
  it('allows control targets across parents only with matching root ancestry', () => {
    const source = lookup()
    source.threadList = [makeSubAgentThread('parent-B', { kind: 'subagent', subAgent: { rootThreadId: 'root-B', agentPath: '/root/reviewer' } })]
    const sibling = makeSubAgent({ childThreadId: 'sibling', parentThreadId: 'root-B', agentPath: '/root/sibling',
      threadSummary: makeSubAgentThread('sibling', { kind: 'subagent', subAgent: { rootThreadId: 'root-B', parentThreadId: 'root-B' } }) })
    source.childrenByParent.set('root-B', [sibling])
    expect(findSubAgentChild(source, null, '/root/sibling', 'children')).toBeNull()
    expect(findSubAgentChild(source, null, '/root/sibling', 'tree')).toBe(sibling)
    sibling.threadSummary!.source!.subAgent!.rootThreadId = 'root-A'
    expect(findSubAgentChild(source, 'sibling', '/root/sibling', 'tree')).toBeNull()
  })
  it('resolves relative paths using the source agent path', () => {
    const source = lookup()
    source.threadList = [makeSubAgentThread('parent-B', { kind: 'subagent', subAgent: { rootThreadId: 'root-B', agentPath: '/root/reviewer' } })]
    source.childrenByParent.set('parent-B', [makeSubAgent({ agentPath: '/root/reviewer/core' })])
    expect(findSubAgentChild(source, null, 'core')?.childThreadId).toBe('child-B')
  })
  it('can prove a direct child from thread metadata before discovery but ignores stale summaries after discovery', () => {
    const source = lookup()
    source.childrenByParent.delete('parent-B')
    source.threadList = [makeSubAgentThread('child-B', { kind: 'subagent', subAgent: { parentThreadId: 'parent-B', agentPath: '/root/review_core' } })]
    expect(findSubAgentChild(source, null, '/root/review_core')?.childThreadId).toBe('child-B')
    source.discoveryByParent = new Map([['parent-B', { status: 'ready', discovered: true }]])
    expect(findSubAgentChild(source, null, '/root/review_core')).toBeNull()
  })
})
