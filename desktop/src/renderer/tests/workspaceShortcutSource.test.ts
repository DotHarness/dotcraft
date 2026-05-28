import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const rendererRoot = resolve(__dirname, '..')

describe('workspace switch shortcut source', () => {
  it('returns to the welcome screen for Ctrl/Cmd+Shift+O instead of opening a folder picker', () => {
    const appSource = readFileSync(resolve(rendererRoot, 'App.tsx'), 'utf8')
    const block = appSource.match(/\/\/ Ctrl\+Shift\+O: switch workspace[\s\S]*?\/\/ Ctrl\+Shift\+N:/)?.[0]

    expect(block).toBeTruthy()
    expect(block).toContain('window.api.workspace.clearSelection()')
    expect(block).not.toContain('workspace.pickFolder')
    expect(block).not.toContain('workspace.switch')
  })
})
