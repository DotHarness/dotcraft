// Emits llms.txt and llms-full.txt (format: https://llmstxt.org/) from the English sidebar at build time.

import { existsSync, readFileSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import type { DefaultTheme, SiteConfig } from 'vitepress'
import { enSidebar, zhSidebar } from './sidebar'

const DESCRIPTION_MAX = 200

type Section = { title: string; links: string[] }
type Page = { link: string; file: string; title: string; description: string }
type PageSection = { title: string; pages: Page[] }

export function stripSidebarLabel(html: string): string {
  const labelled = /<span class="dc-side-label">([\s\S]*?)<\/span>/.exec(html)
  return (labelled ? labelled[1] : html).replace(/<[^>]*>/g, '').trim()
}

/** Nested groups become their own sections; a group's own `link` repeats a child, so links are deduped. */
export function flattenSidebar(sidebar: DefaultTheme.SidebarItem[]): Section[] {
  const sections: Section[] = []
  const seen = new Set<string>()

  const add = (section: Section, link: string | undefined) => {
    if (!link || seen.has(link)) return
    seen.add(link)
    section.links.push(link)
  }

  const visit = (items: DefaultTheme.SidebarItem[], section: Section) => {
    for (const item of items) {
      if (item.items?.length) {
        const nested: Section = { title: stripSidebarLabel(item.text ?? ''), links: [] }
        sections.push(nested)
        add(nested, item.link)
        visit(item.items, nested)
      } else {
        add(section, item.link)
      }
    }
  }

  for (const item of sidebar) {
    const section: Section = { title: stripSidebarLabel(item.text ?? ''), links: [] }
    sections.push(section)
    add(section, item.link)
    visit(item.items ?? [], section)
  }

  return sections.filter((section) => section.links.length > 0)
}

export function resolveSourceFile(srcDir: string, link: string): string {
  const path = link.split(/[#?]/)[0]
  const relative =
    path === '/' ? 'index.md' : path.endsWith('/') ? `${path.slice(1)}index.md` : `${path.slice(1)}.md`
  const file = join(srcDir, relative)
  if (!existsSync(file)) {
    throw new Error(`llms.txt: sidebar link "${link}" has no source file at ${file}`)
  }
  return file
}

function splitFrontmatter(raw: string): { data: Record<string, string>; body: string } {
  if (!raw.startsWith('---')) return { data: {}, body: raw }
  const close = raw.indexOf('\n---', 3)
  if (close === -1) return { data: {}, body: raw }

  const data: Record<string, string> = {}
  for (const line of raw.slice(3, close).split(/\r?\n/)) {
    const field = /^([A-Za-z][\w-]*):\s*(.*)$/.exec(line)
    if (field) data[field[1]] = field[2].trim().replace(/^["']|["']$/g, '')
  }
  return { data, body: raw.slice(raw.indexOf('\n', close + 1) + 1) }
}

function stripInlineMarkdown(text: string): string {
  return text
    .replace(/!\[[^\]]*\]\([^)]*\)/g, '')
    .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/`([^`]*)`/g, '$1')
    .replace(/\*\*([^*]*)\*\*/g, '$1')
    .replace(/\s+/g, ' ')
    .trim()
}

function firstSentence(text: string): string {
  const stops = [text.indexOf('. '), text.indexOf('。')].filter((at) => at !== -1)
  const cut = stops.length > 0 ? Math.min(...stops) + 1 : text.length
  const sentence = text.slice(0, cut).trim()
  if (sentence.length <= DESCRIPTION_MAX) return sentence
  return `${sentence.slice(0, DESCRIPTION_MAX).replace(/\s+\S*$/, '')}…`
}

/** Only the home pages carry frontmatter; every other page opens with an H1 and a lede that supply title and description. */
export function readPageSummary(file: string): { title: string; description: string; body: string } {
  const { data, body } = splitFrontmatter(readFileSync(file, 'utf-8'))
  const lines = body.split(/\r?\n/)
  const headingAt = lines.findIndex((line) => line.startsWith('# '))

  const title = data.title ?? (headingAt === -1 ? '' : lines[headingAt].slice(2).trim())
  if (data.description) return { title, description: data.description, body }

  const lede = lines.slice(headingAt + 1).find((line) => {
    const text = line.trim()
    return text !== '' && !text.startsWith('![') && !text.startsWith(':::') && !text.startsWith('#') && !text.startsWith('<')
  })
  return { title, description: lede ? firstSentence(stripInlineMarkdown(lede)) : '', body }
}

function pageUrl(hostname: string, link: string): string {
  return new URL(link.replace(/^\//, ''), hostname).href
}

function collectPages(srcDir: string, sections: Section[]): PageSection[] {
  return sections.map((section) => ({
    title: section.title,
    pages: section.links.map((link) => {
      const file = resolveSourceFile(srcDir, link)
      const { title, description } = readPageSummary(file)
      return { link, file, title, description }
    })
  }))
}

export function renderLlmsTxt(sections: PageSection[], hostname: string, summary: string): string {
  const out = [
    '# DotCraft',
    '',
    `> ${summary}`,
    '',
    'DotCraft is a .NET agent harness: the CLI, the Desktop app, IDE integrations, and chat bots all',
    'connect to one workspace and share its sessions, memory, skills, and tools under `.craft/`.',
    'Pages under /features/ describe what the agent can do; pages under /developing/ cover embedding it,',
    'extending it, and the protocols behind it. Every configuration field, with defaults and JSON',
    `examples, is listed in the full configuration reference at ${pageUrl(hostname, '/developing/configuration')}.`
  ]

  for (const section of sections) {
    out.push('', `## ${section.title}`, '')
    for (const page of section.pages) {
      const suffix = page.description ? `: ${page.description}` : ''
      out.push(`- [${page.title}](${pageUrl(hostname, page.link)})${suffix}`)
    }
  }

  out.push(
    '',
    '## Optional',
    '',
    `- [Chinese documentation](${pageUrl(hostname, '/zh/')}): Complete Simplified Chinese mirror of every page above, at the same paths under /zh/.`,
    ''
  )
  return out.join('\n')
}

