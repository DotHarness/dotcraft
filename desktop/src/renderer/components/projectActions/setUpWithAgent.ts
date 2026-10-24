import type { ProjectEnvironmentsResult } from '../../../shared/projectActions'
import { useConnectionStore } from '../../stores/connectionStore'
import { useConversationStore } from '../../stores/conversationStore'
import { isSideChatRunning, useSideChatStore } from '../../stores/sideChatStore'
import { useThreadStore } from '../../stores/threadStore'
import { canOpenSideChat, openSideChat } from '../../utils/sideChat'
import { projectFolderName } from '../settings/panels/worktrees/environmentPresentation'
import { setUpEnvironmentPrompt } from './environmentAgentPrompts'
import { selectProjectEnvironment } from './environmentSelection'

type Translate = (key: string, vars?: Record<string, string | number>) => string

function validDefault(result: ProjectEnvironmentsResult): { id: string; path: string } | null {
  return result.environments.find((environment) => environment.isDefault && environment.error === undefined) ?? null
}

export function canSetUpWithAgent(threadId: string): boolean {
  return useThreadStore.getState().activeThreadId === threadId && canOpenSideChat()
}

export function onSetUpWithAgentAvailabilityChange(threadId: string, listener: (available: boolean) => void): () => void {
  let current = canSetUpWithAgent(threadId)
  const check = (): void => {
    const next = canSetUpWithAgent(threadId)
    if (next === current) return
    current = next
    listener(next)
  }
  const disposers = [
    useConnectionStore.subscribe(check),
    useThreadStore.subscribe(check),
    useConversationStore.subscribe(check)
  ]
  return () => {
    for (const dispose of disposers) dispose()
  }
}

export function selectEnvironmentWhenAgentFinishes(sideChatKey: string, target: {
  workspacePath: string
  threadId: string
}): () => void {
  const initial = useSideChatStore.getState().chats.get(sideChatKey)
  let running = initial ? isSideChatRunning(initial) : false
  let checking = false
  let recheck = false
  let stopped = false
  let unsubscribe: () => void = () => {}
  const stop = (): void => {
    stopped = true
    unsubscribe()
  }

  const check = async (): Promise<void> => {
    checking = true
    try {
      const environment = validDefault(await window.api.projectActions.list(target.workspacePath))
      if (!environment || stopped) return
      stop()
      await selectProjectEnvironment({ ...target, environment })
    } catch (error) {
      console.error('Unable to select the new environment:', error)
    } finally {
      checking = false
      if (recheck && !stopped) {
        recheck = false
        void check()
      }
    }
  }

  unsubscribe = useSideChatStore.subscribe((state) => {
    const chat = state.chats.get(sideChatKey)
    if (!chat) {
      stop()
      return
    }
    const next = isSideChatRunning(chat)
    const ended = running && !next
    running = next
    if (!ended) return
    if (checking) recheck = true
    else void check()
  })
  return stop
}

export async function setUpEnvironmentWithAgent({ workspacePath, threadId, t }: {
  workspacePath: string
  threadId: string
  t: Translate
}): Promise<void> {
  const [environments, projectEnvironments] = await Promise.all([
    window.api.projectActions.list(workspacePath),
    window.api.projectActions.listProjectEnvironments(workspacePath)
  ])
  const text = setUpEnvironmentPrompt({
    path: projectEnvironments.newEnvironmentPath,
    name: projectFolderName(workspacePath),
    platform: window.api.platform
  })
  const key = await openSideChat(threadId, t, { prompt: { text } })
  if (key && !validDefault(environments)) selectEnvironmentWhenAgentFinishes(key, { workspacePath, threadId })
}
