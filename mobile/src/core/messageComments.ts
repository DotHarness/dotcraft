export type MessageComment = { path: string; label: string; comment: string }

const SECTION_HEADING = '# Diff comments:'
const SECTION_START = `${SECTION_HEADING}\n\n## User Comment `
const BLOCK_LINE = /^## User Comment \d+$/

export function splitMessageComments(text: string): { text: string; comments: MessageComment[] } {
  let start = text.lastIndexOf(SECTION_START)
  while (start > 0 && text[start - 1] !== '\n') start = text.lastIndexOf(SECTION_START, start - 1)
  if (start < 0) return { text, comments: [] }
  const blocks: string[][] = []
  for (const line of text.slice(start + SECTION_HEADING.length).split('\n')) {
    if (BLOCK_LINE.test(line)) blocks.push([])
    else blocks.at(-1)?.push(line)
  }
  const comments = blocks.flatMap((lines) => {
    const comment = parseBlock(lines)
    return comment ? [comment] : []
  })
  return comments.length > 0 ? { text: text.slice(0, start).trimEnd(), comments } : { text, comments: [] }
}

function parseBlock(lines: string[]): MessageComment | null {
  const bodyAt = lines.indexOf('Comment:')
  if (bodyAt < 0) return null
  const fields = lines.slice(0, bodyAt)
  const field = (name: string) => fields.find((line) => line.startsWith(`${name}: `))?.slice(name.length + 2).trim()
  const comment = lines.slice(bodyAt + 1).map((line) => (/^\\+## User Comment /.test(line) ? line.slice(1) : line)).join('\n').trim()
  const artifactPath = field('Artifact path')
  const target = field('Annotation target')
  if (artifactPath && target) return { path: artifactPath, label: `${artifactPath} · ${target}`, comment }
  const path = field('File')
  const range = field('Lines')
  return path && range ? { path, label: `${path}:${range}`, comment } : null
}
