import { describe, expect, it, vi } from 'vitest'
import { TokenMulticastDispatcher } from '../../preload/notificationDispatcher'

interface TestNotificationPayload {
  method: string
  params?: unknown
}

describe('preload notification dispatcher', () => {
  it('dispatches appserver notifications to multiple subscribers independently', () => {
    const errors: unknown[] = []
    const dispatcher = new TokenMulticastDispatcher<TestNotificationPayload>((error) => {
      errors.push(error)
    })
    const appSubscriber = vi.fn()
    const teamSubscriber = vi.fn()

    const unsubscribeApp = dispatcher.subscribe(appSubscriber)
    const unsubscribeTeam = dispatcher.subscribe(teamSubscriber)

    dispatcher.dispatch({ method: 'thread/runtimeChanged' })

    expect(appSubscriber).toHaveBeenCalledTimes(1)
    expect(teamSubscriber).toHaveBeenCalledTimes(1)
    expect(errors).toEqual([])

    unsubscribeTeam()
    dispatcher.dispatch({ method: 'turn/started' })

    expect(appSubscriber).toHaveBeenCalledTimes(2)
    expect(teamSubscriber).toHaveBeenCalledTimes(1)

    unsubscribeApp()
    dispatcher.dispatch({ method: 'item/completed' })

    expect(appSubscriber).toHaveBeenCalledTimes(2)
    expect(teamSubscriber).toHaveBeenCalledTimes(1)
  })

  it('visiting Team before enable does not clear App notification subscriber', () => {
    const dispatcher = new TokenMulticastDispatcher<TestNotificationPayload>()
    const appSubscriber = vi.fn()
    const teamSubscriber = vi.fn()

    dispatcher.subscribe(appSubscriber)
    const unsubscribeTeam = dispatcher.subscribe(teamSubscriber)

    unsubscribeTeam()
    dispatcher.dispatch({ method: 'turn/completed' })

    expect(appSubscriber).toHaveBeenCalledTimes(1)
    expect(teamSubscriber).not.toHaveBeenCalled()
  })

  it('continues dispatching when one subscriber throws', () => {
    const thrown = new Error('listener failed')
    const errors: unknown[] = []
    const dispatcher = new TokenMulticastDispatcher<TestNotificationPayload>((error) => {
      errors.push(error)
    })
    const healthySubscriber = vi.fn()

    dispatcher.subscribe(() => {
      throw thrown
    })
    dispatcher.subscribe(healthySubscriber)

    dispatcher.dispatch({ method: 'thread/queue/updated' })

    expect(errors).toEqual([thrown])
    expect(healthySubscriber).toHaveBeenCalledTimes(1)
  })
})
