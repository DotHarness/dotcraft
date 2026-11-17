import { describe, expect, it } from 'vitest'
import { createHarness, structured } from './threadTaskTestHelpers'

const ASK = { recap: 'Fixed the parser.', ask: 'Commit the fix?', actions: [{ label: 'Commit' }, { label: 'Not yet' }] }

describe('WrapUp', () => {
  it('shows the box for a composer turn and returns at once without any request', async () => {
    const harness = createHarness()
    harness.composerTurn('turn-1')

    const result = await harness.call(ASK, 'turn-1', 'WrapUp')

    expect(result?.success).toBe(true)
    expect(structured(result)).toEqual({ shown: true })
    expect(harness.calls()).toEqual([])
  })

  it('shows nothing for a recap alone', async () => {
    const harness = createHarness()
    harness.composerTurn('turn-1')

    const result = await harness.call({ recap: 'Answered the question.' }, 'turn-1', 'WrapUp')

    expect(result?.success).toBe(true)
    expect(structured(result)).toEqual({ shown: false })
  })

  it('shows nothing when nobody is watching the turn', async () => {
    const harness = createHarness()
    harness.composerTurn('turn-1', { triggerKind: 'automation' })

    const result = await harness.call(ASK, 'turn-1', 'WrapUp')

    expect(result?.success).toBe(true)
    expect(structured(result)).toEqual({ shown: false })
  })

  it.each([
    [{}],
    [{ recap: 'Done.', ask: 'Ship it?' }],
    [{ recap: 'Done.', ask: 'Ship it?', actions: [{ label: 'Yes' }, { label: 'No' }, { label: 'Later' }] }],
    [{ recap: 'Done.', ask: 'Ship it?', actions: [{ label: 'x'.repeat(25) }] }]
  ])('refuses arguments outside the limits: %j', async (args) => {
    const harness = createHarness()
    harness.composerTurn('turn-1')

    const result = await harness.call(args, 'turn-1', 'WrapUp')

    expect(result).toEqual(expect.objectContaining({ success: false, errorCode: 'InvalidArguments' }))
  })
})
