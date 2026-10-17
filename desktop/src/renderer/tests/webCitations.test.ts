import { describe, expect, it } from 'vitest'
import type { ConversationItem, ConversationTurn } from '../types/conversation'
import {
  collectWebCitationSources,
  formatWebCitations,
  hasWebCitationMarkers,
  stripWebCitations
} from '../utils/webCitations'

const START = String.fromCharCode(0xe200)
const END = String.fromCharCode(0xe201)
const SEP = String.fromCharCode(0xe202)
const PUA = new RegExp(`[${START}${END}${SEP}]`)

function cite(...refIds: string[]): string {
  return `${START}cite${SEP}${refIds.join(SEP)}${END}`
}

function searchItem(id: string, results: Record<string, unknown>[]): ConversationItem {
  return {
    id,
    type: 'webSearch',
    status: 'completed',
    createdAt: '2026-10-16T00:00:00.000Z',
    webSearch: { status: 'completed', query: 'q', action: { type: 'search', query: 'q' }, results }
  }
}

function turn(id: string, items: ConversationItem[]): ConversationTurn {
  return { id, threadId: 'thread-1', status: 'completed', items, startedAt: '2026-10-16T00:00:00.000Z' }
}

const sources = collectWebCitationSources([
  turn('t1', [searchItem('s1', [
    { ref_id: 'turn0search0', url: 'https://www.example.com/a', domain: 'www.example.com' },
    { ref_id: 'turn0search1', url: 'https://docs.example.org/b(1)' },
    { ref_id: 'turn0search2', url: 'javascript:alert(1)', domain: 'evil.test' },
    { ref_id: 'turn0search3', url: 'https://x.test/[a]', domain: 'x[y].test' },
    { url: 'https://no-ref.test' }
  ])])
])

describe('collectWebCitationSources', () => {
  it('keeps http(s) results with a ref id and labels them by domain', () => {
    expect([...sources.keys()]).toEqual(['turn0search0', 'turn0search1', 'turn0search3'])
    expect(sources.get('turn0search0')).toEqual({ url: 'https://www.example.com/a', label: 'example.com' })
    expect(sources.get('turn0search1')).toEqual({ url: 'https://docs.example.org/b(1)', label: 'docs.example.org' })
  })

  it('lets a later search redefine a ref id', () => {
    const later = collectWebCitationSources([
      turn('t1', [searchItem('s1', [{ ref_id: 'turn0search0', url: 'https://old.test/' }])]),
      turn('t2', [searchItem('s2', [{ ref_id: 'turn0search0', url: 'https://new.test/' }])])
    ])
    expect(later.get('turn0search0')?.url).toBe('https://new.test/')
  })
})

describe('formatWebCitations', () => {
  it('leaves text without markers untouched', () => {
    const text = 'Plain [link](https://example.com) text.'
    expect(hasWebCitationMarkers(text)).toBe(false)
    expect(formatWebCitations(text, sources, 'display')).toBe(text)
  })

  it('renders a resolvable citation as a link to its first source', () => {
    expect(formatWebCitations(`Shipped in May ${cite('turn0search0')}.`, sources, 'display'))
      .toBe('Shipped in May [example.com](<https://www.example.com/a>).')
    expect(formatWebCitations(`A ${cite('turn0search0', 'turn0search1', 'turn0search0')}`, sources, 'display'))
      .toBe('A [example.com +1](<https://www.example.com/a>)')
  })

  it('copies every resolvable source as a Markdown link', () => {
    expect(formatWebCitations(`A ${cite('turn0search0', 'missing', 'turn0search1')}.`, sources, 'copy'))
      .toBe('A ([example.com](<https://www.example.com/a>), [docs.example.org](<https://docs.example.org/b(1)>)).')
  })

  it('escapes link labels', () => {
    expect(formatWebCitations(cite('turn0search3'), sources, 'copy')).toBe('([x\\[y\\].test](<https://x.test/[a]>))')
  })

  it('drops unresolvable citations and other marker kinds', () => {
    const text = `One${cite('missing')} two ${START}filecite${SEP}turn0file0${END}three ${START}entity${SEP}["x"]${END}.`
    for (const mode of ['display', 'copy'] as const) {
      const formatted = formatWebCitations(text, sources, mode)
      expect(formatted).toBe('One two three .')
      expect(formatted).not.toMatch(PUA)
    }
  })

  it('hides a partially streamed marker until it completes', () => {
    const partial = `Answer ${START}cite${SEP}turn0sea`
    expect(formatWebCitations(partial, sources, 'display', true)).toBe('Answer ')
    expect(formatWebCitations(`Answer ${START}`, sources, 'display', true)).toBe('Answer ')
    expect(formatWebCitations(`${cite('turn0search0')} and ${START}ci`, sources, 'display', true))
      .toBe('[example.com](<https://www.example.com/a>) and ')
  })

  it('never leaves stray marker characters in finished text', () => {
    const formatted = formatWebCitations(`Broken ${START}cite${SEP}turn0search0 tail ${END} end`, sources, 'display')
    expect(formatted).not.toMatch(PUA)
    expect(stripWebCitations(`a${SEP}b${END}c`)).toBe('abc')
  })

  it('strips every marker when no sources are known', () => {
    expect(stripWebCitations(`Claim ${cite('turn0search0')}.`)).toBe('Claim .')
    expect(stripWebCitations(`Claim ${START}cite`, true)).toBe('Claim ')
  })
})
