import { renderHook } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, describe, expect, it } from 'vitest'
import { TranscriptTurnsContext, useWebCitationSources } from '../components/conversation/useWebCitationSources'
import { useConversationStore } from '../stores/conversationStore'
import type { ConversationItem, ConversationTurn } from '../types/conversation'

const START = String.fromCharCode(0xe200)
const END = String.fromCharCode(0xe201)
const SEP = String.fromCharCode(0xe202)

function searchTurn(id: string, threadId: string, url: string): ConversationTurn {
  const item: ConversationItem = {
    id: 'item_001',
    type: 'webSearch',
    status: 'completed',
    createdAt: '2026-10-17T00:00:00.000Z',
    webSearch: { status: 'completed', query: 'q', action: { type: 'search', query: 'q' }, results: [{ ref_id: 'turn0search0', url }] }
  }
  return { id, threadId, status: 'completed', items: [item], startedAt: '2026-10-17T00:00:00.000Z' }
}

function citingTurn(id: string, threadId: string): ConversationTurn {
  return {
    id,
    threadId,
    status: 'completed',
    items: [{
      id: 'item_001',
      type: 'agentMessage',
      status: 'completed',
      createdAt: '2026-10-17T00:00:00.000Z',
      text: `See ${START}cite${SEP}turn0search0${END}`
    }],
    startedAt: '2026-10-17T00:00:00.000Z'
  }
}

describe('useWebCitationSources', () => {
  afterEach(() => {
    useConversationStore.setState({ turns: [] })
  })

  it('resolves a citation from the earlier turns of the transcript that renders it', () => {
    useConversationStore.setState({
      turns: [searchTurn('turn_001', 'main', 'https://main.example/'), citingTurn('turn_002', 'main')]
    })
    const sideTurns = [searchTurn('turn_001', 'side', 'https://side.example/'), citingTurn('turn_002', 'side')]
    const wrapper = ({ children }: { children: ReactNode }) => (
      <TranscriptTurnsContext.Provider value={sideTurns}>{children}</TranscriptTurnsContext.Provider>
    )

    const { result } = renderHook(() => useWebCitationSources(sideTurns[1], ''), { wrapper })

    expect(result.current.get('turn0search0')?.url).toBe('https://side.example/')
  })

  it('reads the foreground conversation when no transcript provides its turns', () => {
    const turns = [searchTurn('turn_001', 'main', 'https://main.example/'), citingTurn('turn_002', 'main')]
    useConversationStore.setState({ turns })

    const { result } = renderHook(() => useWebCitationSources(turns[1], ''))

    expect(result.current.get('turn0search0')?.url).toBe('https://main.example/')
  })
})
