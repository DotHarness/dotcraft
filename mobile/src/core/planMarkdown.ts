export interface ParsedPlanMarkdown {
  title: string
  overview: string
  content: string
}

export function parsePlanMarkdown(markdown: string): ParsedPlanMarkdown {
  const normalized = markdown.replace(/\r\n/g, '\n').replace(/\r/g, '\n').trim()
  if (!normalized) return { title: '', overview: '', content: '' }

  const lines = normalized.split('\n')
  const h1Index = lines.findIndex((line) => /^#\s+/.test(line.trimStart()))
  const fallbackTitleIndex = lines.findIndex((line) => line.trim().length > 0)
  const title = h1Index >= 0
    ? stripHeading(lines[h1Index])
    : (fallbackTitleIndex >= 0 ? stripInlineMarkdown(lines[fallbackTitleIndex]) : '')
  const contentLines = h1Index >= 0
    ? lines.filter((_, index) => index !== h1Index)
    : [...lines]
  while (contentLines.length > 0 && contentLines[0].trim().length === 0) {
    contentLines.shift()
  }
  const content = contentLines.join('\n').trim()
  let overview = h1Index >= 0
    ? extractFirstSectionOverview(content)
    : readFirstParagraph(lines, fallbackTitleIndex >= 0 ? fallbackTitleIndex + 1 : 0, false)
  if (!overview) {
    overview = h1Index >= 0
      ? readFirstParagraph(lines, h1Index + 1, false)
      : extractFirstSectionOverview(content)
  }
  return { title, overview, content }
}

function extractFirstSectionOverview(content: string): string {
  const lines = content.split('\n')
  for (let i = 0; i < lines.length; i += 1) {
    if (!isHeading(lines[i])) continue
    const paragraph = readFirstParagraph(lines, i + 1, true)
    if (paragraph) return paragraph
  }
  return ''
}

function isHeading(line: string): boolean {
  return /^#{1,6}\s+/.test(line.trimStart())
}

function stripHeading(line: string): string {
  return line.trim().replace(/^#+\s*/, '').replace(/\s*#+\s*$/, '').trim()
}

function readFirstParagraph(lines: string[], start: number, stopAtHeading: boolean): string {
  const paragraph: string[] = []
  for (let i = start; i < lines.length; i += 1) {
    const line = lines[i]
    if (line.trim().length === 0) {
      if (paragraph.length > 0) break
      continue
    }
    if (isHeading(line)) {
      if (paragraph.length > 0) break
      if (stopAtHeading) break
      continue
    }
    paragraph.push(stripInlineMarkdown(line))
  }
  return paragraph.join(' ').trim()
}

function stripInlineMarkdown(line: string): string {
  return line.trim().replace(/^>\s*/, '').replace(/^[-*]\s+/, '')
}
