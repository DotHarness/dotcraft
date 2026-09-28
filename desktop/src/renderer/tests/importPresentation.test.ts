import { describe, expect, it } from 'vitest'
import { categoryLocation } from '../components/settings/panels/importPresentation'

const located = (category: string, sourcePath: string, targetPath: string) => ({ category, sourcePath, targetPath })

describe('import category location', () => {
  it('names the shared category folder under the home directory', () => {
    expect(categoryLocation([
      located('skills', '/home/me/.agent/skills/docx', '/home/me/.craft/skills/docx'),
      located('skills', '/home/me/.agent/skills/pdf', '/home/me/.craft/skills/pdf')
    ])).toEqual({ source: '~/.agent/skills', target: '~/.craft/skills' })
  })

  it('normalizes Windows paths, including nested commands', () => {
    expect(categoryLocation([
      located('commands', 'C:\\Users\\Me\\.agent\\commands\\deploy\\staging.md', 'C:\\Users\\Me\\.craft\\commands\\deploy\\staging.md'),
      located('commands', 'C:\\Users\\Me\\.agent\\commands\\review.md', 'C:\\Users\\Me\\.craft\\commands\\review.md')
    ])).toEqual({ source: '~/.agent/commands', target: '~/.craft/commands' })
  })

  it('shows project files relative to the workspace, ignoring Windows case differences', () => {
    expect(categoryLocation(
      [located('instructions', 'D:\\Work\\Project\\RULES.md', 'd:\\work\\project\\AGENTS.md')],
      'D:\\Work\\Project'
    )).toEqual({ source: 'RULES.md', target: 'AGENTS.md' })
  })

  it('drops a source side that only meets at the home directory', () => {
    expect(categoryLocation([
      located('skills', '/home/me/.agent/skills/a', '/home/me/.craft/skills/a'),
      located('skills', '/home/me/.agents/skills/b', '/home/me/.craft/skills/b')
    ])).toEqual({ source: null, target: '~/.craft/skills' })
  })
})
