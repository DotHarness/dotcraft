import type { ThreadTaskConnection } from './childThreads'
import type { DesktopThreadTasks } from './threadTaskService'
import type { ThreadTaskToolCall, ThreadTaskToolResult } from './threadTaskTools'

const RECAP_CHARS = 300
const ASK_CHARS = 100
const LABEL_CHARS = 24
const MAX_ACTIONS = 2

export const WRAP_UP_DESCRIPTION = `Fill the next-step box the user sees when this turn ends: a two-or-three-sentence recap and, when this turn leaves the user a decision to make or a step to take, one question and buttons that answer it. A turn with nothing pending ends with the recap alone, and no box is shown. Call it once in every turn in which you changed files, ran commands, or completed multi-step work, when this turn's work is done and before your closing message. Skip it for short answers, pure conversation, or a brief read-and-explain turn. A reply that names a follow-up is not brief: ask or propose it, and call this.
Your closing message is ordinary text right after this call, the last thing you say this turn: your answer to the user in full, which the recap never replaces. The result says whether the box shows your question: ask in the message only a question it does not show.
recap and ask are said to the user, in the second person. A button's label is the user's words back to you.`

export const WRAP_UP_PROPERTIES = {
  recap: {
    type: 'string',
    description: `Two or three short sentences to the user (about ${RECAP_CHARS} characters): what is now true or changed, then where it stands, including what is not done or not verified.`
  },
  ask: {
    type: 'string',
    description: `The one decision the user must make next, in one short sentence (about ${ASK_CHARS} characters), with no reason or recommendation and without listing the options the buttons give. Omit it, and actions, when nothing waits on the user.`
  },
  actions: {
    type: 'array',
    description: `1–${MAX_ACTIONS} buttons, recommendation first. They are answers to your ask: the first accepts your recommendation, and another is there only when it is a realistic alternative the user might pick instead. When blocked is true, they are the most likely answers to your question. Never repeat an offer the user passed over, unless what it acts on has changed since or they bring it up.`,
    items: {
      type: 'object',
      properties: {
        label: { type: 'string', description: `The button text, 1–3 words (${LABEL_CHARS} characters): a button, not a sentence. A click sends it to you as the user's answer.` }
      },
      required: ['label'],
      additionalProperties: false
    }
  },
  blocked: {
    type: 'boolean',
    description: 'true when your work stops here until the user answers (the chat shows as waiting on them). Omit it when the answer can wait.'
  }
}

interface WrapUpCall {
  recap: string
  ask: string
  labels: string[]
  blocked: boolean
  advice: string[]
}

function readWrapUp(args: Record<string, unknown>): WrapUpCall | null {
  const recap = typeof args.recap === 'string' ? args.recap.trim() : ''
  if (!recap) return null
  const ask = typeof args.ask === 'string' ? args.ask.trim() : ''
  const labels = (Array.isArray(args.actions) ? args.actions : []).flatMap((action) => {
    const label = (action as { label?: unknown } | null)?.label
    return typeof label === 'string' && label.trim() ? [label.trim()] : []
  })

  const advice: string[] = []
  if (Boolean(ask) !== labels.length > 0) {
    advice.push('The box drew no buttons: it needs ask and at least one entry in actions. When nothing is left for the user, the recap alone is the whole call.')
  }
  if (recap.length > RECAP_CHARS) advice.push(`recap is two or three sentences (about ${RECAP_CHARS} characters). This one ran ${recap.length}.`)
  if (ask.length > ASK_CHARS) advice.push(`ask is the decision alone, in one short sentence (about ${ASK_CHARS} characters). This one ran ${ask.length}.`)
  if (labels.length > MAX_ACTIONS) advice.push(`actions is 1–${MAX_ACTIONS} buttons. Only the first ${MAX_ACTIONS} were shown.`)
  const long = labels.find((label) => label.length > LABEL_CHARS)
  if (long) advice.push(`A reply label is a button, 1–3 words (${LABEL_CHARS} characters). "${long}" is too long.`)

  return { recap, ask, labels, blocked: args.blocked === true, advice }
}

export async function wrapUpTool(
  connection: ThreadTaskConnection,
  tasks: DesktopThreadTasks | undefined,
  args: Record<string, unknown>,
  call: ThreadTaskToolCall
): Promise<ThreadTaskToolResult> {
  const wrapUp = readWrapUp(args)
  if (!wrapUp) {
    return { success: false, errorCode: 'InvalidArguments', errorMessage: 'recap is required: what is now true or changed and where it stands, said to the user in two or three sentences.' }
  }
  if (!wrapUp.ask || wrapUp.labels.length === 0) {
    return result('Nothing is pending, so no box is shown. Write your closing message now, in full, as the last thing you say this turn.', false, wrapUp.advice)
  }
  const threadId = call.threadId?.trim()
  if (tasks && threadId && (await tasks.fromPhone(connection, threadId, call.turnId))) {
    return result('Not shown: the user is on the phone, which has no next-step box. Ask your question in your closing message.', false, [])
  }
  if (wrapUp.blocked && tasks && threadId && call.callId) tasks.waitForQuestion(connection, threadId, call.turnId, call.callId)
  return result('Shown in the next-step box. Write your closing message now, in full, as the last thing you say this turn. The box asks your question, so do not ask it again.', true, wrapUp.advice)
}

function result(text: string, shown: boolean, advice: string[]): ThreadTaskToolResult {
  const full = advice.length === 0
    ? text
    : `${text}\nDo not call WrapUp again this turn. The advice below is for your next call:\n${advice.map((line) => `- ${line}`).join('\n')}`
  return { success: true, contentItems: [{ type: 'text', text: full }], structuredContent: { shown } }
}
