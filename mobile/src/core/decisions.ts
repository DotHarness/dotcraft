import type { UserInputQuestion } from '@dotcraft/sdk/contracts'
import type { PendingRequest } from './state'

export type Decision = PendingRequest | { kind: 'plan'; requestId: string }

export function waitingDecisions(pending: PendingRequest[], planTurnId: string | null): Decision[] {
  return planTurnId === null ? pending : [...pending, { kind: 'plan', requestId: `plan:${planTurnId}` }]
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
