import { join, resolve } from 'node:path'

export function workspaceTempPath(workspacePath: string): string {
  return join(resolve(workspacePath), '.craft', 'tmp')
}
