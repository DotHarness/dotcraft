import threadCoordination from '../prompts/threadCoordination.md?raw'
import type { ThreadTask } from '../../shared/threadTasks'
import { PROPOSAL_EXPIRY_MS } from './threadTaskStore'

const EXPIRY_DAYS = Math.round(PROPOSAL_EXPIRY_MS / 86_400_000)
const NO_LONGER_PENDING = 'it was started in its own chat, or the user sent it to this one, which starts none'

export const CREATE_THREAD_DESCRIPTION = `Start a separate DotCraft chat for a task while you stay with the user (when: see "Starting other chats"). \`initiation\` says who wants it: "userAsked" starts it now and returns its id; "offer" is for a task the user named in their latest message (new work here, or "suggest a separate chat for X"): called with a title alone, it shows a "New chat" / "Do it here" card and WAITS for their answer, so it must be the only call in that message (no other tool); the result then tells you to carry on here, or to send the brief with proposalId so the chat starts; "ownInitiative" is for work you propose yourself that the user has not told you to do — something you noticed, or the follow-up your own answer or work turned up (when your reply calls work a follow-up or a separate change, propose it here rather than only saying so, unless it acts on what this chat just found and so stays here); the user sees a card and nothing runs until they start it, by clicking it or by telling you (then call this again with just proposalId).
"userAsked" and "ownInitiative" both need a \`prompt\` — the brief: the task, the files or area, what done looks like — and the call is refused without it; \`background\` does not stand in for it. Only an "offer" is a title alone.
The new chat gets your \`prompt\` and \`background\` only, not this conversation.`

export const CREATE_THREAD_PROPERTIES = {
  initiation: {
    type: 'string',
    enum: ['userAsked', 'offer', 'ownInitiative'],
    description: '"userAsked": the user asked in words for separate, parallel or background work, such as splitting a task off into its own chat, or told you to start this piece — it starts now. "offer": a task the user named in their latest message (new work for this chat, or one they asked you to suggest as a separate chat) — the user is asked first and the call waits for their answer; send only title with it (the brief follows, with proposalId, if they choose New chat). "ownInitiative": work you propose yourself that the user has not told you to do — something you noticed, or the follow-up your own answer or work turned up; it waits as a proposal card until the user starts it; never for a task the user\'s message tells you to do, nor for the one piece of work that acts on what this chat just found. Required unless proposalId is set.'
  },
  proposalId: {
    type: 'string',
    description: 'Start a proposal the user told you to start, exactly as proposed: its taskId and nothing else. To change it, DismissProposal it and call CreateThread anew. Also sends the brief of an offer the user accepted, as that result says.'
  },
  title: {
    type: 'string',
    description: 'Under 60 chars; names the task. Shown on the card and as the new chat\'s title. Required unless proposalId is set.'
  },
  prompt: {
    type: 'string',
    description: 'The brief: the task, the files or area, what done looks like. Required for "userAsked" and "ownInitiative" — the call is refused without it, and `background` does not stand in for it; an "offer" leaves it out (the card shows the title) and you send it with proposalId once they choose New chat. Here and in `background`, put file paths and code identifiers in backticks.'
  },
  background: {
    type: 'string',
    description: 'Overall goal, decisions and constraints so far, and how this piece fits — a few sentences for someone who has not seen this conversation.'
  },
  model: {
    type: 'string',
    description: 'Optional model id for the new chat. Omit to use the default; on a card the user can change it.'
  },
  reasoningEffort: {
    type: 'string',
    enum: ['low', 'medium', 'high', 'extraHigh', 'max', 'ultra'],
    description: 'Optional reasoning effort for the new chat; on a card the user can change it.'
  }
}

export const DISMISS_PROPOSAL_DESCRIPTION = `Withdraw a proposal you made with CreateThread (initiation "ownInitiative" or "offer") that the user has not started.

Call this when it went stale: the work was done here after all (by you or the user), it turned out unnecessary, or you proposed a better-scoped replacement (propose the new one first, then dismiss the old taskId). A card the user answered has already left, "Do it here" included, so it needs no call.`

export const DISMISS_PROPOSAL_PROPERTIES = {
  taskId: { type: 'string', description: 'The taskId the CreateThread proposal returned.' },
  reason: {
    type: 'string',
    description: 'Optional one-line reason the proposal is no longer needed, e.g. "fixed in this chat" or "superseded by task_ab12cd34".'
  }
}

export const THREAD_COORDINATION_GUIDANCE = threadCoordination.trimEnd()

