import { describe, expect, it } from 'vitest'
import { formatRequestUserInputResultLines } from '../utils/requestUserInputToolDisplay'

describe('formatRequestUserInputResultLines', () => {
  it('maps question text to selected option and free-form answers', () => {
    const args = {
      questions: [
        {
          id: 'mode',
          question: 'Which mode should DotCraft use?',
          options: [{ label: 'Auto' }, { label: 'Manual' }]
        },
        {
          id: 'note',
          question: 'Anything to adjust?',
          options: [{ label: 'No' }, { label: 'Yes' }]
        }
      ]
    }
    const result = JSON.stringify({
      answers: {
        mode: { answers: ['Auto'] },
        note: { answers: ['user_note: Prefer the lighter UI'] }
      }
    })

    expect(formatRequestUserInputResultLines(args, result, 'en')).toEqual([
      { question: 'Which mode should DotCraft use?', answer: 'Auto' },
      { question: 'Anything to adjust?', answer: 'Prefer the lighter UI' }
    ])
  })

  it('falls back to ids and masks secret free-form answers', () => {
    const args = {
      questions: [
        {
          id: 'token',
          header: 'Token',
          question: 'Enter token',
          isSecret: true,
          options: [{ label: 'Use existing' }, { label: 'Enter manually' }]
        }
      ]
    }
    const result = JSON.stringify({
      answers: {
        token: { answers: ['user_note: sk-secret'] }
      }
    })

    expect(formatRequestUserInputResultLines(args, result, 'en')).toEqual([
      { question: 'Enter token', answer: 'Hidden' }
    ])
    expect(formatRequestUserInputResultLines(undefined, result, 'zh-Hans')).toEqual([
      { question: 'token', answer: 'sk-secret' }
    ])
  })
})
