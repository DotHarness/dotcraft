import { mkdtempSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, describe, expect, it } from 'vitest'
import { ThreadTaskStore } from '../threadTasks/threadTaskStore'
import { createHarness, PARENT, structured } from './threadTaskTestHelpers'

const ASK = { recap: 'Fixed the parser.', ask: 'Commit the fix?', actions: [{ label: 'Commit' }, { label: 'Not yet' }] }

describe('WrapUp', () => {
  it('shows the box and returns at once without any request', async () => {
    const harness = createHarness()
    harness.composerTurn('turn-1')

    const result = await harness.call(ASK, 'turn-1', 'WrapUp')

    expect(result?.success).toBe(true)
    expect(structured(result)).toEqual({ shown: true })
    expect(harness.calls()).toEqual([])
  })

  it('shows nothing for a recap alone or a question without buttons', async () => {
    const harness = createHarness()

    expect(structured(await harness.call({ recap: 'Answered the question.' }, 'turn-1', 'WrapUp'))).toEqual({ shown: false })
    expect(structured(await harness.call({ recap: 'Done.', ask: 'Ship it?' }, 'turn-1', 'WrapUp'))).toEqual({ shown: false })
  })

  it('still shows a box past the suggested sizes', async () => {
    const harness = createHarness()

    const result = await harness.call({ recap: 'x'.repeat(400), ask: 'Ship it?', actions: [{ label: 'Yes' }, { label: 'No' }, { label: 'Later' }] }, 'turn-1', 'WrapUp')

    expect(result?.success).toBe(true)
    expect(structured(result)).toEqual({ shown: true })
  })

  it('refuses a call without a recap', async () => {
    const harness = createHarness()

    const result = await harness.call({ ask: 'Ship it?', actions: [{ label: 'Yes' }] }, 'turn-1', 'WrapUp')

    expect(result).toEqual(expect.objectContaining({ success: false, errorCode: 'InvalidArguments' }))
  })

  it('tells the agent a phone turn shows no box', async () => {
    const harness = createHarness()
    harness.composerTurn('turn-1', { originChannel: 'dotcraft-mobile' })

    const result = await harness.call({ ...ASK, blocked: true }, 'turn-1', 'WrapUp')

    expect(structured(result)).toEqual({ shown: false })
    expect(harness.store.snapshot(PARENT).question).toBeUndefined()
  })

  describe('blocked question', () => {
    let tempDir: string | null = null
    afterEach(() => {
      if (tempDir) rmSync(tempDir, { recursive: true, force: true })
      tempDir = null
    })

    it.each([
      { name: 'a recap alone', args: { recap: 'Finished.' }, shown: false },
      { name: 'a question without buttons', args: { recap: 'Finished.', ask: 'Commit?', blocked: true }, shown: false },
      { name: 'an explicitly non-blocking question', args: { ...ASK, blocked: false }, shown: true },
      { name: 'a question with blocked omitted', args: ASK, shown: true }
    ])('clears persisted waiting when replaced by $name', async ({ args, shown }) => {
      tempDir = mkdtempSync(join(tmpdir(), 'wrap-up-'))
      const filePath = join(tempDir, 'thread-tasks.json')
      const harness = createHarness({ filePath })
      harness.composerTurn('turn-1')
      await harness.call({ ...ASK, blocked: true }, 'turn-1', 'WrapUp')
      expect(harness.store.snapshot(PARENT).question).toBeDefined()

      const result = await harness.call(args, 'turn-1', 'WrapUp')

      expect(result?.success).toBe(true)
      expect(structured(result)).toEqual({ shown })
      expect(harness.store.snapshot(PARENT).question).toBeUndefined()
      expect(new ThreadTaskStore(filePath).snapshot(PARENT).question).toBeUndefined()
    })

    it('preserves the persisted question when a replacement fails', async () => {
      tempDir = mkdtempSync(join(tmpdir(), 'wrap-up-'))
      const filePath = join(tempDir, 'thread-tasks.json')
      const harness = createHarness({ filePath })
      harness.composerTurn('turn-1')
      await harness.call({ ...ASK, blocked: true }, 'turn-1', 'WrapUp')

      const result = await harness.call({ ask: 'Commit?', actions: [{ label: 'Commit' }] }, 'turn-1', 'WrapUp')

      expect(result?.success).toBe(false)
      const question = { turnId: 'turn-1', callId: 'call-turn-1' }
      expect(harness.store.snapshot(PARENT).question).toEqual(question)
      expect(new ThreadTaskStore(filePath).snapshot(PARENT).question).toEqual(question)
    })

    it('clears a recovered question when the successful call is suppressed on the phone', async () => {
      const harness = createHarness()
      harness.composerTurn('turn-1', { originChannel: 'dotcraft-mobile' })
      harness.store.setQuestion(PARENT, { turnId: 'turn-0', callId: 'earlier-call' })

      const result = await harness.call({ ...ASK, blocked: true }, 'turn-1', 'WrapUp')

      expect(structured(result)).toEqual({ shown: false })
      expect(harness.store.snapshot(PARENT).question).toBeUndefined()
    })

    it('keeps the chat waiting across a restart until the next turn starts', async () => {
      tempDir = mkdtempSync(join(tmpdir(), 'wrap-up-'))
      const filePath = join(tempDir, 'thread-tasks.json')
      const harness = createHarness({ filePath })
      harness.composerTurn('turn-1')

      await harness.call({ ...ASK, blocked: true }, 'turn-1', 'WrapUp')

      expect(new ThreadTaskStore(filePath).snapshot(PARENT).question).toEqual({ turnId: 'turn-1', callId: 'call-turn-1' })
      harness.composerTurn('turn-2')
      expect(harness.store.snapshot(PARENT).question).toBeUndefined()
      expect(new ThreadTaskStore(filePath).snapshot(PARENT).question).toBeUndefined()
    })
  })
})
