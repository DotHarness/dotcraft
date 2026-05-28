import { describe, expect, it } from 'vitest'
import {
  buildSkillManageDiff,
  formatSkillManageLabel,
  getSkillManageDisplay,
  parseSkillManageResult
} from '../utils/skillManageToolDisplay'

describe('skillManageToolDisplay', () => {
  it('parses SkillManage JSON results', () => {
    const parsed = parseSkillManageResult(JSON.stringify({
      success: true,
      message: "Patched skill 'demo'.",
      replacementCount: 2
    }))

    expect(parsed).toMatchObject({
      success: true,
      message: "Patched skill 'demo'.",
      replacementCount: 2
    })
  })

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

  it('formats collapsed labels without exposing raw JSON', () => {
    expect(formatSkillManageLabel(
      { action: 'write_file', name: 'demo', filePath: 'scripts/check.ps1' },
      undefined,
      'en'
    )).toBe('Added skill file scripts/check.ps1')
  })

  it('builds a patch diff from oldString and newString', () => {
    const diff = buildSkillManageDiff(
      {
        action: 'patch',
        name: 'demo',
        oldString: 'before',
        newString: 'after'
      },
      JSON.stringify({ success: true, message: 'ok' }),
      'turn-1'
    )

    expect(diff?.filePath).toBe('demo/SKILL.md')
    expect(diff?.additions).toBe(1)
    expect(diff?.deletions).toBe(1)
    expect(diff?.diffHunks[0]?.lines.map((line) => line.content)).toEqual(['before', 'after'])
  })

  it('builds a supporting file diff from fileContent', () => {
    const diff = buildSkillManageDiff(
      {
        action: 'write_file',
        name: 'demo',
        filePath: 'scripts/check.ps1',
        fileContent: 'Write-Host ok'
      },
      JSON.stringify({ success: true, message: 'ok' }),
      'turn-1'
    )

    expect(diff?.filePath).toBe('demo/scripts/check.ps1')
    expect(diff?.isNewFile).toBe(true)
    expect(diff?.additions).toBe(1)
  })
})
