import { execFile } from 'child_process'

export interface GitCommandResult {
  stdout: string
  stderr: string
  exitCode: number
}

interface ExecFileError extends Error {
  code?: number | string
}

export function runGitCommand(
  cwd: string,
  args: string[],
  allowedExitCodes: number[] = [0]
): Promise<GitCommandResult> {
  return new Promise((resolve, reject) => {
    execFile('git', args, { cwd }, (err, stdout, stderr) => {
      const execError = err as ExecFileError | null
      const exitCode = err ? (typeof execError?.code === 'number' ? execError.code : null) : 0
      if (exitCode !== null && allowedExitCodes.includes(exitCode)) {
        resolve({
          stdout: String(stdout),
          stderr: String(stderr),
          exitCode
        })
        return
      }
      if (err) {
        reject(new Error(String(stderr || err.message).trim()))
        return
      }
      resolve({
        stdout: String(stdout),
        stderr: String(stderr),
        exitCode: exitCode ?? 0
      })
    })
  })
}
