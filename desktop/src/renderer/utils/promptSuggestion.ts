const SUGGESTION_TIMEOUT_MS = 30_000

const SUGGESTION_PROMPT = `Predict the user's next message in DotCraft from their original request and recent messages. Choose what they are already likely to type. Avoid steering them toward what you think they should do.

Use concrete next steps when the conversation makes them clear:
- A requested bug fix is done but the requested tests remain: "run the tests".
- Code is ready to try: "try it out".
- Options were offered: the choice the conversation indicates they would make.
- The assistant asks to continue: "yes" or "go ahead".
- A task is complete with an obvious follow-up: "commit this" or "push it".

Match the user's style in 2–12 words. Prefer a specific action such as "run the tests" over a vague "continue". Exclude praise or thanks, questions, assistant phrasing such as "Let me", "I'll", or "Here's", unrequested ideas, and multiple sentences.

Return nothing after an error or misunderstanding, giving the user room to assess or correct it. Also return nothing when no next step is obvious, or when a suggestion could be unsafe or inappropriate. Stay silent on sensitive topics, including security incidents, credentials, harm, and private data, even during legitimate security work.

Output only the suggested message, without quotes or explanation, or leave the response empty.`

interface TerminalTurn {
  status?: string
  items?: Array<{ type?: string; payload?: { text?: string } }>
}

export interface PromptSuggestionDiagnostic {
  parentThreadId: string
  parentTurnId: string
  threadId: string | null
  turnId: string | null
  outcome: string
  textLength?: number
}

export function logPromptSuggestion(diagnostic: PromptSuggestionDiagnostic): void {
  console.debug('[prompt-suggestion]', diagnostic)
}

export async function generatePromptSuggestion(
  threadId: string,
  turnId: string,
  signal: AbortSignal,
  onDiagnostic?: (diagnostic: PromptSuggestionDiagnostic) => void
): Promise<string | null> {
  if (signal.aborted) return null

  let forkId: string | null = null
  let forkTurnId: string | null = null
  let terminal = false
  let expired = false
  let resolveTerminal!: (turn: TerminalTurn | null) => void
  const terminalPromise = new Promise<TerminalTurn | null>((resolve) => { resolveTerminal = resolve })
  const record = (outcome: string, textLength?: number): void => {
    const diagnostic = {
      parentThreadId: threadId,
      parentTurnId: turnId,
      threadId: forkId,
      turnId: forkTurnId,
      outcome,
      textLength
    }
    logPromptSuggestion(diagnostic)
    onDiagnostic?.(diagnostic)
  }
  const onAbort = (): void => {
    record('cancelled')
    resolveTerminal(null)
  }
  signal.addEventListener('abort', onAbort, { once: true })
  const timeout = window.setTimeout(() => {
    expired = true
    record('timeout')
    resolveTerminal(null)
  }, SUGGESTION_TIMEOUT_MS)
  const unsubscribe = window.api.appServer.onNotification((notification) => {
    if (forkId == null || !['turn/completed', 'turn/failed', 'turn/cancelled'].includes(notification.method)) return
    const params = notification.params as { turn?: { id?: string; threadId?: string } & TerminalTurn }
    if (params.turn?.threadId !== forkId) return
    forkTurnId ??= params.turn.id ?? null
    terminal = true
    if (notification.method !== 'turn/completed') {
      record(notification.method === 'turn/cancelled' ? 'cancelled' : 'failed')
    }
    resolveTerminal(notification.method === 'turn/completed' ? params.turn : null)
  })

  try {
    record('requested')
    const fork = await window.api.appServer.sendRequest('thread/fork', {
      threadId,
      forkPoint: { turnId, position: 'after' },
      ephemeral: true,
      promptSuggestion: true,
      displayName: '[internal] Prompt suggestion'
    }, SUGGESTION_TIMEOUT_MS)
    forkId = fork.thread?.id ?? null
    record('forked')
    if (forkId == null || signal.aborted || expired) return null

    const started = await window.api.appServer.sendRequest('turn/start', {
      threadId: forkId,
      input: [{ type: 'text', text: SUGGESTION_PROMPT }]
    }, SUGGESTION_TIMEOUT_MS)
    forkTurnId = started.turn?.id ?? forkTurnId
    record('started')
    if (signal.aborted || expired) return null

    const result = await terminalPromise
    if (signal.aborted || expired || result?.status !== 'completed') return null
    const answer = result.items?.filter((item) => item.type === 'agentMessage').at(-1)?.payload?.text
    const suggestion = typeof answer === 'string' ? answer.trim() || null : null
    record(suggestion ? 'received' : 'empty', suggestion?.length ?? 0)
    return suggestion
  } catch {
    if (!signal.aborted && !expired) record('failed')
    return null
  } finally {
    unsubscribe()
    window.clearTimeout(timeout)
    signal.removeEventListener('abort', onAbort)
    if (forkId != null) {
      if (!terminal && forkTurnId != null) {
        await window.api.appServer.sendRequest('turn/interrupt', {
          threadId: forkId,
          turnId: forkTurnId
        }).catch(() => {})
      }
      await window.api.appServer.sendRequest('thread/delete', { threadId: forkId }).catch(() => {})
    }
  }
}
