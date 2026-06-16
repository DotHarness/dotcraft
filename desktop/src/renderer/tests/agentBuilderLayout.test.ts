import { describe, expect, it } from 'vitest'
import {
  AGENT_BUILDER_CHAT_DEFAULT_WIDTH,
  AGENT_BUILDER_CHAT_DEFAULT_WIDTH_RATIO,
  AGENT_BUILDER_CHAT_MIN_WIDTH,
  AGENT_BUILDER_MAIN_MIN_WIDTH,
  resolveAgentBuilderChatWidth
} from '../utils/agentBuilderLayout'

describe('resolveAgentBuilderChatWidth', () => {
  it('keeps the current visual default at the default ratio', () => {
    expect(resolveAgentBuilderChatWidth(
      AGENT_BUILDER_CHAT_DEFAULT_WIDTH,
      AGENT_BUILDER_CHAT_DEFAULT_WIDTH_RATIO,
      Math.round(AGENT_BUILDER_CHAT_DEFAULT_WIDTH / AGENT_BUILDER_CHAT_DEFAULT_WIDTH_RATIO)
    )).toBe(AGENT_BUILDER_CHAT_DEFAULT_WIDTH)
  })

  it('never shrinks below the chat pane minimum width', () => {
    expect(resolveAgentBuilderChatWidth(200, 0.1, 1200)).toBe(AGENT_BUILDER_CHAT_MIN_WIDTH)
  })

  it('caps the chat pane to preserve the main editor minimum width', () => {
    expect(resolveAgentBuilderChatWidth(900, 900 / 1200, 1200)).toBe(1200 - AGENT_BUILDER_MAIN_MIN_WIDTH)
  })

  it('scales proportionally when the split gets narrower', () => {
    expect(resolveAgentBuilderChatWidth(520, 520 / 1600, 1200)).toBe(390)
  })

  it('uses the fallback width before the split is measured', () => {
    expect(resolveAgentBuilderChatWidth(480, 0.4, null)).toBe(480)
  })
})
