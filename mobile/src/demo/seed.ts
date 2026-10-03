import { FakeComputer, sequenceId, type FakeComputerSeed, type FakeItem, type FakePending, type FakeProject, type FakeThread } from './fakeComputer'

export const STUDIO_FINGERPRINT = '3f9c1b0e5d2a4c7f8b6e1d0a9c3f5e7b2a4d6c8e0f1a3b5c7d9e2f4a6b8c0d1e'
export const STUDIO_ADDRESSES = ['192.168.1.20', '100.101.102.103']
export const DEMO_CREDENTIAL = 'demo-credential'
export const DEMO_PORT = 47610

type Line =
  | { kind: 'user'; text: string }
  | { kind: 'assistant'; text: string }
  | { kind: 'reasoning'; seconds: number; text: string }
  | { kind: 'ran'; command: string }
  | { kind: 'read'; paths: string[] }
  | { kind: 'searched'; pattern: string }
  | { kind: 'edited'; files: { path: string; added: number; removed: number }[] }

interface ChatSeed {
  id: string
  title: string
  project: 'dotcraft' | 'design-lab' | 'chats'
  profile?: string
  minutesAgo: number
  lines: Line[]
  pending?: FakePending
  stream?: string
  error?: string
  continuation: string
}

const fileList = (prefix: string, count: number) => Array.from({ length: count }, (_, index) => `${prefix}/file-${index + 1}.ts`)

