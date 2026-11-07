import type { ThreadRuntimeState, UserInputQuestion } from '@dotcraft/sdk/contracts'
import { awaitsPlanConfirmation } from './chatState'
import type { ChatDetail, PendingRequest } from './state'

export type Decision = PendingRequest | { kind: 'plan'; requestId: string }

export function implementationTurn(detail: ChatDetail | undefined, runtime: ThreadRuntimeState | null): string | null {
  if (!detail || detail.loading || !detail.currentPlan || !awaitsPlanConfirmation(runtime, detail.config?.mode)) return null
  const turn = detail.history.turns.at(-1)
  if (turn?.status !== 'completed') return null
  if (detail.currentPlanTurnId && detail.currentPlanTurnId !== turn.id) return null
  const items = detail.history.items.filter((item) => item.turnId === turn.id)
  const call = items.findLast((item) => item.type === 'toolCall' && item.payload.toolName === 'CreatePlan')
  const args = call?.payload.arguments as { plan?: string } | undefined
  if (!call || args?.plan !== detail.currentPlan) return null
  return items.some((item) => item.type === 'toolResult' && item.payload.callId === call.payload.callId && item.payload.success === true)
    ? turn.id : null
}

export function waitingDecisions(pending: PendingRequest[], planTurnId: string | null): Decision[] {
  const requests = [...pending.filter((request) => request.kind === 'approval'), ...pending.filter((request) => request.kind === 'question')]
  return planTurnId === null ? requests : [...requests, { kind: 'plan', requestId: `plan:${planTurnId}` }]
}

export interface QuestionChoice {
  option: number
  other: string
}

export function initialChoices(questions: UserInputQuestion[]): QuestionChoice[] {
  return questions.map(() => ({ option: 0, other: '' }))
}

export function hasOther(question: UserInputQuestion): boolean {
  return question.isOther !== false
}

export function questionAnswers(questions: UserInputQuestion[], choices: QuestionChoice[], otherLabel: string): Record<string, string[]> {
  const answers: Record<string, string[]> = {}
  questions.forEach((question, index) => {
    const choice = choices[index] ?? { option: 0, other: '' }
    const option = question.options[choice.option]
    if (option) {
      answers[question.id] = [option.label]
      return
    }
    const text = choice.other.trim()
    answers[question.id] = [text ? `user_note: ${text}` : otherLabel]
  })
  return answers
}
