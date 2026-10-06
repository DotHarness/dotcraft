import type { MessageKey } from '../../shared/locales'

type T = (key: MessageKey | string, vars?: Record<string, string | number>) => string

interface AgentProfileText {
  id: string
  name?: string
  description?: string
  source?: string
}

const BUILT_IN_PROFILE_TEXT: Record<string, { name: MessageKey; description: MessageKey }> = {
  'data-analyst': { name: 'agentProfile.builtIn.dataAnalyst.name', description: 'agentProfile.builtIn.dataAnalyst.description' },
  prototyper: { name: 'agentProfile.builtIn.prototyper.name', description: 'agentProfile.builtIn.prototyper.description' },
  'qa-tester': { name: 'agentProfile.builtIn.qaTester.name', description: 'agentProfile.builtIn.qaTester.description' },
  researcher: { name: 'agentProfile.builtIn.researcher.name', description: 'agentProfile.builtIn.researcher.description' },
  'task-runner': { name: 'agentProfile.builtIn.taskRunner.name', description: 'agentProfile.builtIn.taskRunner.description' },
  writer: { name: 'agentProfile.builtIn.writer.name', description: 'agentProfile.builtIn.writer.description' }
}

export function localizeAgentProfile<P extends AgentProfileText>(profile: P, t: T): P {
  const keys = profile.source === 'builtIn' ? BUILT_IN_PROFILE_TEXT[profile.id] : undefined
  return keys ? { ...profile, name: t(keys.name), description: t(keys.description) } : profile
}
