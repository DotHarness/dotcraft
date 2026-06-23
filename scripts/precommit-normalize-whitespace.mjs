#!/usr/bin/env node

import { spawnSync } from 'node:child_process'
import { existsSync, lstatSync, readFileSync, writeFileSync } from 'node:fs'
import path from 'node:path'

const SAFE_EXTENSIONS = new Set([
  '.bat',
  '.cmd',
  '.cs',
  '.csproj',
  '.css',
  '.editorconfig',
  '.gitattributes',
  '.gitignore',
  '.html',
  '.js',
  '.json',
  '.jsonc',
  '.jsx',
  '.mjs',
  '.props',
  '.ps1',
  '.sh',
  '.sln',
  '.targets',
  '.toml',
  '.ts',
  '.tsx',
  '.xml',
  '.yaml',
  '.yml',
])

const SAFE_FILENAMES = new Set([
  '.dockerignore',
  '.editorconfig',
  '.gitattributes',
  '.gitignore',
  '.npmrc',
  '.yarnrc',
])

function runGit(args, options = {}) {
  const result = spawnSync('git', args, {
    encoding: options.encoding ?? 'utf8',
    stdio: options.stdio ?? 'pipe',
  })

  if (result.error) {
    throw result.error
  }

  if (result.status !== 0) {
    const stderr = result.stderr?.toString() ?? ''
    const stdout = result.stdout?.toString() ?? ''
    throw new Error(`git ${args.join(' ')} failed\n${stderr || stdout}`.trim())
  }

  return result.stdout
}

function gitStatus(args) {
  const result = spawnSync('git', args, { encoding: 'utf8', stdio: 'pipe' })
  if (result.error) {
    throw result.error
  }

  return result
}

function parseNullSeparated(buffer) {
  return buffer
    .toString('utf8')
    .split('\0')
    .filter((entry) => entry.length > 0)
}

function isSafeTextPath(file) {
  const basename = path.basename(file)
  if (SAFE_FILENAMES.has(basename)) {
    return true
  }

  return SAFE_EXTENSIONS.has(path.extname(file).toLowerCase())
}

function hasUnstagedChanges(file) {
  const result = gitStatus(['diff', '--quiet', '--', file])
  if (result.status === 0) {
    return false
  }

  if (result.status === 1) {
    return true
  }

  const stderr = result.stderr?.toString() ?? ''
  throw new Error(`git diff --quiet failed for ${file}\n${stderr}`.trim())
}

function isBinary(buffer) {
  return buffer.includes(0)
}

function normalizeText(text) {
  return text
    .replace(/\r\n/g, '\n')
    .replace(/\r/g, '\n')
    .replace(/[ \t]+$/gm, '')
}

function runCachedWhitespaceCheck() {
  const result = gitStatus(['diff', '--cached', '--check'])
  if (result.status === 0) {
    return
  }

  process.stderr.write('pre-commit: git diff --cached --check found whitespace errors.\n')
  if (result.stdout) {
    process.stderr.write(result.stdout)
  }
  if (result.stderr) {
    process.stderr.write(result.stderr)
  }
  process.exit(result.status ?? 1)
}

function main() {
  const repoRoot = runGit(['rev-parse', '--show-toplevel']).trim()
  process.chdir(repoRoot)

  const stagedFiles = parseNullSeparated(
    runGit(['diff', '--cached', '--name-only', '--diff-filter=ACMR', '-z'], {
      encoding: 'buffer',
    }),
  )

  const safeFiles = stagedFiles.filter((file) => isSafeTextPath(file))
  const partiallyStaged = safeFiles.filter((file) => existsSync(file) && hasUnstagedChanges(file))
  if (partiallyStaged.length > 0) {
    process.stderr.write('pre-commit: cannot auto-normalize files that also have unstaged changes.\n')
    process.stderr.write('Stage, stash, or split these changes before committing:\n')
    for (const file of partiallyStaged) {
      process.stderr.write(`  - ${file}\n`)
    }
    process.exit(1)
  }

  const normalized = []
  for (const file of safeFiles) {
    if (!existsSync(file) || !lstatSync(file).isFile()) {
      continue
    }

    const buffer = readFileSync(file)
    if (isBinary(buffer)) {
      continue
    }

    const original = buffer.toString('utf8')
    const next = normalizeText(original)
    if (next === original) {
      continue
    }

    writeFileSync(file, next, 'utf8')
    runGit(['add', '--', file])
    normalized.push(file)
  }

  if (normalized.length > 0) {
    process.stderr.write('pre-commit: normalized whitespace in staged files:\n')
    for (const file of normalized) {
      process.stderr.write(`  - ${file}\n`)
    }
  }

  runCachedWhitespaceCheck()
}

try {
  main()
} catch (error) {
  process.stderr.write(`pre-commit: ${error instanceof Error ? error.message : String(error)}\n`)
  process.exit(1)
}
