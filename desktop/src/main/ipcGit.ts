import { promises as fs } from 'fs'
import * as path from 'path'
import type { ForgeBranchPullResult, ForgeRefResult, ForgeRepo } from '../shared/forgeReferences'
import type { GitApplyPatchOptions } from '../shared/gitApply'
import type { GitHeadInspection } from '../shared/gitHead'
import { translate } from '../shared/locales'
import { forgeReferences } from './forgeReferences'
import { applyGitPatch } from './gitApplyPatch'
import { runGitCommand } from './gitCommand'
import { mainLocale, type WindowIpcContext } from './ipcBridgeContracts'
import {
  assertGitInspectionPath,
  assertGitWorkspacePath,
  assertPathWithinWorkspace,
  assertPathWithinWorkspaceOrChatFolder,
  resolveCommitFilePaths
} from './ipcBridgePaths'
interface GitBranchEntry {
  name: string
  current: boolean
}

interface GitBranchListResult {
  current: string | null
  detachedHead: string | null
  branches: GitBranchEntry[]
}

export function registerGitHandlers(context: WindowIpcContext): void {
  const { handleSafe, workspacePath, callbacks, owner } = context
  handleSafe('file:write', async (_event, absPath: string, content: string) => {
    const resolved = assertPathWithinWorkspace(absPath, workspacePath, mainLocale(callbacks))
    await fs.mkdir(path.dirname(resolved), { recursive: true })
    await fs.writeFile(resolved, content, 'utf-8')
  })

  handleSafe('file:read', async (_event, absPath: string): Promise<string> => {
    const resolved = assertPathWithinWorkspaceOrChatFolder(
      absPath,
      workspacePath,
      mainLocale(callbacks),
      owner.webContents.id
    )
    try {
      return await fs.readFile(resolved, 'utf-8')
    } catch (err: unknown) {
      const code = (err as NodeJS.ErrnoException)?.code
      if (code === 'ENOENT') return ''
      throw err
    }
  })

  handleSafe('file:exists', async (_event, absPath: string): Promise<boolean> => {
    const resolved = assertPathWithinWorkspaceOrChatFolder(
      absPath,
      workspacePath,
      mainLocale(callbacks),
      owner.webContents.id
    )
    try {
      await fs.access(resolved)
      return true
    } catch {
      return false
    }
  })

  handleSafe('git:commit', async (_event, wsPath: string, files: string[], message: string): Promise<string> => {
    const locale = mainLocale(callbacks)
    const gitWorkspacePath = assertGitWorkspacePath(wsPath, workspacePath, locale)
    if (!Array.isArray(files)) {
      throw new Error(translate(locale, 'ipc.noGitChangesToCommit'))
    }

    const commitFiles = await resolveCommitFilePaths(gitWorkspacePath, files, locale)
    if (commitFiles.length === 0) {
      throw new Error(translate(locale, 'ipc.noGitChangesToCommit'))
    }

    await runGitCommand(gitWorkspacePath, ['add', '--', ...commitFiles])
    const stagedDiff = await runGitCommand(
      gitWorkspacePath,
      ['diff', '--cached', '--quiet', '--', ...commitFiles],
      [0, 1]
    )
    if (stagedDiff.exitCode === 0) {
      throw new Error(translate(locale, 'ipc.noGitChangesToCommit'))
    }

    const commit = await runGitCommand(gitWorkspacePath, ['commit', '-m', message, '--', ...commitFiles])
    return commit.stdout.trim()
  })
  handleSafe('git:applyPatch', async (_event, wsPath: string, patchText: string, options: GitApplyPatchOptions) =>
    applyGitPatch(assertGitWorkspacePath(wsPath, workspacePath, mainLocale(callbacks)), patchText, options)
  )
  handleSafe('git:branch', async (_event, wsPath: string): Promise<string | null> => {
    const locale = mainLocale(callbacks)
    const gitWorkspacePath = assertGitWorkspacePath(wsPath, workspacePath, locale)
    try {
      await runGitCommand(gitWorkspacePath, ['rev-parse', '--is-inside-work-tree'])
      const branch = (await runGitCommand(gitWorkspacePath, ['branch', '--show-current'])).stdout.trim()
      if (branch) return branch
      const head = (await runGitCommand(gitWorkspacePath, ['rev-parse', '--short', 'HEAD'])).stdout.trim()
      return head || null
    } catch {
      return null
    }
  })
  handleSafe('git:inspectHead', async (_event, wsPath: string): Promise<GitHeadInspection> => {
    const locale = mainLocale(callbacks)
    const gitWorkspacePath = assertGitInspectionPath(
      wsPath,
      workspacePath,
      callbacks.getLocalProjectPaths(),
      locale,
      owner.webContents.id
    )
    try {
      await runGitCommand(gitWorkspacePath, ['rev-parse', '--is-inside-work-tree'])
      const branch = (await runGitCommand(gitWorkspacePath, ['branch', '--show-current'])).stdout.trim()
      if (branch) return { kind: 'branch', label: branch }
      const head = (await runGitCommand(gitWorkspacePath, ['rev-parse', '--short', 'HEAD'])).stdout.trim()
      return head ? { kind: 'detached', label: head } : { kind: 'none' }
    } catch {
      return { kind: 'none' }
    }
  })
  handleSafe(
    'forge:getReferenceSummary',
    (_event, target: unknown): Promise<ForgeRefResult> => forgeReferences.getReferenceSummary(target)
  )
  handleSafe(
    'forge:getBranchPullRequest',
    (_event, wsPath: string, branch: string): Promise<ForgeBranchPullResult> =>
      forgeReferences.getBranchPullRequest(
        assertGitInspectionPath(
          wsPath,
          workspacePath,
          callbacks.getLocalProjectPaths(),
          mainLocale(callbacks),
          owner.webContents.id
        ),
        branch
      )
  )
  handleSafe(
    'forge:getOriginBranchPullRequest',
    (_event, originUrl: string, branch: string): Promise<ForgeBranchPullResult> =>
      forgeReferences.getOriginBranchPullRequest(originUrl, branch)
  )
  handleSafe(
    'forge:getWorkspaceRepo',
    (_event, wsPath: string): Promise<ForgeRepo | null> =>
      forgeReferences.getWorkspaceRepo(
        assertGitInspectionPath(
          wsPath,
          workspacePath,
          callbacks.getLocalProjectPaths(),
          mainLocale(callbacks),
          owner.webContents.id
        )
      )
  )
  handleSafe(
    'forge:getAuthorAvatar',
    (_event, host: unknown, url: unknown): Promise<string | null> => forgeReferences.getAuthorAvatar(host, url)
  )
  handleSafe('git:listBranches', async (_event, wsPath: string): Promise<GitBranchListResult> => {
    const locale = mainLocale(callbacks)
    const gitWorkspacePath = assertGitWorkspacePath(wsPath, workspacePath, locale)
    await runGitCommand(gitWorkspacePath, ['rev-parse', '--is-inside-work-tree'])
    const current = (await runGitCommand(gitWorkspacePath, ['branch', '--show-current'])).stdout.trim() || null
    const detachedHead = current
      ? null
      : (await runGitCommand(gitWorkspacePath, ['rev-parse', '--short', 'HEAD'])).stdout.trim() || null
    const refs = await runGitCommand(gitWorkspacePath, [
      'for-each-ref',
      '--sort=refname',
      '--format=%(refname:short)',
      'refs/heads'
    ])
    const branches = refs.stdout
      .split(/\r?\n/)
      .map((name) => name.trim())
      .filter((name) => name.length > 0)
      .map((name) => ({ name, current: current === name }))
    return { current, detachedHead, branches }
  })
  handleSafe('git:checkoutBranch', async (_event, wsPath: string, branchName: string): Promise<void> => {
    const locale = mainLocale(callbacks)
    const gitWorkspacePath = assertGitWorkspacePath(wsPath, workspacePath, locale)
    const branch = typeof branchName === 'string' ? branchName.trim() : ''
    if (!branch) throw new Error('Branch name is required.')
    await runGitCommand(gitWorkspacePath, ['switch', branch])
  })
  handleSafe('git:createAndCheckoutBranch', async (_event, wsPath: string, branchName: string): Promise<void> => {
    const locale = mainLocale(callbacks)
    const gitWorkspacePath = assertGitWorkspacePath(wsPath, workspacePath, locale)
    const branch = typeof branchName === 'string' ? branchName.trim() : ''
    if (!branch) throw new Error('Branch name is required.')
    await runGitCommand(gitWorkspacePath, ['check-ref-format', '--branch', branch])
    await runGitCommand(gitWorkspacePath, ['switch', '-c', branch])
  })
}
