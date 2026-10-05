import type { UserInputQuestion } from '@dotcraft/sdk/contracts'
import { afterEach, describe, expect, it } from 'vitest'
import type { FakeComputer } from '../demo/fakeComputer'
import { createStudio } from '../demo/seed'
import { createHarness, waitFor, type Harness } from '../test/harness'
import { initialChoices, questionAnswers, waitingDecisions } from './decisions'
import type { ComputerState, PendingRequest } from './state'

const harnesses: Harness[] = []

afterEach(() => {
  for (const harness of harnesses.splice(0)) harness.session.dispose()
})

async function opened(title: string): Promise<{ computer: FakeComputer; harness: Harness; state: () => ComputerState; key: string }> {
  const computer = createStudio(new Date())
  computer.streamDelayMs = 1
  const harness = createHarness([computer])
  harnesses.push(harness)
  const state = () => harness.computerState()
  await harness.session.boot()
  await waitFor(() => state().link === 'online' && !state().syncing)
  const key = Object.values(state().chats).find((chat) => chat.title === title)!.key
  harness.link().openChat(key)
  await waitFor(() => state().details[key]?.loading === false)
  return { computer, harness, state, key }
}

function question(id: string, isOther = true): UserInputQuestion {
  return {
    id,
    header: id,
    question: `${id}?`,
    options: [
      { label: `${id} A`, description: '' },
      { label: `${id} B`, description: '' },
    ],
    isOther,
    isSecret: false,
  }
}

describe('question answers', () => {
  it('answers every question with its chosen option, defaulting to the first', () => {
    const questions = [question('one'), question('two')]
    const choices = initialChoices(questions)
    choices[1] = { option: 1, other: '' }
    expect(questionAnswers(questions, choices, 'Other')).toEqual({ one: ['one A'], two: ['two B'] })
  })

  it('sends typed Other text as a note and a bare Other without text', () => {
    const questions = [question('one'), question('two')]
    const choices = [
      { option: 2, other: '  Use yarn  ' },
      { option: 2, other: ' ' },
    ]
    expect(questionAnswers(questions, choices, 'Other')).toEqual({ one: ['user_note: Use yarn'], two: ['Other'] })
  })
})

describe('waiting decisions', () => {
  const approval: PendingRequest = { kind: 'approval', requestId: 'a', approvalType: 'shell', operation: 'ls', target: '', targetLabel: null, reason: '' }

  it('queues requests in arrival order and adds a pending plan confirmation last', () => {
    expect(waitingDecisions([approval], null).map((decision) => decision.requestId)).toEqual(['a'])
    expect(waitingDecisions([approval], 'turn_002').map((decision) => decision.kind)).toEqual(['approval', 'plan'])
    expect(waitingDecisions([], 'turn_002')).toEqual([{ kind: 'plan', requestId: 'plan:turn_002' }])
  })

  it('counts two requests waiting in one chat and keeps the turn waiting until both are answered', async () => {
    const { computer, harness, state, key } = await opened('Retune the sidebar icon motion')
    await waitFor(() => state().pending[key]?.length === 2)
    expect(state().pending[key].map((request) => request.kind)).toEqual(['approval', 'question'])

    harness.link().decide(key, 'approval_motion', 'once')
    await waitFor(() => computer.decisions.length === 1)
    expect(waitingDecisions(state().pending[key], null).map((decision) => decision.requestId)).toEqual(['question_motion'])
    expect(state().chats[key].runtime?.waitingOnInput).toBe(true)
  })
})

describe('dismissing a question', () => {
  it('answers a non-blocking question with no answers', async () => {
    const { computer, harness, state, key } = await opened('Retune the sidebar icon motion')
    await waitFor(() => state().pending[key]?.length === 2)
    const request = state().pending[key].find((entry) => entry.kind === 'question')!
    if (request.kind !== 'question') throw new Error('expected a question')
    expect(request.isBlocking).toBe(false)

    await harness.link().dismissQuestion(key, request)
    await waitFor(() => computer.answers.length === 1)
    expect(computer.answers[0]).toEqual({ requestId: 'question_motion', answers: {} })
    expect(computer.calls.some((call) => call.method === 'turn/interrupt')).toBe(false)
  })

  it('interrupts the turn for a blocking question and answers nothing', async () => {
    const { computer, harness, state, key } = await opened('Set up the docs site build')
    await waitFor(() => state().pending[key]?.length === 1)
    const request = state().pending[key][0]
    if (request.kind !== 'question') throw new Error('expected a question')
    expect(request.isBlocking).toBe(true)

    await harness.link().dismissQuestion(key, request)
    expect(computer.calls.filter((call) => call.method === 'turn/interrupt')).toHaveLength(1)
    await waitFor(() => state().chats[key].runtime?.running === false)
    expect(state().pending[key]).toBeUndefined()
    expect(computer.answers).toHaveLength(0)
  })
})

describe('plan confirmation', () => {
  it('sends nothing and reports the failure when the switch to agent fails', async () => {
    const { computer, harness, key } = await opened('Plan the phone pairing flow')
    computer.failingMethods.add('thread/mode/set')

    await expect(harness.link().implementPlan(key)).rejects.toThrow()
    expect(computer.calls.filter((call) => call.method === 'turn/start')).toHaveLength(0)
  })
})
