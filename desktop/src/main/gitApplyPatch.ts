import { app } from 'electron'
import { promises as fs } from 'fs'
import { randomUUID } from 'crypto'
import * as path from 'path'
import type { GitApplyPatchOptions, GitApplyPatchResult } from '../shared/gitApply'
import { runGitCommand } from './gitCommand'

const MAX_PATCH_LENGTH = 20_000_000

export async function applyGitPatch(
  gitWorkspacePath: string,
  patchText: string,
  options: GitApplyPatchOptions
): Promise<GitApplyPatchResult> {
  if (typeof patchText !== 'string' || patchText.length > MAX_PATCH_LENGTH || patchText.trim() === '') {
    return { ok: false, code: 'invalid-patch', message: 'Patch text is empty or too large.' }
  }

  let prefix: string
  try {
    prefix = (await runGitCommand(gitWorkspacePath, ['rev-parse', '--show-prefix'])).stdout.trim()
  } catch (error) {
    return { ok: false, code: 'not-git-repo', message: error instanceof Error ? error.message : String(error) }
  }

  const patchFile = path.join(app.getPath('temp'), `dotcraft-patch-${process.pid}-${Date.now()}-${randomUUID()}.diff`)
  try {
    await fs.writeFile(patchFile, patchText, 'utf8')
    return await runGitCommand(gitWorkspacePath, [
      // The patch carries the file's exact bytes, so git must not convert line endings on write.
      '-c', 'core.autocrlf=false',
      '-c', 'core.eol=lf',
      'apply',
      '--whitespace=nowarn',
      // Git resolves `diff --git` paths from the repository root and silently skips paths outside the cwd.
      ...(prefix ? [`--directory=${prefix}`] : []),
      ...(options?.reverse === true ? ['-R'] : []),
      patchFile
    ]).then(
      (): GitApplyPatchResult => ({ ok: true }),
      (error: Error): GitApplyPatchResult => ({ ok: false, code: 'apply-failed', message: error.message })
    )
  } finally {
    // A leftover temp file must not turn an applied patch into a reported failure.
    await fs.rm(patchFile, { force: true }).catch(() => {})
  }
}
