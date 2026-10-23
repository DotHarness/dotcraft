import { describe, expect, it } from 'vitest'
import {
  fileIconName,
  DEFAULT_FILE_ICON,
  DEFAULT_FOLDER_ICON,
  DEFAULT_FOLDER_OPEN_ICON
} from '../utils/fileTypeIcons'

describe('fileIconName', () => {
  it('resolves special names before extensions, from either slash style, with folder and default fallbacks', () => {
    expect(fileIconName('a/b/Component.tsx')).toBe('vscode-icons:file-type-reactts')
    expect(fileIconName('types.d.ts')).toBe('vscode-icons:file-type-typescriptdef')
    expect(fileIconName('package.json')).toBe('vscode-icons:file-type-npm')
    expect(fileIconName('Dockerfile')).toBe('vscode-icons:file-type-docker')
    expect(fileIconName('C:\\proj\\src\\app.py')).toBe('vscode-icons:file-type-python')
    expect(fileIconName('mystery.zzz')).toBe(DEFAULT_FILE_ICON)
    expect(fileIconName('')).toBe(DEFAULT_FILE_ICON)
    expect(fileIconName('src', { dir: true })).toBe(DEFAULT_FOLDER_ICON)
    expect(fileIconName('src', { dir: true, expanded: true })).toBe(DEFAULT_FOLDER_OPEN_ICON)
  })
})
