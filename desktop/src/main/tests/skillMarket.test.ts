import { afterEach, describe, expect, it, vi } from 'vitest'
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'fs/promises'
import { existsSync } from 'fs'
import { tmpdir } from 'os'
import { join } from 'path'
import { zipSync } from 'fflate'
import {
  bindDotCraftSkillInstall,
  cleanupDotCraftSkillInstall,
  getSkillMarketDetail,
  installSkillFromMarket,
  normalizeArchive,
  prepareDotCraftSkillInstall,
  searchSkillMarket
} from '../skillMarket'

function jsonResponse(value: unknown): Response {
  return new Response(JSON.stringify(value), {
    status: 200,
    headers: { 'content-type': 'application/json' }
  })
}

function zipResponse(files: Record<string, string>): Response {
  const encoded: Record<string, Uint8Array> = {}
  for (const [filePath, content] of Object.entries(files)) {
    encoded[filePath] = new TextEncoder().encode(content)
  }
  const zip = zipSync(encoded)
  return new Response(Buffer.from(zip), {
    status: 200,
    headers: {
      'content-type': 'application/zip',
      'content-length': String(zip.byteLength)
    }
  })
}

describe('skillMarket', () => {
  let tempRoot = ''

  afterEach(async () => {
    if (tempRoot) {
      await rm(tempRoot, { recursive: true, force: true })
      tempRoot = ''
    }
  })

  it('normalizes SkillHub and ClawHub search responses', async () => {
    tempRoot = await mkdtemp(join(tmpdir(), 'dotcraft-skill-market-'))
    const fetcher = vi.fn(async (url: string) => {
      if (url.startsWith('https://api.skillhub.cn/')) {
        return jsonResponse({
          data: {
            skills: [
              {
                slug: 'baidu-search',
                name: 'Baidu Search',
                description: 'Search Baidu',
                version: '1.1.3',
                downloads: 12
              }
            ]
          }
        })
      }
      return jsonResponse({
        results: [
          {
            slug: 'git-helper',
            title: 'Git Helper',
            summary: 'Git workflow help',
            latestVersion: '2.0.0',
            downloadCount: 34
          }
        ]
      })
    }) as typeof fetch

    const result = await searchSkillMarket(tempRoot, { query: 'git', provider: 'all' }, fetcher)

    expect(result.skills).toEqual([
      expect.objectContaining({
        provider: 'skillhub',
        slug: 'baidu-search',
        name: 'Baidu Search',
        version: '1.1.3',
        downloads: 12,
        installed: false
      }),
      expect.objectContaining({
        provider: 'clawhub',
        slug: 'git-helper',
        name: 'Git Helper',
        version: '2.0.0',
        downloads: 34,
        installed: false
      })
    ])
  })

  it('returns partial search results when one provider fails', async () => {
    tempRoot = await mkdtemp(join(tmpdir(), 'dotcraft-skill-market-'))
    const fetcher = vi.fn(async (url: string) => {
      if (url.startsWith('https://api.skillhub.cn/')) {
        return new Response('busy', { status: 429 })
      }
      return jsonResponse({
        results: [{ slug: 'git-helper', name: 'Git Helper' }]
      })
    }) as typeof fetch

    const result = await searchSkillMarket(tempRoot, { query: 'git', provider: 'all' }, fetcher)

    expect(result.skills).toEqual([
      expect.objectContaining({
        provider: 'clawhub',
        slug: 'git-helper',
        name: 'Git Helper'
      })
    ])
  })

  it('loads SKILL.md from the provider file endpoint for detail preview', async () => {
    tempRoot = await mkdtemp(join(tmpdir(), 'dotcraft-skill-market-'))
    const fetcher = vi.fn(async (url: string) => {
      if (url.includes('/file?')) {
        return new Response('# Git Helper\n\nUse git safely.', {
          status: 200,
          headers: { 'content-type': 'text/markdown' }
        })
      }
      return jsonResponse({
        skill: {
          slug: 'git-helper',
          displayName: 'Git Helper',
          summary: 'Git workflow help'
        },
        latestVersion: { version: '1.0.0' }
      })
    }) as typeof fetch

    const detail = await getSkillMarketDetail(
      tempRoot,
      { provider: 'clawhub', slug: 'git-helper' },
      fetcher
    )

    expect(fetcher).toHaveBeenCalledWith(
      expect.stringContaining('https://clawhub.ai/api/v1/skills/git-helper/file?'),
      expect.any(Object)
    )
    expect(detail.readme).toContain('# Git Helper')
    expect(detail.description).toBe('Git workflow help')
    expect(detail.version).toBe('1.0.0')
  })

  it('normalizes nested SkillHub detail metadata', async () => {
    tempRoot = await mkdtemp(join(tmpdir(), 'dotcraft-skill-market-'))
    const fetcher = vi.fn(async (url: string) => {
      if (url.includes('/file?')) {
        return new Response('# Self Improvement', {
          status: 200,
          headers: { 'content-type': 'text/markdown' }
        })
      }
      return jsonResponse({
        latestVersion: { version: '3.0.18' },
        skill: {
          slug: 'self-improving-agent',
          displayName: 'self-improving-agent',
          summary: 'Captures learnings',
          stats: {
            downloads: 550683,
            stars: 2985
          },
          tags: {
            latest: '3.0.18'
          }
        }
      })
    }) as typeof fetch

    const detail = await getSkillMarketDetail(
      tempRoot,
      { provider: 'skillhub', slug: 'self-improving-agent' },
      fetcher
    )

    expect(detail.version).toBe('3.0.18')
    expect(detail.downloads).toBe(550683)
    expect(detail.rating).toBe(2985)
  })

  it('keeps detail metadata when preview file loading fails', async () => {
    tempRoot = await mkdtemp(join(tmpdir(), 'dotcraft-skill-market-'))
    const fetcher = vi.fn(async (url: string) => {
      if (url.includes('/file?')) {
        return new Response('missing', { status: 404 })
      }
      return jsonResponse({
        skill: {
          slug: 'git-helper',
          displayName: 'Git Helper',
          summary: 'Git workflow help'
        }
      })
    }) as typeof fetch

    const detail = await getSkillMarketDetail(
      tempRoot,
      { provider: 'skillhub', slug: 'git-helper' },
      fetcher
    )

    expect(detail.readme).toBeUndefined()
    expect(detail.description).toBe('Git workflow help')
  })

  it('installs a valid skill archive into workspace .craft skills', async () => {
    tempRoot = await mkdtemp(join(tmpdir(), 'dotcraft-skill-market-'))
    const fetcher = vi.fn(async () =>
      zipResponse({
        'demo-skill/SKILL.md': '---\nname: demo-skill\n---\n# Demo',
        'demo-skill/README.md': '# Demo readme'
      })
    ) as typeof fetch

    const result = await installSkillFromMarket(
      tempRoot,
      { provider: 'skillhub', slug: 'demo-skill', version: '1.0.0' },
      fetcher
    )

    expect(result).toEqual(
      expect.objectContaining({
        skillName: 'demo-skill',
        version: '1.0.0',
        overwritten: false
      })
    )
    expect(existsSync(join(tempRoot, '.craft', 'skills', 'demo-skill', 'SKILL.md'))).toBe(true)
    const marker = JSON.parse(
      await readFile(join(tempRoot, '.craft', 'skills', 'demo-skill', '.dotcraft-market.json'), 'utf-8')
    ) as { provider: string; slug: string; version: string }
    expect(marker).toMatchObject({ provider: 'skillhub', slug: 'demo-skill', version: '1.0.0' })
  })

  it('rejects unsafe or incomplete archives', () => {
    expect(() => normalizeArchive(zipSync({ 'demo/README.md': new TextEncoder().encode('x') }))).toThrow(
      /SKILL\.md/
    )
    expect(() =>
      normalizeArchive(zipSync({ '../SKILL.md': new TextEncoder().encode('# Bad') }))
    ).toThrow(/Invalid archive path/)
  })

  it('blocks existing installs unless overwrite is explicit', async () => {
    tempRoot = await mkdtemp(join(tmpdir(), 'dotcraft-skill-market-'))
    const targetDir = join(tempRoot, '.craft', 'skills', 'demo-skill')
    await mkdir(targetDir, { recursive: true })
    await writeFile(join(targetDir, 'SKILL.md'), '# Old', 'utf-8')
    const fetcher = vi.fn(async () => zipResponse({ 'SKILL.md': '# New' })) as typeof fetch

    await expect(
      installSkillFromMarket(tempRoot, { provider: 'clawhub', slug: 'demo-skill' }, fetcher)
    ).rejects.toThrow(/already installed/)

    const result = await installSkillFromMarket(
      tempRoot,
      { provider: 'clawhub', slug: 'demo-skill', version: '2.0.0', overwrite: true },
      fetcher
    )

    expect(result.overwritten).toBe(true)
    await expect(readFile(join(targetDir, 'SKILL.md'), 'utf-8')).resolves.toBe('# New')
  })

  it('prepares a DotCraft install candidate without writing installed skills', async () => {
    tempRoot = await mkdtemp(join(tmpdir(), 'dotcraft-skill-market-'))
    const fetcher = vi.fn(async () =>
      zipResponse({
        'demo-skill/SKILL.md': '---\nname: demo-skill\n---\n# Demo',
        'demo-skill/README.md': '# Demo readme'
      })
    ) as typeof fetch

    const result = await prepareDotCraftSkillInstall(
      tempRoot,
      { provider: 'skillhub', slug: 'demo-skill', version: '1.0.0' },
      fetcher
    )

    expect(result).toEqual(
      expect.objectContaining({
        skillName: 'demo-skill',
        provider: 'skillhub',
        slug: 'demo-skill',
        version: '1.0.0',
        workspacePath: tempRoot
      })
    )
    expect(result.candidateDir).toContain(join('.craft', 'skill-install-staging'))
    expect(existsSync(join(result.candidateDir, 'SKILL.md'))).toBe(true)
    expect(existsSync(join(result.candidateDir, 'README.md'))).toBe(true)
    expect(existsSync(result.metadataPath)).toBe(true)
    expect(existsSync(join(tempRoot, '.craft', 'skills', 'demo-skill', 'SKILL.md'))).toBe(false)
  })

  it('cleans a DotCraft install candidate bound to a thread', async () => {
    tempRoot = await mkdtemp(join(tmpdir(), 'dotcraft-skill-market-'))
    const fetcher = vi.fn(async () =>
      zipResponse({
        'demo-skill/SKILL.md': '---\nname: demo-skill\n---\n# Demo'
      })
    ) as typeof fetch

    const result = await prepareDotCraftSkillInstall(
      tempRoot,
      { provider: 'skillhub', slug: 'demo-skill' },
      fetcher
    )

    await bindDotCraftSkillInstall(tempRoot, {
      threadId: 'thread_123',
      stagingDir: result.stagingDir
    })
    expect(existsSync(result.stagingDir)).toBe(true)

    await cleanupDotCraftSkillInstall(tempRoot, { threadId: 'thread_123' })

    expect(existsSync(result.stagingDir)).toBe(false)
  })
})
