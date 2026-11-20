import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'
import { parseLaunchFile } from '../devServers/launchFile'
import { argsForPort } from '../devServers/ports'

const folder = resolve('/workspace')

function parse(configurations: unknown[], extra: Record<string, unknown> = {}) {
  return parseLaunchFile(JSON.stringify({ version: '0.0.1', configurations, ...extra }), folder)
}

describe('parseLaunchFile', () => {
  it('builds commands from runtimeExecutable, program, and their arguments', () => {
    const parsed = parse([
      { name: 'web', runtimeExecutable: 'npm', runtimeArgs: ['run', 'dev'], port: 5173 },
      { name: 'api', program: 'server.js', args: ['--verbose'], cwd: 'api', port: 8080, autoPort: false, env: { A: '1' } }
    ])
    expect(parsed).toMatchObject({
      status: 'ok',
      autoVerify: true,
      configurations: [
        { kind: 'command', name: 'web', command: 'npm', args: ['run', 'dev'], cwd: folder, port: 5173, autoPort: undefined },
        { kind: 'command', name: 'api', command: 'node', args: ['server.js', '--verbose'], cwd: resolve(folder, 'api'), autoPort: false, env: { A: '1' } }
      ]
    })
  })

  it('reads the port from env.PORT or a port argument when "port" is absent', () => {
    const parsed = parse([
      { name: 'env', runtimeExecutable: 'serve', env: { PORT: '4000' } },
      { name: 'flag', runtimeExecutable: 'serve', runtimeArgs: ['--port', '4001'] },
      { name: 'joined', runtimeExecutable: 'serve', runtimeArgs: ['--port=4002'] }
    ])
    expect(parsed.status === 'ok' && parsed.configurations.map((entry) => 'port' in entry && entry.port)).toEqual([4000, 4001, 4002])
  })

  it('skips malformed configurations by name and keeps the rest', () => {
    const parsed = parse([
      { name: 'quoted', runtimeExecutable: 'serve', port: 1, autoPort: 'true' },
      { name: 'escape', runtimeExecutable: 'serve', port: 1, cwd: '../elsewhere' },
      { name: 'noport', runtimeExecutable: 'serve' },
      { name: 'empty' },
      { name: 'good', runtimeExecutable: 'serve', port: 3000 },
      { name: 'GOOD', runtimeExecutable: 'serve', port: 3001 }
    ])
    expect(parsed.status).toBe('ok')
    if (parsed.status !== 'ok') return
    expect(parsed.configurations.map((entry) => entry.name)).toEqual(['good'])
    expect(parsed.skipped.map((entry) => entry.name)).toEqual(['quoted', 'escape', 'noport', 'empty', 'GOOD'])
  })

  it('accepts attach configurations and rejects local addresses with a path', () => {
    const parsed = parse([
      { name: 'docs', url: 'http://localhost:4000' },
      { name: 'deep', url: 'http://localhost:4000/app' },
      { name: 'remote', url: 'https://example.com/app' }
    ])
    expect(parsed.status === 'ok' && parsed.configurations).toEqual([
      { kind: 'attach', name: 'docs', url: 'http://localhost:4000', port: 4000 },
      { kind: 'attach', name: 'remote', url: 'https://example.com/app' }
    ])
  })

  it('needs attention when the file cannot be used', () => {
    expect(parseLaunchFile('{', folder).status).toBe('invalid')
    expect(parse([]).status).toBe('invalid')
    const allSkipped = parse([{ name: 'empty' }])
    expect(allSkipped.status === 'invalid' && allSkipped.reason).toContain('empty')
    expect(parse([{ name: 'a', runtimeExecutable: 'x', port: 1 }], { autoVerify: false })).toMatchObject({ autoVerify: false })
  })
})

describe('argsForPort', () => {
  it('rewrites port arguments, or passes the port to package scripts', () => {
    expect(argsForPort('serve', ['--port', '3000', '-x'], 3001)).toEqual(['--port', '3001', '-x'])
    expect(argsForPort('serve', ['--port=3000'], 3001)).toEqual(['--port=3001'])
    expect(argsForPort('npm', ['run', 'dev'], 3001)).toEqual(['run', 'dev', '--', '--port', '3001'])
    expect(argsForPort('pnpm', ['run', 'dev'], 3001)).toEqual(['run', 'dev', '--port', '3001'])
    expect(argsForPort('node', ['server.js'], 3001)).toEqual(['server.js'])
  })
})