/** The emitted heading already carries the title, so drop the body's own H1. */
function bodyWithoutHeading(body: string): string {
  const lines = body.trim().split(/\r?\n/)
  if (!lines[0]?.startsWith('# ')) return body.trim()
  return lines.slice(1).join('\n').trim()
}

export function renderLlmsFullTxt(sections: PageSection[], hostname: string): string {
  const out: string[] = []

  for (const section of sections) {
    for (const page of section.pages) {
      const { title, description, body } = readPageSummary(page.file)
      out.push(`# ${title}`, '', `Source: ${pageUrl(hostname, page.link)}`, '')
      // The home page body is hero markup rather than prose.
      out.push(page.link === '/' ? description : bodyWithoutHeading(body), '')
    }
  }

  return out.join('\n')
}

export function createLlmsBuildEnd({ hostname }: { hostname: string }) {
  return async (siteConfig: SiteConfig): Promise<void> => {
    const { srcDir, outDir } = siteConfig

    // Resolve the Chinese links too so drift in either language fails the build.
    for (const section of flattenSidebar(zhSidebar as DefaultTheme.SidebarItem[])) {
      for (const link of section.links) resolveSourceFile(srcDir, link)
    }

    const sections = collectPages(srcDir, flattenSidebar(enSidebar as DefaultTheme.SidebarItem[]))
    const home = readPageSummary(join(srcDir, 'index.md'))

    writeFileSync(join(outDir, 'llms.txt'), renderLlmsTxt(sections, hostname, home.description), 'utf-8')
    writeFileSync(join(outDir, 'llms-full.txt'), renderLlmsFullTxt(sections, hostname), 'utf-8')
  }
}
