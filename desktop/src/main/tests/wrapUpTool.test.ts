import { describe, expect, it } from 'vitest'
import { createHarness, structured } from './threadTaskTestHelpers'

const ASK = { recap: 'Fixed the parser.', ask: 'Commit the fix?', actions: [{ label: 'Commit' }, { label: 'Not yet' }] }

describe('WrapUp', () => {
  it('shows the box and returns at once without any request', async () => {
    const harness = createHarness()

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
})
