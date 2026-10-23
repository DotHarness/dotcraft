import { mkdtemp, rm, writeFile } from 'fs/promises'
import { tmpdir } from 'os'
import { join } from 'path'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { loginSetupChatGpt } from '../workspaceSetup'

const tempRoots: string[] = []

afterEach(async () => {
  await Promise.all(tempRoots.splice(0).map((path) => rm(path, { recursive: true, force: true })))
})

describe('setup sign-in bridge', () => {
  it('signs an unsaved provider draft in through stdin and keeps backend failure details', async () => {
    const runBackend = vi.fn().mockResolvedValue({ kind: 'success' })
    const result = await loginSetupChatGpt({
      provider: {
        id: ' chatgpt ',
        displayName: 'ChatGPT',
        protocol: 'openai-responses',
        apiKey: '',
        endPoint: '',
        authMethod: 'chatgptOAuth'
      }
    }, { runBackend })

    expect(result).toEqual({ kind: 'success' })
    const [args, stdin, timeoutMs] = runBackend.mock.calls[0]
    expect(args).toEqual(['auth', 'login', '--stdin'])
    expect(JSON.parse(stdin)).toMatchObject({ id: 'chatgpt', protocol: 'openai-responses', authMethod: 'chatgptOAuth' })
    expect(timeoutMs).toBe(15 * 60_000)

    runBackend.mockRejectedValueOnce(new Error('Authorization cancelled'))
    await expect(loginSetupChatGpt({
      provider: { id: 'chatgpt', displayName: '', protocol: 'openai-responses', apiKey: '', endPoint: '', authMethod: 'chatgptOAuth' }
    }, { runBackend })).resolves.toEqual({ kind: 'error', errorCode: 'provider_login_failed', errorMessage: 'Authorization cancelled' })
  })

  it('signs a saved provider in from its user configuration without putting secrets in argv', async () => {
    const root = await mkdtemp(join(tmpdir(), 'dotcraft-setup-auth-'))
    tempRoots.push(root)
    const userConfigPath = join(root, 'config.json')
    await writeFile(userConfigPath, JSON.stringify({
      Providers: { subscription: { Protocol: 'openai-responses', AuthMethod: 'chatgptOAuth', ApiKey: 'secret-key' } }
    }))
    const runBackend = vi.fn().mockResolvedValue({ kind: 'success' })

    await loginSetupChatGpt({ providerId: 'subscription' }, { userConfigPath, runBackend })

    const [args, stdin] = runBackend.mock.calls[0]
    expect(args).toEqual(['auth', 'login', '--stdin'])
    expect(args.join(' ')).not.toContain('secret-key')
    expect(JSON.parse(stdin)).toMatchObject({ id: 'subscription', authMethod: 'chatgptOAuth' })
  })
})