const CHATS: ChatSeed[] = [
  {
    id: 'vite-upgrade',
    title: 'Upgrade Vite to 6.4',
    project: 'dotcraft',
    minutesAgo: 2,
    lines: [
      { kind: 'user', text: 'Upgrade Vite to 6.4 in the desktop app and make sure the build still works.' },
      {
        kind: 'reasoning',
        seconds: 6,
        text: 'The desktop package pins Vite 6.0 and an older React plugin. Vite 6.4 needs plugin 4.3 or later, so both move together.',
      },
      { kind: 'read', paths: ['desktop/package.json'] },
      { kind: 'searched', pattern: 'vite.config' },
      { kind: 'assistant', text: 'Vite 6.4 needs the matching React plugin, so I’ll install both and then run the desktop build.' },
    ],
    pending: {
      kind: 'approval',
      requestId: 'approval_vite',
      approvalType: 'shell',
      operation: 'pnpm add -D vite@6.4.3 @vitejs/plugin-react@4.3.4',
      target: 'dotcraft/desktop',
      reason: 'Installs two packages from the network.',
    },
    continuation: 'Both packages are installed. Running `pnpm build` now to check that the desktop bundle still compiles.',
  },
  {
    id: 'docs-build',
    title: 'Set up the docs site build',
    project: 'dotcraft',
    minutesAgo: 6,
    lines: [
      { kind: 'user', text: 'Set up a build for the docs site and add it to CI.' },
      { kind: 'read', paths: ['docs/package.json'] },
      { kind: 'ran', command: 'ls docs' },
      {
        kind: 'assistant',
        text: 'The docs folder has both `pnpm-lock.yaml` and `package-lock.json`, so I can’t tell which one CI should trust.',
      },
    ],
    pending: {
      kind: 'question',
      requestId: 'question_docs',
      questions: [
        {
          id: 'package_manager',
          header: 'Package manager',
          question: 'Which package manager should the docs build use?',
          options: [
            { label: 'pnpm', description: 'Matches the desktop app' },
            { label: 'npm', description: 'Matches the SDK packages' },
          ],
          isOther: true,
          isSecret: false,
        },
      ],
    },
    continuation: 'Thanks. I’ll remove the other lockfile and add a docs job to the CI workflow.',
  },
  {
    id: 'turn-diff-flake',
    title: 'Fix the flaky turn-diff test',
    project: 'dotcraft',
    minutesAgo: 0,
    lines: [
      { kind: 'user', text: 'The turn-diff test fails about one run in five on CI. Find out why and fix it.' },
      {
        kind: 'reasoning',
        seconds: 9,
        text: 'It only fails on CI, so timing is the likely cause. The test reads the diff right after the turn ends, but the snapshot is written asynchronously.',
      },
      { kind: 'read', paths: ['src/turnDiffStore.ts'] },
      { kind: 'ran', command: 'npm test -- turnDiff' },
      {
        kind: 'assistant',
        text: 'The test reads the diff before the snapshot write settles. I’ll make the store resolve only after the write and wait on that in the test.',
      },
      {
        kind: 'edited',
        files: [
          { path: 'src/turnDiffStore.ts', added: 30, removed: 5 },
          { path: 'src/turnDiff.test.ts', added: 10, removed: 3 },
          { path: 'src/snapshotWriter.ts', added: 2, removed: 0 },
        ],
      },
      { kind: 'ran', command: 'npm test' },
    ],
    stream:
      'All 128 tests pass locally. I’m running the turn-diff suite ten more times in a row to make sure the flake is really gone before I write up the change.',
    continuation: 'Picking up from here.',
  },
  {
    id: 'pairing-review',
    title: 'Review the phone pairing PR',
    project: 'dotcraft',
    profile: 'reviewer',
    minutesAgo: 0.5,
    lines: [
      { kind: 'user', text: 'Review the phone pairing pull request before I merge it.' },
      { kind: 'read', paths: fileList('src/pairing', 6) },
      {
        kind: 'assistant',
        text: 'The pairing route consumes the code before it checks the fingerprint, so a mismatched certificate still burns the code.',
      },
    ],
    stream:
      'Two smaller notes so far: the QR payload should not repeat the port when it matches the default, and the device list sorts by name instead of last seen.',
    continuation: 'Picking up from here.',
  },
  {
    id: 'motion-tokens',
    title: 'Retune the sidebar icon motion',
    project: 'design-lab',
    minutesAgo: 9,
    lines: [
      { kind: 'user', text: 'Slow the sidebar icon motion down a little and update the tokens to match.' },
      { kind: 'read', paths: ['tokens/motion.css'] },
      {
        kind: 'assistant',
        text: 'I’ve drafted slower curves. The motion tokens are marked as protected in this project, so the edit needs your approval.',
      },
    ],
    pending: {
      kind: 'approval',
      requestId: 'approval_motion',
      approvalType: 'file',
      operation: 'edit',
      target: 'tokens/motion.css',
      reason: 'Changes files this project marks as protected.',
    },
    continuation: 'The tokens are updated. Rebuilding the motion lab so you can compare the curves.',
  },
  {
    id: 'dialog-audit',
    title: 'Audit the dialog headers',
    project: 'design-lab',
    minutesAgo: 0.2,
    lines: [
      { kind: 'user', text: 'Check every dialog in the lab for the shared header treatment.' },
      { kind: 'searched', pattern: 'ModalHeader' },
      { kind: 'read', paths: fileList('src/dialogs', 14) },
    ],
    stream:
      'Twelve of the fourteen dialogs use the shared header. The two that don’t are the color picker and the import sync dialog, and both draw a bare icon without the badge.',
    continuation: 'Picking up from here.',
  },
  {
    id: 'release-summary',
    title: 'Summarize yesterday’s release notes',
    project: 'chats',
    minutesAgo: 60,
    lines: [
      { kind: 'user', text: 'Summarize yesterday’s release notes in three bullets.' },
      {
        kind: 'reasoning',
        seconds: 4,
        text: 'The notes have eleven entries. Three themes cover most of them: prompt caching, the file viewer, and Docker updates.',
      },
      {
        kind: 'assistant',
        text: 'Here’s the short version:\n- Long tool runs and approvals keep the prompt cache warm, so resumed turns cost less.\n- Each segment of the file viewer path now opens a folder tree.\n- Docker deployments confirm when an update has finished.',
      },
    ],
    continuation: 'Sure, here’s more detail.',
  },
  {
    id: 'changelog',
    title: 'Draft the 0.8.1 changelog',
    project: 'dotcraft',
    minutesAgo: 180,
    lines: [
      { kind: 'user', text: 'Draft the 0.8.1 changelog from the merged pull requests.' },
      { kind: 'ran', command: 'git log --oneline v0.8.0..HEAD' },
    ],
    error: 'The model provider is overloaded.',
    continuation: 'Trying again. Reading the merged pull requests since 0.8.0.',
  },
  {
    id: 'segment-names',
    title: 'Rename the settings segments',
    project: 'dotcraft',
    minutesAgo: 60 * 26,
    lines: [
      { kind: 'user', text: 'Shorten the Connections segment names so they fit on one line in German.' },
      { kind: 'read', paths: ['locales/de.ts'] },
      { kind: 'edited', files: [{ path: 'locales/de.ts', added: 4, removed: 4 }] },
      { kind: 'assistant', text: 'All four German segment names now fit on one line at the narrowest settings width.' },
    ],
    continuation: 'Sure, here’s more detail.',
  },
  {
    id: 'pairing-plan',
    title: 'Plan the phone pairing flow',
    project: 'design-lab',
    minutesAgo: 60 * 50,
    lines: [
      { kind: 'user', text: 'Sketch the steps for pairing a phone with the computer.' },
      {
        kind: 'assistant',
        text: 'Four steps:\n- Turn on phone access in Connections.\n- Choose Add phone to show a one-time code.\n- Scan it with the phone and choose Allow.\n- The phone appears in the list, where you can remove it at any time.',
      },
    ],
    continuation: 'Sure, here’s more detail.',
  },
]

function iso(now: Date, minutesAgo: number, secondsOffset = 0): string {
  return new Date(now.getTime() - minutesAgo * 60_000 + secondsOffset * 1000).toISOString()
}

