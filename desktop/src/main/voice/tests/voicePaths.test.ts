import { join } from 'path'
import { describe, expect, it } from 'vitest'

import { resolveVoiceRoot } from '../voicePaths'

describe('resolveVoiceRoot', () => {
  it('keeps managed voice assets in the global DotCraft cache', () => {
    const home = join('users', 'example')

    expect(resolveVoiceRoot(home)).toBe(join(home, '.craft', 'cache', 'voice'))
  })
})
