import { translate, type AppLocale } from '../../shared/locales'

interface RequestUserInputQuestionDisplay {
  id: string
  label: string
  isSecret: boolean
}

interface RequestUserInputAnswerValue {
  answers?: unknown
}

export interface RequestUserInputResultLine {
  question: string
  answer: string
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value != null && !Array.isArray(value)
}

function peelJsonStringWrapper(value: unknown): unknown {
  if (typeof value !== 'string') return value
  try {
    return JSON.parse(value) as unknown
  } catch {
    return value
  }
}

function parseResult(result: string | undefined): Record<string, RequestUserInputAnswerValue> | null {
  const trimmed = result?.trim() ?? ''
  if (!trimmed) return null

  try {
    const parsed = peelJsonStringWrapper(JSON.parse(trimmed) as unknown)
    if (!isRecord(parsed)) return null
    const answers = parsed.answers
    if (!isRecord(answers)) return null
    return answers as Record<string, RequestUserInputAnswerValue>
  } catch {
    return null
  }
}

function parseQuestions(args: Record<string, unknown> | undefined): RequestUserInputQuestionDisplay[] {
  const questions = args?.questions
  if (!Array.isArray(questions)) return []

  return questions.flatMap((entry) => {
    if (!isRecord(entry)) return []
    const id = typeof entry.id === 'string' ? entry.id.trim() : ''
    if (!id) return []
    const question = typeof entry.question === 'string' ? entry.question.trim() : ''
    const header = typeof entry.header === 'string' ? entry.header.trim() : ''
    return [{
      id,
      label: question || header || id,
      isSecret: entry.isSecret === true
    }]
  })
}

function stripUserNote(value: string): string | null {
  const match = value.match(/^user_note:\s*(.*)$/i)
  return match ? match[1]?.trim() ?? '' : null
}

function formatAnswer(
  raw: RequestUserInputAnswerValue | undefined,
  isSecret: boolean,
  locale: AppLocale
): string {
  const values = Array.isArray(raw?.answers)
    ? raw.answers
      .filter((value): value is string => typeof value === 'string')
      .map((value) => value.trim())
      .filter(Boolean)
    : []

  if (values.length === 0) {
    return translate(locale, 'toolCall.requestUserInput.noAnswer')
  }

  const displayValues = values.map((value) => {
    const note = stripUserNote(value)
    if (note == null) return value
    if (isSecret && note) return translate(locale, 'toolCall.requestUserInput.hiddenAnswer')
    return note
  }).filter(Boolean)

  return displayValues.length > 0
    ? displayValues.join(', ')
    : translate(locale, 'toolCall.requestUserInput.noAnswer')
}

export function formatRequestUserInputResultLines(
  args: Record<string, unknown> | undefined,
  result: string | undefined,
  locale: AppLocale
): RequestUserInputResultLine[] | null {
  const answers = parseResult(result)
  if (!answers) return null

  const questions = parseQuestions(args)
  const entries = questions.length > 0
    ? questions
    : Object.keys(answers).map((id) => ({ id, label: id, isSecret: false }))

  if (entries.length === 0) return null

  return entries.map((question) => ({
    question: question.label,
    answer: formatAnswer(answers[question.id], question.isSecret, locale)
  }))
}
