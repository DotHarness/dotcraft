import { mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, expect, it } from 'vitest'
import { QrFileWatcher } from '../qrWatcher'

const workspaces: string[] = []

afterEach(async () => {
  await Promise.all(workspaces.splice(0).map((workspace) => rm(workspace, { recursive: true, force: true })))
})

it('reads module QR files from the workspace tmp directory', async () => {
  const workspace = await mkdtemp(join(tmpdir(), 'dotcraft-qr-watch-'))
  workspaces.push(workspace)
  const qrDirectory = join(workspace, '.craft', 'tmp', 'channel-qq')
  await mkdir(qrDirectory, { recursive: true })
  await writeFile(join(qrDirectory, 'qr.png'), Buffer.from([0x89, 0x50, 0x4e, 0x47]))
  const updates: string[] = []
  const watcher = new QrFileWatcher({
    workspacePath: workspace,
    onQrUpdate: (payload) => { if (payload.qrDataUrl) updates.push(payload.qrDataUrl) }
  })

  try {
    await watcher.startWatching('channel-qq')
    expect(updates).toEqual(['data:image/png;base64,iVBORw=='])
  } finally {
    watcher.stopWatching('channel-qq')
  }
})
