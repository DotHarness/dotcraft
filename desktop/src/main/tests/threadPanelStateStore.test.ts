import { afterEach, describe, expect, it } from 'vitest'
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { tmpdir } from 'node:os'
import { ThreadPanelStateStore } from '../threadPanelStateStore'
import type { PanelCheckpoint } from '../../shared/viewer/panelState'

const directories: string[] = []
function fixture() {
  const dir = mkdtempSync(join(tmpdir(), 'dotcraft-panels-'))
  directories.push(dir)
  const path = join(dir, 'panels.json')
  return { path, store: new ThreadPanelStateStore(path) }
}
const scope = { projectKey: '/project-a', threadId: 'thread' }
function checkpoint(): PanelCheckpoint {
  return {
    version: 1,
    tabs: [{ id: 'terminal', kind: 'terminal', label: 'Shell', cwd: '/project-a', hasStarted: true,
      pid: 123, pendingRun: { command: 'should-not-run', env: { SECRET: 'not-saved' }, nonce: 1 } }],
    systemTabs: ['changes', 'plan'], activeTab: { kind: 'system', id: 'plan' },
    visible: true, fullView: true, width: 600
  }
}
afterEach(() => directories.splice(0).forEach(dir => rmSync(dir, { recursive: true, force: true })))

describe('Desktop panel checkpoints', () => {
  it('reconstructs durable routes and layout without storing terminal execution state', () => {
    const { path, store } = fixture()
    store.update(scope, checkpoint())
    const restored = new ThreadPanelStateStore(path).read(scope)
    expect(restored).toMatchObject({ activeTab: { kind: 'system', id: 'plan' }, visible: true, fullView: true, width: 600 })
    expect(restored?.tabs[0]).toMatchObject({ kind: 'terminal', cwd: '/project-a', hasStarted: false })
    expect(restored?.tabs[0]).not.toHaveProperty('pendingRun')
    expect(restored?.tabs[0]).not.toHaveProperty('pid')
    expect(readFileSync(path, 'utf8')).not.toContain('should-not-run')
    expect(readFileSync(path, 'utf8')).not.toContain('not-saved')
  })

  it('separates identical thread IDs by project and returns independent snapshots', () => {
    const { store } = fixture()
    store.update(scope, checkpoint())
    const other = { projectKey: '/project-b', threadId: scope.threadId }
    store.update(other, { ...checkpoint(), visible: false })
    const read = store.read(scope)!
    read.tabs.length = 0
    expect(store.read(scope)?.tabs).toHaveLength(1)
    expect(store.read(scope)?.visible).toBe(true)
    expect(store.read(other)?.visible).toBe(false)
  })

  it('removes a closed scope without clearing another project', () => {
    const { path, store } = fixture()
    const other = { projectKey: '/project-b', threadId: scope.threadId }
    store.update(scope, checkpoint())
    store.update(other, checkpoint())
    store.update(scope, null)
    const restored = new ThreadPanelStateStore(path)
    expect(restored.read(scope)).toBeNull()
    expect(restored.read(other)?.tabs).toHaveLength(1)
  })

  it('does not resurrect a deleted thread from a late checkpoint write', () => {
    const { path, store } = fixture()
    store.update(scope, checkpoint())
    store.removeThread(scope)
    expect(() => store.update(scope, checkpoint())).toThrow('deleted')
    expect(new ThreadPanelStateStore(path).read(scope)).toBeNull()
  })

  it.each(['not json', JSON.stringify({ version: 9, panels: {} })])('ignores unreadable or unknown data', content => {
    const { path } = fixture()
    writeFileSync(path, content)
    expect(new ThreadPanelStateStore(path).read(scope)).toBeNull()
  })

  it('rejects invalid checkpoint writes without removing the last valid state', () => {
    const { store } = fixture()
    store.update(scope, checkpoint())
    expect(() => store.update(scope, { version: 99 })).toThrow('Invalid panel checkpoint')
    expect(store.read(scope)?.visible).toBe(true)
  })
})
