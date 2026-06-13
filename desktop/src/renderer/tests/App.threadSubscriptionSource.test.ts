import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const rendererRoot = resolve(__dirname, '..')

function readRendererFile(path: string): string {
  return readFileSync(resolve(rendererRoot, path), 'utf8').replace(/\r\n/g, '\n')
}

describe('App active thread subscription source', () => {
  it('waits for thread subscription readiness before restoring parked approvals', () => {
    const appSource = readRendererFile('App.tsx')

    expect(appSource).toContain('threadSubscriptionIntentRef')
    expect(appSource).toContain('threadSubscriptionInFlightRef')
    expect(appSource).toContain('threadSubscriptionReadyRef')
    expect(appSource).toContain('const subscriptionReady = ensureThreadSubscribed(requestedId)')
    expect(appSource).toContain("sendRequest('thread/subscribe', requestParams)")

    const subscribeIndex = appSource.indexOf('const subscriptionReady = ensureThreadSubscribed(requestedId)')
    const awaitIndex = appSource.indexOf('if (!await subscriptionReady)')
    const approvalsIndex = appSource.indexOf('consumeParkedApprovals(requestedId)')
    const inputIndex = appSource.indexOf('consumeParkedUserInput(requestedId)')

    expect(subscribeIndex).toBeGreaterThan(-1)
    expect(awaitIndex).toBeGreaterThan(subscribeIndex)
    expect(approvalsIndex).toBeGreaterThan(awaitIndex)
    expect(inputIndex).toBeGreaterThan(awaitIndex)
  })
})