export const threadTaskText = {
  initiationRequired: 'initiation is required: "offer" (a task from the user\'s latest message — they choose New chat or Do it here), "userAsked" (the user told you to start this chat) or "ownInitiative" (work you propose yourself, such as a follow-up your answer recommends, unless it acts on what this chat just found and so stays here — it waits as a proposal).',
  titleRequired: 'title is required.',
  promptRequired: (initiation: string) =>
    `prompt is required with initiation "${initiation}": the brief (the task, the files or area, what done looks like); background does not stand in for it. Only an "offer" is a title alone.`,
  proposalIdFormat: 'proposalId must be the taskId a CreateThread proposal returned (format: task_xxxxxxxx).',
  proposalExtras: (keys: string[]) =>
    `proposalId starts the proposal exactly as proposed — drop ${keys.join(', ')}. To run it differently, DismissProposal the proposal and call CreateThread with the new details.`,
  briefRequired: (taskId: string) =>
    `The user chose "New chat" for ${taskId}: send its brief with exactly proposalId "${taskId}", prompt and background.`,
  proposalNotFound: (taskId: string) =>
    `No proposal with id ${taskId} in this chat; nothing started. Do not start it again.`,
  proposalNotPending: (taskId: string) =>
    `Proposal ${taskId} is no longer pending, so there is nothing to start: ${NO_LONGER_PENDING}. If their message asks you to do it here, no other chat has it, so carry on. Nothing was changed.`,
  proposalDismissed: (taskId: string) =>
    `Proposal ${taskId} was withdrawn, so there is nothing to start. Propose it again only if the user asks for it.`,
  proposalExpired: (taskId: string) =>
    `Proposal ${taskId} expired unstarted, so there is nothing to start. Propose it again only if the user asks for it.`,
  proposalStarting: (taskId: string) =>
    `Proposal ${taskId} is being started right now. Nothing was changed.`,
  started: (title: string, threadId: string, ignored: string[] = []) =>
    `Started chat "${title}" (threadId: ${threadId}). It runs on its own and does not report back here. Don't repeat the introduction: say in one line that it is running in its own chat, then end your turn unless the user asked you for more here — do not wait for it.` +
    (ignored.length > 0 ? ` (Ignored ${ignored.join(', ')}: the user's choices on the card decide those.)` : ''),
  noCardNobody: 'No card was shown: this turn\'s request did not come from someone at the Desktop composer, so there is nobody to ask. Do the task in this chat as usual.',
  noCardAlwaysHere: 'No card was shown: the user\'s setting keeps new tasks in the chat they were asked in. Do the task in this chat as usual.',
  offerAccepted: (task: ThreadTask) =>
    `The user chose "New chat" — the chat is already opening — for "${task.title}" (taskId: ${task.id}). Write its brief now: call CreateThread once more with exactly proposalId "${task.id}", prompt (the task as the user would send it to a fresh chat: the files or area, what done looks like) and background (what that chat needs from this conversation, a few sentences) — no other arguments (where it runs, model and permissions were the user's to pick on the card) and nothing else in that message. Do not do the task here. When that call returns, say in one line that it is running in its own chat, then end your turn.`,
  offerKept: (taskId: string) =>
    `The user chose "Do it here" (taskId: ${taskId}): do the task in this chat now, as usual.`,
  offerTypedPast: (taskId: string) =>
    `The user did not answer the card: they sent a new message instead. The offer is closed (taskId: ${taskId}) and the task gets no chat of its own, so do not offer it again. Their message may change the task or call it off, so deal with it first. If you can see it right after this result, respond to it, then do the task only if it still fits what they wrote. If you cannot see it, it is queued and reaches you only when this turn ends: do not start the task; say in one line that you will read their message first, and end your turn. When it arrives, respond to it, then do the task if it still fits.`,
  offerUnstarted: (taskId: string) =>
    `The user chose "New chat" but it could not be started, and they saw why (taskId: ${taskId}). Do the task in this chat as usual.`,
  offerAbandoned: (taskId: string) =>
    `The offer was withdrawn before the user answered (taskId: ${taskId}); do nothing further with it.`,
  proposed: (task: ThreadTask, evicted: ThreadTask[], others: ThreadTask[]) =>
    `Proposed "${task.title}" (taskId: ${task.id}). The user sees a card and nothing runs until they start it — with a click, or by telling you (then call CreateThread with proposalId ${task.id} and nothing else). It expires in ${EXPIRY_DAYS} days unless started; DismissProposal withdraws it sooner if it goes stale. ` +
    (evicted.length > 0
      ? `The oldest waiting proposal${evicted.length === 1 ? ' was' : 's were'} taken off the queue to make room (no longer shown as a card): ${listTasks(evicted)}. `
      : '') +
    (others.length > 0 ? `Also still waiting: ${listTasks(others)}. ` : '') +
    'Don\'t wait for it and don\'t repeat the introduction; carry on with what you were doing.',
  nothingProposed: 'Nothing was proposed. This turn\'s request did not come from someone at the Desktop composer, and they may not see a task card, so do not mention a card or suggest a chat on this turn; carry on with what you were doing.',
  dismissFormat: 'taskId must be the id a CreateThread proposal returned (format: task_xxxxxxxx).',
  dismissed: (taskId: string) =>
    `Task ${taskId} withdrawn — the card is no longer shown to the user. Continue your current work.`,
  dismissAlreadyStarted: (taskId: string) =>
    `Task ${taskId} is no longer pending, so there is nothing to withdraw: ${NO_LONGER_PENDING}. If their message asks you to do it here, no other chat has it, so carry on. Nothing was changed.`,
  dismissAlreadyDismissed: (taskId: string) =>
    `Task ${taskId} had already left the queue (dismissed earlier, or displaced by newer proposals). Do not propose it again.`,
  dismissExpired: (taskId: string) =>
    `Task ${taskId} already expired unstarted, so it is no longer shown. Do not propose it again.`,
  dismissStarting: (taskId: string) =>
    `Task ${taskId} is being started right now — it can't be withdrawn while the start is in flight. Nothing was changed.`,
  dismissNotFound: (taskId: string) =>
    `No pending task with id ${taskId} — it may never have been proposed in this chat, or its record aged out. Do not propose it again.`
}

function listTasks(tasks: ThreadTask[]): string {
  return tasks.map((task) => `${task.id} "${task.title.replace(/[\r\n]+/g, ' ').slice(0, 120)}"`).join(', ')
}
