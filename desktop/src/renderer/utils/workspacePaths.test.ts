import { describe, expect, it } from 'vitest'
import { toAbsoluteWorkspacePath } from './workspacePaths'

describe('toAbsoluteWorkspacePath', () => {
  it('keeps absolute and UNC paths and resolves relative ones against the workspace', () => {
    for (const [filePath, expected] of [
      ['/home/dev/notes.md', '/home/dev/notes.md'],
      ['D:\\docs\\notes.md', 'D:\\docs\\notes.md'],
      ['\\\\server\\share\\file.txt', '\\\\server\\share\\file.txt'],
      ['src\\app.ts', 'C:/repo/src/app.ts']
    ]) {
      expect(toAbsoluteWorkspacePath('C:\\repo', filePath)).toBe(expected)
    }
  })
})
