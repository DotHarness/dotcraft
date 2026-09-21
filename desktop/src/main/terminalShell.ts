import { statSync } from 'node:fs'
import { win32 } from 'node:path'

interface ShellProbe {
  platform: NodeJS.Platform
  env: NodeJS.ProcessEnv
  fileExists: (path: string) => boolean
}

function isFile(path: string): boolean {
  try {
    return statSync(path).isFile()
  } catch {
    return false
  }
}

export function resolveShellCommand(
  probe: ShellProbe = { platform: process.platform, env: process.env, fileExists: isFile }
): { shell: string; args: string[] } {
  if (probe.platform !== 'win32') {
    return { shell: probe.env.SHELL?.trim() || '/bin/bash', args: [] }
  }

  const env = (name: string): string | undefined =>
    Object.entries(probe.env).find(([key]) => key.toLowerCase() === name.toLowerCase())?.[1]
  const onPath = (name: string): string | undefined => {
    for (const entry of (env('PATH') || '').split(';')) {
      const directory = entry.trim().replace(/^"|"$/g, '')
      if (!directory) continue
      const candidate = win32.join(directory, name)
      if (probe.fileExists(candidate)) return candidate
    }
    return undefined
  }
  const programFiles = env('ProgramFiles')
  const systemRoot = env('SystemRoot')
  const candidates = [
    onPath('pwsh.exe'),
    programFiles && win32.join(programFiles, 'PowerShell', '7', 'pwsh.exe'),
    onPath('powershell.exe'),
    systemRoot && win32.join(systemRoot, 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe')
  ]
  const shell = candidates.find((candidate) => candidate && probe.fileExists(candidate))
  if (shell) return { shell, args: ['-NoLogo'] }
  return { shell: env('COMSPEC')?.trim() || 'cmd.exe', args: [] }
}
