import { afterEach, describe, expect, it } from 'vitest'
import type { FakeComputer } from '../demo/fakeComputer'
import { createStudio } from '../demo/seed'
import { createHarness, waitFor, type Harness } from '../test/harness'
import { changesWorkspaceApproval, controlsOf, workspaceApprovalOf } from './threadConfig'

const harnesses: Harness[] = []

afterEach(() => {
  for (const harness of harnesses.splice(0)) harness.session.dispose()
})

describe('approval policy shown for a chat', () => {
  it('shows an explicit policy as set and anything else as the workspace default', () => {
    expect(controlsOf({ approvalPolicy: 'prompt' }, 'autoApprove').approvalPolicy).toBe('prompt')
    expect(controlsOf({ approvalPolicy: 'autoApprove' }, 'prompt').approvalPolicy).toBe('autoApprove')
    expect(controlsOf({ approvalPolicy: 'default' }, 'autoApprove').approvalPolicy).toBe('autoApprove')
    expect(controlsOf({ approvalPolicy: 'deny' }, 'autoApprove').approvalPolicy).toBe('autoApprove')
    expect(controlsOf({}, 'autoApprove').approvalPolicy).toBe('autoApprove')
    expect(controlsOf(null, 'prompt').approvalPolicy).toBe('prompt')
  })

  it('reads the workspace default from the effective configuration, ignoring key case', () => {
    expect(workspaceApprovalOf({ Permissions: { DefaultApprovalPolicy: 'autoApprove' } })).toBe('autoApprove')
    expect(workspaceApprovalOf({ permissions: { defaultApprovalPolicy: 'autoApprove' } })).toBe('autoApprove')
    expect(workspaceApprovalOf({ Permissions: { DefaultApprovalPolicy: 'default' } })).toBe('prompt')
    expect(workspaceApprovalOf({})).toBe('prompt')
  })

  it('treats a change of the setting or of its parent as a change of the workspace default', () => {
    expect(changesWorkspaceApproval(['Permissions.DefaultApprovalPolicy'])).toBe(true)
    expect(changesWorkspaceApproval(['Permissions'])).toBe(true)
    expect(changesWorkspaceApproval(['Permissions.SomethingElse', 'providers'])).toBe(false)
  })
})

describe('workspace approval default from the computer', () => {
  function studio(): FakeComputer {
    const computer = createStudio(new Date())
    computer.streamDelayMs = 1
    return computer
  }

  it('reads it once per project connection and follows config/changed', async () => {
    const computer = studio()
    const project = computer.projects.find((entry) => entry.name === 'dotcraft')!
    project.approvalDefault = 'autoApprove'
    const harness = createHarness([computer])
    harnesses.push(harness)
    await harness.session.boot()
    await waitFor(() => harness.computerState().link === 'online' && !harness.computerState().syncing)
    expect(harness.computerState().models[project.id].approvalDefault).toBe('autoApprove')
    const reads = computer.calls.filter((call) => call.method === 'config/read').length

    computer.setApprovalDefault(project.id, 'default')
    await waitFor(() => harness.computerState().models[project.id].approvalDefault === 'prompt')
    expect(computer.calls.filter((call) => call.method === 'config/read')).toHaveLength(reads + 1)
  })
})
