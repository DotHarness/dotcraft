import { describe, expect, it } from 'vitest'
import { getSkillViewDisplay } from '../utils/skillViewToolDisplay'

describe('skillViewToolDisplay', () => {
  it('treats regular SkillView content as loaded', () => {
    expect(getSkillViewDisplay({ name: 'browser' }, '---\nname: browser\n---\n# Browser workflow')).toMatchObject({
      name: 'browser',
      loaded: true,
      message: ''
    })
  })

  it.each([
    [{}, 'Skill name is required.'],
    [{ name: 'missing-skill' }, "Skill 'missing-skill' not found."]
  ])('treats %j as a failed load', (args, result) => {
    expect(getSkillViewDisplay(args, result)).toMatchObject({ loaded: false, message: result })
  })
})
