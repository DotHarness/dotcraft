import { useState } from 'react'
import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { ImportCandidate } from '@dotcraft/sdk/contracts'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ImportSelectionRows, importItemKey, selectionFor } from '../components/settings/panels/ImportSelectionRows'

const item = (sourceId: string, category: string, scope: string, state = 'new'): ImportCandidate => ({
  source: 'claude-code', sourceId, category, scope, state, fingerprint: 'hash', sourcePath: '/source',
  targetPath: '/target', title: sourceId, reason: '', fallbackText: '', cwd: '', updatedAt: '2026-09-28T00:00:00Z', turnCount: 0
})
const items = [item('skill', 'skills', 'user'), item('hook', 'hooks', 'user'), item('command', 'commands', 'workspace'), item('blocked', 'mcp', 'workspace', 'unsupported')]

function Fixture(): React.JSX.Element {
  const [selected, setSelected] = useState(new Set(items.filter(i => i.state === 'new').map(importItemKey)))
  return <LocaleProvider loadSettings={false}>
    <ImportSelectionRows items={items} selected={selected} onChange={setSelected} disabled={false} workspaceName="project" />
    <output data-testid="selection">{JSON.stringify(selectionFor(items, selected))}</output>
  </LocaleProvider>
}

function showProjectConfiguration(): void {
  const disclosure = screen.getByRole('button', { name: 'Show Current project configuration details' })
  expect(disclosure).toHaveAttribute('aria-expanded', 'false')
  fireEvent.click(disclosure)
  expect(screen.getByRole('button', { name: 'Hide Current project configuration details' })).toHaveAttribute('aria-expanded', 'true')
}

describe('import content selection', () => {
  it('keeps scope selections separate and exposes partially selected groups', () => {
    render(<Fixture />)
    fireEvent.click(screen.getByRole('checkbox', { name: 'Skills (1)' }))
    expect(screen.getByRole('checkbox', { name: 'Tools & setup' })).toBePartiallyChecked()
    expect(JSON.parse(screen.getByTestId('selection').textContent!)).toEqual({ all: false, user: ['hooks'], workspace: ['commands'], sessions: false })
    fireEvent.click(screen.getByRole('checkbox', { name: 'Tools & setup' }))
    expect(screen.getByRole('checkbox', { name: 'Skills (1)' })).toBeChecked()
    showProjectConfiguration()
    expect(screen.getByRole('checkbox', { name: 'Commands (1)' })).toBeChecked()
  })

  it('does not submit unsupported items even when selecting the whole project', () => {
    render(<Fixture />)
    showProjectConfiguration()
    expect(screen.getByRole('checkbox', { name: 'MCP servers (0)' })).toBeDisabled()
    fireEvent.click(screen.getByRole('checkbox', { name: 'Current project configuration' }))
    fireEvent.click(screen.getByRole('checkbox', { name: 'Current project configuration' }))
    expect(JSON.parse(screen.getByTestId('selection').textContent!).workspace).toEqual(['commands'])
  })
})
