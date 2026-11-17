import type { ThreadTaskConnection } from './childThreads'
import type { DesktopThreadTasks } from './threadTaskService'
import type { ThreadTaskToolCall, ThreadTaskToolResult } from './threadTaskTools'

const MAX_RECAP = 300
const MAX_ASK = 100
const MAX_LABEL = 24
const MAX_ACTIONS = 2

export const WRAP_UP_DESCRIPTION = `Fill the next-step box the user sees when this turn ends: a short recap and, when this turn leaves the user a decision to make, one question with buttons that answer it. Call it once in every turn in which you changed files, ran commands, or completed multi-step work, when the work is done and before your closing message. Skip it for short answers and pure conversation.
A button sends its label to you as the user's next message, so the user can go on with a click. One follow-up that acts on what this chat just found belongs here as the question, with a button that has you do it here. A task of its own goes to CreateThread "ownInitiative" instead.
Your closing message follows this call and is your full answer to the user. The recap never replaces it.`

export const WRAP_UP_PROPERTIES = {
  recap: {
    type: 'string',
    description: `Two or three short sentences to the user (at most ${MAX_RECAP} characters): what is now true or changed, then what is not done or not verified.`
  },
  ask: {
    type: 'string',
    description: `The one decision the user must make next, in one short sentence (at most ${MAX_ASK} characters), without listing the options the buttons give. Omit it, and actions, when nothing waits on the user.`
  },
  actions: {
    type: 'array',
    minItems: 1,
    maxItems: MAX_ACTIONS,
    description: `1–${MAX_ACTIONS} answers to ask, your recommendation first. Another button is there only when it is a realistic alternative the user might pick instead.`,
    items: {
      type: 'object',
      properties: {
        label: { type: 'string', description: `The button text, 1–3 words (at most ${MAX_LABEL} characters), written as the user's reply to you.` }
      },
      required: ['label'],
      additionalProperties: false
    }
  },
  blocked: {
    type: 'boolean',
    description: 'true when your work stops here until the user answers. Omit it when the answer can wait.'
  }
}

export interface WrapUpArguments {
  recap: string
  ask?: string
  actions?: Array<{ label: string }>
  blocked?: boolean
}

export function parseWrapUp(args: Record<string, unknown>): { ok: true; value: WrapUpArguments } | { ok: false; message: string } {
  const recap = typeof args.recap === 'string' ? args.recap.trim() : ''
  if (!recap) return { ok: false, message: 'recap must be a non-empty string.' }
  if (recap.length > MAX_RECAP) return { ok: false, message: `recap must be at most ${MAX_RECAP} characters.` }
  if (args.blocked != null && typeof args.blocked !== 'boolean') return { ok: false, message: 'blocked must be a boolean.' }

  if (args.ask != null && typeof args.ask !== 'string') return { ok: false, message: 'ask must be a string.' }
  const ask = typeof args.ask === 'string' ? args.ask.trim() : ''
  if (ask.length > MAX_ASK) return { ok: false, message: `ask must be at most ${MAX_ASK} characters.` }

  const rawActions = args.actions
  if (rawActions != null && !Array.isArray(rawActions)) return { ok: false, message: 'actions must be an array.' }
  const actions: Array<{ label: string }> = []
  for (const action of rawActions ?? []) {
    const label = action && typeof action === 'object' && typeof (action as { label?: unknown }).label === 'string'
      ? (action as { label: string }).label.trim()
      : ''
    if (!label) return { ok: false, message: 'Each action must be { "label": "…" } with a non-empty label.' }
    if (label.length > MAX_LABEL) return { ok: false, message: `Each action label must be at most ${MAX_LABEL} characters.` }
    actions.push({ label })
  }
  if (actions.length > MAX_ACTIONS) return { ok: false, message: `actions holds at most ${MAX_ACTIONS} buttons.` }
  if (Boolean(ask) !== actions.length > 0) return { ok: false, message: 'ask and actions come together: send both, or neither.' }

  return {
    ok: true,
    value: {
      recap,
      ...(ask ? { ask, actions } : {}),
      ...(args.blocked === true ? { blocked: true } : {})
    }
  }
}

export async function wrapUpTool(
  connection: ThreadTaskConnection,
  tasks: DesktopThreadTasks | undefined,
  args: Record<string, unknown>,
  call: ThreadTaskToolCall
): Promise<ThreadTaskToolResult> {
  const parsed = parseWrapUp(args)
  if (!parsed.ok) return { success: false, errorCode: 'InvalidArguments', errorMessage: parsed.message }
  const threadId = call.threadId?.trim()
  if (!tasks || !threadId || !(await tasks.canSeeCard(connection, threadId, call.turnId))) {
    return shownResult('Not shown: nobody is watching this turn. Carry on and report your result as usual.', false)
  }
  if (!parsed.value.ask) {
    return shownResult('Nothing is pending, so no box is shown. Write your closing message now, in full, as the last thing you say this turn.', false)
  }
  if (parsed.value.blocked && call.callId) tasks.notifyWrapUpQuestion(connection, threadId, call.turnId, call.callId)
  return shownResult('Shown in the next-step box. Write your closing message now, in full, as the last thing you say this turn. The box asks your question, so do not ask it again.', true)
}

function shownResult(text: string, shown: boolean): ThreadTaskToolResult {
  return { success: true, contentItems: [{ type: 'text', text }], structuredContent: { shown } }
}
