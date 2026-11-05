import { describe, expect, it } from 'vitest'
import { parseChangedPaths } from '../workspaceGitChanges'
import { PathSearchIndex } from '../workspacePathSearch'

const workspace = new PathSearchIndex([
  'README.md',
  'package.json',
  'docs/README.md',
  'docs/intro.md',
  'docs/guides/setup.md',
  'docs/reference/api.md',
  'lib/render/adapters/media/README.md',
  'app/main/searchIndex.ts',
  'app/main/tests/searchIndex.test.ts'
].map((relativePath) => {
  const slash = relativePath.lastIndexOf('/')
  return { relativePath, name: relativePath.slice(slash + 1), dir: slash < 0 ? '' : relativePath.slice(0, slash) }
}))

function search(query: string, options: { includeDirectories?: boolean; boostedPaths?: Set<string> } = { includeDirectories: true }): string[] {
  return workspace.search(query, 10, options)
    .map((match) => match.kind === 'dir' ? `${match.relativePath}/` : match.relativePath)
}

describe('PathSearchIndex', () => {
  it('matches a query that spans folders and a file name', () => {
    expect(search('docs/readme')[0]).toBe('docs/README.md')
  })

  it('returns folders derived from indexed files', () => {
    expect(search('docs')[0]).toBe('docs/')
    expect(search('guides')).toContain('docs/guides/')
  })

  it('returns files only unless folders are requested', () => {
    expect(search('docs', {}).every((path) => !path.endsWith('/'))).toBe(true)
  })

  it('prefers files whose own name matches over deep folder matches', () => {
    expect(search('readme').slice(0, 2)).toEqual(['README.md', 'docs/README.md'])
  })

  it('lists a folder and its direct children when the query names the folder', () => {
    expect(search('docs/').slice(0, 5)).toEqual(['docs/', 'docs/guides/', 'docs/reference/', 'docs/intro.md', 'docs/README.md'])
  })

  it('lists top-level folders before files for an empty query', () => {
    expect(search('')).toEqual(['app/', 'docs/', 'lib/', 'package.json', 'README.md'])
  })

  it('ranks test files below matching source files', () => {
    expect(search('searchindex', {}).slice(0, 2)).toEqual(['app/main/searchIndex.ts', 'app/main/tests/searchIndex.test.ts'])
  })

  it('ranks boosted paths higher', () => {
    expect(search('readme', { includeDirectories: true, boostedPaths: new Set(['docs/README.md']) })[0]).toBe('docs/README.md')
  })
})

describe('parseChangedPaths', () => {
  it('keeps changed files inside the workspace relative to it', () => {
    const porcelain = [' M app/src/a.ts', '?? app/notes.md', 'M  other/b.ts'].join('\0') + '\0'
    expect([...parseChangedPaths(porcelain, 'app/')]).toEqual(['src/a.ts', 'notes.md'])
  })
})
