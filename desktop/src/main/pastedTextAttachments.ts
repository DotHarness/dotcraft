import { randomUUID } from 'crypto'
import { promises as fs } from 'fs'
import path from 'path'
import type { PastedTextContext } from '../shared/composerContext'
import { PASTED_TEXT_RESTORE_LIMIT, PASTED_TEXT_THRESHOLD } from '../shared/composerContext'

export async function createPastedTextAttachment(workspacePath: string, text: string): Promise<PastedTextContext> {
  const id = randomUUID()
  const directory = path.join(workspacePath, '.craft', 'attachments', id)
  await fs.mkdir(directory, { recursive: true })
  const absolutePath = path.join(directory, 'pasted-text.txt')
  await fs.writeFile(absolutePath, text, 'utf8')
  return { kind: 'pastedText', id, path: absolutePath, fileName: 'pasted-text.txt',
    preview: text.trim().replace(/\s+/g, ' ').slice(0, 80), characterCount: text.length }
}

export async function readPastedTextAttachment(absolutePath: string): Promise<string> {
  return fs.readFile(absolutePath, 'utf8')
}

export async function restorePastedTextAttachment(absolutePath: string): Promise<string> {
  const text = await fs.readFile(absolutePath, 'utf8')
  if (text.length < PASTED_TEXT_THRESHOLD || text.length > PASTED_TEXT_RESTORE_LIMIT) {
    throw new Error('Pasted text cannot be restored into the editor at this size.')
  }
  return text
}
