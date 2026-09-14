import { afterEach, describe, expect, it } from 'vitest'
import { mkdtemp, readFile, rm } from 'fs/promises'
import { tmpdir } from 'os'
import path from 'path'
import { createPastedTextAttachment, readPastedTextAttachment, restorePastedTextAttachment } from '../pastedTextAttachments'
import { canRestorePastedText } from '../../shared/composerContext'

const directories: string[] = []
afterEach(async () => { await Promise.all(directories.splice(0).map((directory) => rm(directory, { recursive: true, force: true }))) })
describe('pasted text files', () => {
  it.each([5_000, 25_000, 25_001, 120_000])('preserves %i Unicode characters as a real file', async (count) => {
    const directory = await mkdtemp(path.join(tmpdir(), 'dotcraft-paste-'))
    directories.push(directory)
    const text = '文'.repeat(count)
    const attachment = await createPastedTextAttachment(directory, text)
    expect(await readFile(attachment.path, 'utf8')).toBe(text)
    expect(await readPastedTextAttachment(attachment.path)).toBe(text)
    expect(canRestorePastedText(attachment)).toBe(count <= 25_000)
    if (count <= 25_000) expect(await restorePastedTextAttachment(attachment.path)).toBe(text)
    else await expect(restorePastedTextAttachment(attachment.path)).rejects.toThrow('cannot be restored')
    expect(canRestorePastedText({ ...attachment, characterCount: undefined })).toBe(false)
  })
})
