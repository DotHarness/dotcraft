import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useAutomationsStore, type AutomationDefinition } from '../stores/automationsStore'
import { validSchedule } from '../components/automations/AutomationScheduleEditor'
import { parseAutomationResult } from '../components/conversation/AutomationToolCard'
import { stageAutomationCreationInWelcome } from '../components/automations/automationDraft'
import { useUIStore } from '../stores/uiStore'
import { ensureScheduleTimeZone } from '../utils/automationTimeZone'
const definition: AutomationDefinition = { id: 'a', version: 3, name: 'Check CI', prompt: 'Check build results', status: 'active', executionMode: 'thread', targetThreadId: 't', workspaceMode: 'project', approvalPolicy: 'workspaceScope', notificationPolicy: 'important', schedule: { kind: 'every', everyMs: 60000 }, createdAt: '2026-09-08T00:00:00Z', updatedAt: '2026-09-08T00:00:00Z' }
const sendRequest = vi.fn()
beforeEach(() => {
  Object.defineProperty(globalThis, 'window', { configurable: true, value: {} })
  Object.defineProperty(window, 'api', { configurable: true, value: { appServer: { sendRequest } } })
  sendRequest.mockReset()
  useAutomationsStore.setState({ automations: [definition], runs: {}, selectedAutomationId: 'a' })
  useAutomationsStore.setState({ pendingActions: {} })
})
describe('unified automations', () => {
  it('stages the automation skill in Welcome before conversation creation', () => {
    useUIStore.setState({ activeMainView: 'automations', welcomeDraft: null })

    stageAutomationCreationInWelcome('Set up a weekly review.')

    const state = useUIStore.getState()
    expect(state.activeMainView).toBe('conversation')
    expect(state.welcomeDraft?.segments).toEqual([
      { type: 'skill', skillName: 'automations' },
      { type: 'text', value: ' Set up a weekly review.' }
    ])
    expect(state.welcomeDraft?.text).toContain('$automations')
  })
  it('sends optimistic concurrency and only editable fields', async () => {
    sendRequest.mockResolvedValue({ automation: { ...definition, version: 4 } })
    await useAutomationsStore.getState().save({ ...definition, name: 'Updated' }, definition)
    expect(sendRequest).toHaveBeenCalledWith('automation/update', expect.objectContaining({ automationId: 'a', expectedVersion: 3, automation: expect.objectContaining({ name: 'Updated', targetThreadId: 't' }) }))
    expect(sendRequest.mock.calls[0][1].automation).not.toHaveProperty('version')
    expect(sendRequest.mock.calls[0][1].automation).not.toHaveProperty('origin')
  })
  it('preserves definition on rejected writes', async () => {
    sendRequest.mockRejectedValue(new Error('Version conflict'))
    await expect(useAutomationsStore.getState().save({ ...definition, name: 'Draft' }, definition)).rejects.toThrow('Version conflict')
    expect(useAutomationsStore.getState().automations[0].name).toBe('Check CI')
  })
  it('deletes the definition without deleting conversation history', async () => {
    sendRequest.mockResolvedValue({ ok: true })
    await useAutomationsStore.getState().remove('a')
    expect(sendRequest).toHaveBeenCalledExactlyOnceWith('automation/delete', { automationId: 'a' })
    expect(useAutomationsStore.getState().automations).toEqual([])
  })
  it('records queued runs without treating acceptance as success', async () => {
    sendRequest.mockResolvedValue({ run: { id: 'r', automationId: 'a', status: 'queued', threadId: 't', turnId: 'turn' } })
    await useAutomationsStore.getState().run('a')
    expect(useAutomationsStore.getState().runs.a[0].status).toBe('queued')
    expect(useAutomationsStore.getState().automations[0].status).toBe('active')
  })
  it('validates weekly day selection and explicit time zones', () => {
    expect(validSchedule({ kind: 'weekly', hour: 9, minute: 0, timeZone: 'UTC', days: [] })).toBe(false)
    expect(validSchedule({ kind: 'weekly', hour: 9, minute: 0, timeZone: 'UTC', days: [1,5] })).toBe(true)
    expect(validSchedule({ kind: 'daily', hour: 9, minute: 0, timeZone: 'not-a-zone' })).toBe(false)
  })
  it('parses operation snapshots without requiring a live definition', () => {
    expect(parseAutomationResult(JSON.stringify({ operation: 'delete', automation: definition }))?.automation?.name).toBe('Check CI')
    expect(parseAutomationResult(JSON.stringify({ operation: 'complete', automation: definition }))).toBeNull()
    expect(parseAutomationResult('invalid')).toBeNull()
  })
  it('fills a missing calendar time zone and preserves a stored one', () => {
    const inferred = ensureScheduleTimeZone({ kind: 'daily', hour: 9, minute: 0 })
    expect(inferred.timeZone).toBeTruthy()
    expect(ensureScheduleTimeZone({ ...inferred, timeZone: 'America/New_York' }).timeZone).toBe('America/New_York')
    expect(ensureScheduleTimeZone({ kind: 'every', everyMs: 60000 })).not.toHaveProperty('timeZone')
  })
})

it('refreshes a selected cached definition and removes it on not found', async () => {
  sendRequest.mockResolvedValueOnce({ automation: { ...definition, version: 4 } })
  useAutomationsStore.getState().selectAutomation('a')
  await vi.waitFor(() => expect(useAutomationsStore.getState().automations[0].version).toBe(4))
  sendRequest.mockRejectedValueOnce({ code: -32051 })
  useAutomationsStore.getState().selectAutomation('a')
  await vi.waitFor(() => expect(useAutomationsStore.getState().automations).toEqual([]))
  expect(useAutomationsStore.getState().selectedAutomationId).toBe('a')
})

it('shares pending pause state, blocks duplicate requests, and keeps failure state unchanged', async () => {
  let reject!: (reason: Error) => void
  sendRequest.mockImplementationOnce(() => new Promise((_resolve, failure) => { reject = failure }))
  const first = useAutomationsStore.getState().setEnabled('a', false)
  expect(useAutomationsStore.getState().pendingActions.a).toBe(true)
  await useAutomationsStore.getState().setEnabled('a', false)
  expect(sendRequest).toHaveBeenCalledTimes(1)
  reject(new Error('offline'))
  await expect(first).rejects.toThrow('offline')
  expect(useAutomationsStore.getState().automations[0].status).toBe('active')
  expect(useAutomationsStore.getState().pendingActions.a).toBe(false)
  sendRequest.mockResolvedValueOnce({ automation: { ...definition, status: 'paused', version: 4, nextRunAt: null } })
  await useAutomationsStore.getState().setEnabled('a', false)
  expect(useAutomationsStore.getState().automations[0]).toMatchObject({ status: 'paused', nextRunAt: null })
  expect(sendRequest.mock.calls.every(([method]) => method === 'automation/update')).toBe(true)
})
