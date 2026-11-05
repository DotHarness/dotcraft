import { promises as fs } from 'fs'
import * as path from 'path'
import { runGitCommand } from './gitCommand'

const REFRESH_INTERVAL_MS = 5_000

export class WorkspaceGitChanges {
  private paths: ReadonlySet<string> = new Set()
  private refreshedAt = 0
  private pending: Promise<void> | null = null

  constructor(private readonly root: string) {}

  current(): ReadonlySet<string> {
    if (!this.pending && Date.now() - this.refreshedAt >= REFRESH_INTERVAL_MS) {
      this.pending = this.refresh().finally(() => {
        this.pending = null
      })
    }
    return this.paths
  }

  private async refresh(): Promise<void> {
    let next: ReadonlySet<string> = new Set()
    try {
      // Spawning git outside a repository is wasted work, and on Windows it also locks the directory.
      const repoRoot = await findRepositoryRoot(this.root)
      if (repoRoot) {
        const prefix = path.relative(repoRoot, this.root).split(path.sep).filter(Boolean).map((part) => part + '/').join('')
        const status = await runGitCommand(this.root, [
          '-c', 'core.quotepath=false',
          'status', '--porcelain=v1', '-z', '--no-renames', '--untracked-files=all', '--', '.'
        ])
        next = parseChangedPaths(status.stdout, prefix)
      }
    } catch {}
    this.paths = next
    this.refreshedAt = Date.now()
  }
}

async function findRepositoryRoot(start: string): Promise<string | null> {
  for (let dir = start; ; dir = path.dirname(dir)) {
    try {
      await fs.stat(path.join(dir, '.git'))
      return dir
    } catch {
      if (path.dirname(dir) === dir) return null
    }
  }
}

export function parseChangedPaths(porcelain: string, prefix: string): ReadonlySet<string> {
  const paths = new Set<string>()
  for (const record of porcelain.split('\0')) {
    const repoPath = record.slice(3)
    if (repoPath.length > prefix.length && repoPath.startsWith(prefix)) paths.add(repoPath.slice(prefix.length))
  }
  return paths
}
