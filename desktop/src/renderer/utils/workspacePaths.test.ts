import { describe, expect, it } from 'vitest'
import { toAbsoluteWorkspacePath } from './workspacePaths'

describe('toAbsoluteWorkspacePath', () => {
  it.each([
    ['/home/dev/notes.md', '/home/dev/notes.md'],
    ['D:\\docs\\notes.md', 'D:\\docs\\notes.md'],
    ['\\\\server\\share\\file.txt', '\\\\server\\share\\file.txt'],
    ['src\\app.ts', 'C:/repo/src/app.ts']
  ])('resolves %s against the workspace as %s', (filePath, expected) => {
    expect(toAbsoluteWorkspacePath('C:\\repo', filePath)).toBe(expected)
  })
})
