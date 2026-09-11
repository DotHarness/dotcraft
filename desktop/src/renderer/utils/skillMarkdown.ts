export function stripYamlFrontmatter(markdown: string): string {
  if (!markdown.startsWith('---')) return markdown
  const match = markdown.match(/^---\r?\n[\s\S]*?\r?\n---\r?\n/)
  return match ? markdown.slice(match[0].length).trim() : markdown
}
