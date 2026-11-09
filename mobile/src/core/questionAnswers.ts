import type { HistoryItem } from './history'
import type { QuestionAnswer } from './transcriptTypes'

interface Question {
  id: string
  question?: string
  header?: string
  isSecret?: boolean
}

export function questionAnswersOf(request: HistoryItem | undefined, response: HistoryItem): QuestionAnswer[] {
  const answers = (response.payload.response as { answers: Record<string, { answers: string[] }> }).answers
  const questions: Question[] = (request?.payload.questions as Question[] | undefined)
    ?? Object.keys(answers).map((id) => ({ id }))
  return questions.map((question) => {
    const values = (answers[question.id]?.answers ?? []).map((answer) => answer.replace(/^user_note:\s*/i, '').trim()).filter(Boolean)
    const hidden = values.length > 0 && (!request || question.isSecret === true)
    return {
      id: question.id,
      question: question.question?.trim() || question.header?.trim() || question.id,
      answers: hidden ? [] : values,
      hidden,
    }
  })
}