function buildThread(chat: ChatSeed, now: Date): FakeThread {
  const turnId = sequenceId('turn', 1)
  const started = iso(now, chat.minutesAgo + 1)
  let clock = 0
  let call = 0
  const items: FakeItem[] = []
  const push = (type: string, payload: Record<string, unknown>, seconds = 1) => {
    const createdAt = iso(now, chat.minutesAgo + 1, clock)
    clock += seconds
    items.push({ id: sequenceId('item', items.length + 1), turnId, type, status: 'completed', payload, createdAt, completedAt: iso(now, chat.minutesAgo + 1, clock) })
  }
  const tool = (toolName: string, args: Record<string, unknown>, structuredContent?: Record<string, unknown>) => {
    call += 1
    const callId = sequenceId('call', call)
    push('toolCall', { toolName, providerFlatName: toolName, callId, arguments: args })
    push('toolResult', { toolName, providerFlatName: toolName, callId, result: 'ok', success: true, ...(structuredContent ? { structuredContent } : {}) })
  }
  for (const line of chat.lines) {
    switch (line.kind) {
      case 'user':
        push('userMessage', { text: line.text })
        break
      case 'assistant':
        push('agentMessage', { text: line.text })
        break
      case 'reasoning':
        push('reasoningContent', { text: line.text }, line.seconds)
        break
      case 'ran':
        tool('Exec', { command: line.command })
        break
      case 'read':
        for (const path of line.paths) tool('ReadFile', { path })
        break
      case 'searched':
        tool('GrepFiles', { pattern: line.pattern })
        break
      case 'edited':
        for (const file of line.files) {
          tool('EditFile', { path: file.path }, {
            kind: 'fileChange',
            writeState: 'applied',
            changes: [{ path: file.path, kind: 'update', additions: file.added, deletions: file.removed }],
          })
        }
        break
    }
  }
  if (chat.pending) {
    const { kind, ...payload } = chat.pending
    push(kind === 'approval' ? 'approvalRequest' : 'userInputRequest', payload)
  }
  const status = chat.error ? 'failed' : chat.stream || chat.pending ? 'running' : 'completed'
  const id = `thread_${chat.id.replace(/-/g, '_')}`
  return {
    id,
    displayName: chat.title,
    profileId: chat.profile ?? null,
    createdAt: started,
    lastActiveAt: iso(now, chat.minutesAgo),
    turns: [
      {
        id: turnId,
        threadId: id,
        status,
        startedAt: started,
        ...(status === 'running' ? {} : { completedAt: iso(now, chat.minutesAgo) }),
        ...(chat.error ? { error: chat.error } : {}),
      },
    ],
    items,
    pending: chat.pending ?? null,
    stream: chat.stream ?? null,
    continuation: chat.continuation,
  }
}

export interface StudioOptions {
  chats?: boolean
  cantStart?: string[]
}

function studioSeed(now: Date, options: StudioOptions = {}): FakeComputerSeed {
  const chats = options.chats === false ? [] : CHATS.map((chat) => ({ chat, thread: buildThread(chat, now) }))
  const project = (id: 'dotcraft' | 'design-lab' | 'chats', name: string, running: boolean, minutesAgo: number): FakeProject => ({
    id: `${id}-0000000000000000`.slice(0, 24).replace(/-/g, '0'),
    name,
    running,
    cantStart: options.cantStart?.includes(id) ?? false,
    lastActiveAt: iso(now, minutesAgo),
    threads: chats.filter((entry) => entry.chat.project === id).map((entry) => entry.thread),
  })
  return {
    name: 'Studio PC',
    version: '0.8.0',
    port: DEMO_PORT,
    fingerprint: STUDIO_FINGERPRINT,
    addresses: STUDIO_ADDRESSES,
    projects: [
      project('dotcraft', 'dotcraft', true, 0),
      project('design-lab', 'design-lab', true, 1),
      project('chats', 'Chats', false, 60),
    ],
    credentials: { [DEMO_CREDENTIAL]: 'dev_demo' },
    pairingCodes: ['demo-code'],
    profiles: [{ id: 'reviewer', name: 'Reviewer' }],
  }
}

export function buildBoxSeed(now: Date): FakeComputerSeed {
  return {
    name: 'Build Box',
    version: '0.8.0',
    port: DEMO_PORT,
    fingerprint: '7a1d93c40b8e2f65a0c47d19e3b5f8a26c0d4e71b9f3a58e2d6c0b4a17f9e3d5',
    addresses: ['192.168.1.40'],
    projects: [
      { id: 'release0tools00000000000', name: 'release-tools', running: false, cantStart: false, lastActiveAt: iso(now, 30), threads: [] },
      { id: 'chats0000000000000000000', name: 'Chats', running: false, cantStart: false, lastActiveAt: iso(now, 90), threads: [] },
    ],
    pairingCodes: ['build-box-code'],
  }
}

export function pairingUrl(computer: FakeComputer, code: string): string {
  const relay = computer.relay ? `&relay=${encodeURIComponent(computer.relay.url)}&host=${computer.relay.hostId}` : ''
  return (
    `dotcraft://pair?v=1&name=${encodeURIComponent(computer.name)}&port=${computer.port}&fp=${computer.certificate}` +
    `&addr=${computer.addresses.join(',')}&code=${encodeURIComponent(code)}${relay}`
  )
}

export function createStudio(now: Date, options?: StudioOptions): FakeComputer {
  return new FakeComputer(studioSeed(now, options))
}
