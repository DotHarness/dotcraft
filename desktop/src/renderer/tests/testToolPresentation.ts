import type { ConversationItem } from '../types/conversation'
import { CORE_TOOL_PRESENTATION_IDS } from '../utils/toolRendererRegistry'

const PRESENTATION_BY_TOOL_NAME: Readonly<Record<string, {
  presentationId: string
  options?: Record<string, unknown>
}>> = {
  CreatePlan: { presentationId: CORE_TOOL_PRESENTATION_IDS.createPlan },
  Cron: { presentationId: CORE_TOOL_PRESENTATION_IDS.cron },
  SkillManage: { presentationId: CORE_TOOL_PRESENTATION_IDS.skillManage },
  SkillView: { presentationId: CORE_TOOL_PRESENTATION_IDS.skillView },
  SpawnAgent: { presentationId: CORE_TOOL_PRESENTATION_IDS.subagent, options: { operation: 'spawn' } },
  WaitAgent: { presentationId: CORE_TOOL_PRESENTATION_IDS.subagent, options: { operation: 'wait' } },
  SendMessage: { presentationId: CORE_TOOL_PRESENTATION_IDS.subagent, options: { operation: 'sendMessage' } },
  FollowupTask: { presentationId: CORE_TOOL_PRESENTATION_IDS.subagent, options: { operation: 'followupTask' } },
  ListAgents: { presentationId: CORE_TOOL_PRESENTATION_IDS.subagent, options: { operation: 'list' } },
  CloseAgent: { presentationId: CORE_TOOL_PRESENTATION_IDS.subagent, options: { operation: 'close' } },
  SendInput: { presentationId: CORE_TOOL_PRESENTATION_IDS.subagent, options: { operation: 'sendInput' } },
  ResumeAgent: { presentationId: CORE_TOOL_PRESENTATION_IDS.subagent, options: { operation: 'resume' } },
  Exec: { presentationId: CORE_TOOL_PRESENTATION_IDS.shell },
  RunCommand: { presentationId: CORE_TOOL_PRESENTATION_IDS.shell },
  BashCommand: { presentationId: CORE_TOOL_PRESENTATION_IDS.shell },
  WriteFile: { presentationId: CORE_TOOL_PRESENTATION_IDS.fileWrite, options: { operation: 'write' } },
  EditFile: { presentationId: CORE_TOOL_PRESENTATION_IDS.fileWrite, options: { operation: 'edit' } },
  WebSearch: { presentationId: CORE_TOOL_PRESENTATION_IDS.web, options: { operation: 'search' } },
  WebFetch: { presentationId: CORE_TOOL_PRESENTATION_IDS.web, options: { operation: 'fetch' } },
  RequestUserInput: { presentationId: CORE_TOOL_PRESENTATION_IDS.requestUserInput },
  ReadFile: { presentationId: CORE_TOOL_PRESENTATION_IDS.readFile },
  GrepFiles: { presentationId: CORE_TOOL_PRESENTATION_IDS.readFile },
  FindFiles: { presentationId: CORE_TOOL_PRESENTATION_IDS.readFile },
  TodoWrite: { presentationId: CORE_TOOL_PRESENTATION_IDS.todo },
  UpdateTodos: { presentationId: CORE_TOOL_PRESENTATION_IDS.todo },
  SearchTools: { presentationId: CORE_TOOL_PRESENTATION_IDS.deferredSearch },
  tool_search: { presentationId: CORE_TOOL_PRESENTATION_IDS.deferredSearch }
}

export function withTestCorePresentation(item: ConversationItem): ConversationItem {
  if (item.type !== 'toolCall' || item.presentation || item.source) return item
  const descriptor = item.toolName ? PRESENTATION_BY_TOOL_NAME[item.toolName] : undefined
  if (!descriptor) return item
  return {
    ...item,
    source: { kind: 'CoreNative', sourceId: 'test-core', sourceToolId: item.toolName },
    presentation: descriptor
  }
}
