import { describe, expect, it } from 'vitest'
import {
  buildSkillManageDiff,
  formatSkillManageLabel,
  getSkillManageDisplay
} from '../utils/skillManageToolDisplay'

describe('skillManageToolDisplay', () => {
  it('detects variant updates from the result message', () => {
    const display = getSkillManageDisplay(
      { action: 'patch', name: 'demo' },
      JSON.stringify({
        success: true,
        message: "Patched skill 'demo'. The original skill was not modified."
      })
    )

    expect(display.variantUpdated).toBe(true)
  })

  it('accepts only exact action wire values', () => {
    expect(getSkillManageDisplay({ action: 'PATCH', name: 'demo' }, undefined).action).toBeNull()
    expect(getSkillManageDisplay({ action: 'patch', name: 'demo' }, undefined).action).toBe('patch')
  })

  it('formats collapsed labels without exposing raw JSON', () => {
    expect(formatSkillManageLabel(
      { action: 'write_file', name: 'demo', filePath: 'scripts/check.ps1' },
      undefined,
      'en'
    )).toBe('Added skill file scripts/check.ps1')
  })

  it('builds diffs from a patch or from supporting file content', () => {
    const diff = buildSkillManageDiff(
      {
        action: 'patch',
        name: 'demo',
        oldString: 'before',
        newString: 'after'
      },
      JSON.stringify({ success: true, message: 'ok' })
    )

    expect(diff?.filePath).toBe('demo/SKILL.md')
    expect(diff?.additions).toBe(1)
    expect(diff?.deletions).toBe(1)
    expect(diff?.diffHunks[0]?.lines.map((line) => line.content)).toEqual(['before', 'after'])

    const fileDiff = buildSkillManageDiff(
      {
        action: 'write_file',
        name: 'demo',
        filePath: 'scripts/check.ps1',
        fileContent: 'Write-Host ok'
      },
      JSON.stringify({ success: true, message: 'ok' })
    )

    expect(fileDiff).toMatchObject({ filePath: 'demo/scripts/check.ps1', isNewFile: true, additions: 1 })
  })
})
