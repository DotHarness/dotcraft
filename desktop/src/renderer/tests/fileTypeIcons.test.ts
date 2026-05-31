import { describe, expect, it } from 'vitest'
import {
  fileIconName,
  DEFAULT_FILE_ICON,
  DEFAULT_FOLDER_ICON,
  DEFAULT_FOLDER_OPEN_ICON
} from '../utils/fileTypeIcons'

describe('fileIconName', () => {
  it('maps common extensions to vscode-icons names', () => {
    expect(fileIconName('foo.ts')).toBe('vscode-icons:file-type-typescript')
    expect(fileIconName('a/b/Component.tsx')).toBe('vscode-icons:file-type-reactts')
    expect(fileIconName('Main.cs')).toBe('vscode-icons:file-type-csharp')
    expect(fileIconName('readme.md')).toBe('vscode-icons:file-type-markdown')
    expect(fileIconName('doc.pdf')).toBe('vscode-icons:file-type-pdf2')
    expect(fileIconName('logo.png')).toBe('vscode-icons:file-type-image')
  })

  it('handles compound and special filenames before extension lookup', () => {
    expect(fileIconName('types.d.ts')).toBe('vscode-icons:file-type-typescriptdef')
    expect(fileIconName('package.json')).toBe('vscode-icons:file-type-npm')
    expect(fileIconName('.gitignore')).toBe('vscode-icons:file-type-git')
    expect(fileIconName('Dockerfile')).toBe('vscode-icons:file-type-docker')
  })

  it('derives the basename from both slash styles', () => {
    expect(fileIconName('C:\\proj\\src\\app.py')).toBe('vscode-icons:file-type-python')
    expect(fileIconName('proj/src/app.go')).toBe('vscode-icons:file-type-go')
  })

  it('falls back to the default file icon for unknown or extension-less names', () => {
    expect(fileIconName('mystery.zzz')).toBe(DEFAULT_FILE_ICON)
    expect(fileIconName('noext')).toBe(DEFAULT_FILE_ICON)
    expect(fileIconName('')).toBe(DEFAULT_FILE_ICON)
  })

  it('returns folder icons for directories, with an opened variant', () => {
    expect(fileIconName('src', { dir: true })).toBe(DEFAULT_FOLDER_ICON)
    expect(fileIconName('src', { dir: true, expanded: true })).toBe(DEFAULT_FOLDER_OPEN_ICON)
  })
})
