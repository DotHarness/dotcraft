import { readFile, readdir } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const desktopRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const rendererRoot = path.join(desktopRoot, 'src/renderer')
const maximumLines = 800

async function collectFiles(directory, extension) {
  const entries = await readdir(directory, { withFileTypes: true })
  const files = []

  for (const entry of entries) {
    const absolute = path.join(directory, entry.name)
    if (entry.isDirectory()) files.push(...await collectFiles(absolute, extension))
    else if (absolute.endsWith(extension)) files.push(absolute)
  }

  return files
}

function relative(file) {
  return path.relative(desktopRoot, file).replaceAll('\\', '/')
}

const cssFiles = await collectFiles(rendererRoot, '.css')
const oversized = []

for (const file of cssFiles) {
  const source = await readFile(file, 'utf8')
  const lineCount = source.split(/\r?\n/).length - (source.endsWith('\n') ? 1 : 0)
  if (lineCount >= maximumLines) oversized.push(`${relative(file)} (${lineCount} lines)`)
}

const entryPath = path.join(rendererRoot, 'styles/index.css')
const entrySource = await readFile(entryPath, 'utf8')
const invalidEntryLines = entrySource
  .split(/\r?\n/)
  .filter((line) => line.trim() && !/^@import\s+["'][^"']+["'];$/.test(line.trim()))

const sourceFiles = [
  ...await collectFiles(rendererRoot, '.ts'),
  ...await collectFiles(rendererRoot, '.tsx'),
]
const entryImports = []
const legacyImports = []

for (const file of sourceFiles) {
  const source = await readFile(file, 'utf8')
  if (source.includes("'./styles/index.css'") || source.includes('"./styles/index.css"')) {
    entryImports.push(relative(file))
  }
  if (source.includes('styles/tokens.css')) legacyImports.push(relative(file))
}

const failures = []
if (oversized.length) failures.push(`CSS files at or above ${maximumLines} lines:\n${oversized.join('\n')}`)
if (invalidEntryLines.length) failures.push('styles/index.css must contain imports only')
if (entryImports.length !== 1 || entryImports[0] !== 'src/renderer/main.tsx') {
  failures.push(`styles/index.css must be imported once by main.tsx; found: ${entryImports.join(', ') || 'none'}`)
}
if (legacyImports.length) failures.push(`Legacy styles/tokens.css references: ${legacyImports.join(', ')}`)

if (failures.length) {
  console.error(failures.join('\n\n'))
  process.exitCode = 1
} else {
  const largest = await Promise.all(cssFiles.map(async (file) => ({
    file: relative(file),
    lines: (await readFile(file, 'utf8')).split(/\r?\n/).length - 1,
  })))
  largest.sort((left, right) => right.lines - left.lines)
  console.log(`CSS architecture check passed (${cssFiles.length} files; largest: ${largest[0].file}, ${largest[0].lines} lines).`)
}
