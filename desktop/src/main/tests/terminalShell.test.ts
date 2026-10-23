import { describe, expect, it } from 'vitest'
import { composeShellCommandLine, resolveShellCommand } from '../terminalShell'

const pwsh = 'D:\\Portable Shell\\pwsh.exe'
const installedPwsh = 'D:\\Program Files\\PowerShell\\7\\pwsh.exe'
const powershell = 'D:\\Portable Shell\\powershell.exe'
const systemPowerShell = 'D:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe'
const cmd = 'D:\\Windows\\System32\\cmd.exe'
const env = {
  Path: '"D:\\Portable Shell";D:\\Other',
  ProgramFiles: 'D:\\Program Files',
  SystemRoot: 'D:\\Windows',
  COMSPEC: cmd
}

describe('Windows terminal shell discovery', () => {
  it('prefers PowerShell from PATH, then installed and system PowerShell, then COMSPEC and cmd', () => {
    const cases: Array<[string[], string]> = [
      [[pwsh, installedPwsh, powershell, systemPowerShell], pwsh],
      [[installedPwsh, powershell, systemPowerShell], installedPwsh],
      [[powershell, systemPowerShell], powershell],
      [[systemPowerShell], systemPowerShell]
    ]
    for (const [files, expected] of cases) {
      expect(resolveShellCommand({
        platform: 'win32', env, fileExists: (path) => files.includes(path)
      })).toEqual({ shell: expected, args: ['-NoLogo'] })
    }
    expect(resolveShellCommand({ platform: 'win32', env, fileExists: () => false }))
      .toEqual({ shell: cmd, args: [] })
    expect(resolveShellCommand({ platform: 'win32', env: {}, fileExists: () => false }))
      .toEqual({ shell: 'cmd.exe', args: [] })
  })

  it('preserves Unix SHELL and bash fallback', () => {
    expect(resolveShellCommand({ platform: 'linux', env: { SHELL: '/bin/zsh' }, fileExists: () => false }))
      .toEqual({ shell: '/bin/zsh', args: [] })
    expect(resolveShellCommand({ platform: 'darwin', env: {}, fileExists: () => false }))
      .toEqual({ shell: '/bin/bash', args: [] })
  })
})

describe('shell command lines', () => {
  it('enters the command in each shell syntax after changing into the folder', () => {
    expect(composeShellCommandLine('C:\\Program Files\\PowerShell\\7\\pwsh.exe', "C:\\it's here", 'npm test'))
      .toBe("Set-Location -LiteralPath 'C:\\it''s here'; if ($?) { npm test }")
    expect(composeShellCommandLine('powershell.exe', 'C:\\repo', 'dotnet build'))
      .toBe("Set-Location -LiteralPath 'C:\\repo'; if ($?) { dotnet build }")
    expect(composeShellCommandLine('C:\\Windows\\System32\\cmd.exe', 'D:\\my repo', 'make'))
      .toBe('cd /d "D:\\my repo" && make')
    expect(composeShellCommandLine('/bin/zsh', '/home/me/"q"/$x', 'npm start'))
      .toBe('cd "/home/me/\\"q\\"/\\$x" && npm start')
  })
})
