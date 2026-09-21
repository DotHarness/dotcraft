import { describe, expect, it } from 'vitest'
import { resolveShellCommand } from '../terminalShell'

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
  it.each([
    [[pwsh, installedPwsh, powershell, systemPowerShell], pwsh],
    [[installedPwsh, powershell, systemPowerShell], installedPwsh],
    [[powershell, systemPowerShell], powershell],
    [[systemPowerShell], systemPowerShell]
  ])('selects the first available PowerShell from %j', (files, expected) => {
    expect(resolveShellCommand({
      platform: 'win32', env, fileExists: (path) => files.includes(path)
    })).toEqual({ shell: expected, args: ['-NoLogo'] })
  })

  it('falls back to COMSPEC only without PowerShell', () => {
    expect(resolveShellCommand({ platform: 'win32', env, fileExists: () => false }))
      .toEqual({ shell: cmd, args: [] })
  })

  it('falls back to cmd without environment paths', () => {
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
