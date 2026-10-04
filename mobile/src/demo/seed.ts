import {
  FakeComputer,
  sequenceId,
  type FakeComputerSeed,
  type FakeItem,
  type FakePending,
  type FakeProject,
  type FakeProvider,
  type FakeThread,
} from './fakeComputer'
import { sampleDiff, textBase64 } from './fakeDiffs'
import { MEADOW_PNG } from './images'

export const STUDIO_FINGERPRINT = '3f9c1b0e5d2a4c7f8b6e1d0a9c3f5e7b2a4d6c8e0f1a3b5c7d9e2f4a6b8c0d1e'
export const STUDIO_ADDRESSES = ['192.168.1.20', '100.101.102.103']
export const DEMO_CREDENTIAL = 'demo-credential'
export const DEMO_PORT = 47610

type Line =
  | { kind: 'user'; text: string; photos?: number }
  | { kind: 'assistant'; text: string }
  | { kind: 'reasoning'; seconds: number; text: string }
  | { kind: 'ran'; command: string; output?: string }
  | { kind: 'read'; paths: string[] }
  | { kind: 'searched'; pattern: string }
  | { kind: 'edited'; files: { path: string; added: number; removed: number }[] }
  | { kind: 'codeMode'; lines: Line[]; running?: boolean }
  | { kind: 'image'; prompt: string }
  | { kind: 'chart'; text: string }
  | { kind: 'plan'; plan: string; steps: string[] }

interface ChatSeed {
  id: string
  title: string
  project: 'dotcraft' | 'design-lab' | 'chats'
  profile?: string
  minutesAgo: number
  lines: Line[]
  pending?: FakePending[]
  stream?: string
  error?: string
  planned?: boolean
  context?: number
  config?: Record<string, unknown>
  continuation: string
}

const RELEASE_REPLY = [
  '### What it does',
  '',
  'The release script does **three things**, in order:',
  '',
  '1. Builds the app with `dotnet publish` for each runtime.',
  '2. Signs the binaries and ~~uploads symbols~~ skips symbols for now.',
  '3. Fills the notes from [the template](scripts/release-notes.md) inside [release.ps1](D:/Projects/dotcraft/scripts/release.ps1:42) and adds [the banner](docs/release-banner.png).',
  '',
  '- [x] Builds on Windows and Linux',
  '- [ ] Mac signing still needs a certificate',
  '',
  '> It stops at the *first* failed step, so a half-published release never happens.',
  '',
  '![The release banner](docs/release-banner.png)',
  '',
  '```powershell',
  './scripts/release.ps1 -Version 0.8.1 -Runtime win-x64,linux-x64 -SkipSymbols -OutputDirectory artifacts/release',
  '```',
  '',
  '| Step | Time | Output |',
  '|---|---:|---|',
  '| Publish | 3 min | `artifacts/release/<rid>` |',
  '| Sign | 40 s | signed executables and installers |',
  '',
  '---',
  '',
  'The packaged build lands in [app.zip](artifacts/release/app.zip). The full checklist is in the [publishing guide](https://example.com/docs/publishing).',
].join('\n')

const PUBLISH_COMMAND =
  'dotnet publish src/App/App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/release/win-x64'

const PUBLISH_OUTPUT = [
  '  Determining projects to restore...',
  '  All projects are up-to-date for restore.',
  '  Core -> D:\\Projects\\dotcraft\\src\\Core\\bin\\Release\\net10.0\\win-x64\\Core.dll',
  '  App -> D:\\Projects\\dotcraft\\artifacts\\release\\win-x64\\',
  'Build succeeded in 182.4s',
].join('\n')

const PAIRING_PLAN = [
  '# Phone pairing flow',
  '',
  '## Summary',
  '',
  'Pair a phone with one scan: the computer shows a one-time code, the phone scans it, and the person allows it on the phone.',
  '',
  '## Implementation changes',
  '',
  '- Add a **Phone access** switch to Connections that starts the gateway.',
  '- **Add phone** shows a QR code that carries the address, port, certificate fingerprint, and a one-time code.',
  '- The phone pins the fingerprint, sends the code, and stores the credential it gets back.',
  '- The Phones list shows each phone with **Remove**, which revokes its credential at once.',
  '',
  '## Test plan',
  '',
  '- Pair, revoke, and pair again without restarting the computer.',
  '- A reused or expired code is refused.',
  '',
  '## Assumptions',
  '',
  '- The phone and the computer share a network for the first scan.',
].join('\n')

const fileList = (prefix: string, count: number) => Array.from({ length: count }, (_, index) => `${prefix}/file-${index + 1}.ts`)

const CHATS: ChatSeed[] = [
  {
    id: 'release-script',
    context: 0.12,
    title: 'Explain the release script',
    project: 'dotcraft',
    minutesAgo: 4,
    lines: [
      { kind: 'user', text: 'Walk me through @scripts/release.ps1 with $release-notes and try a Windows publish.' },
      { kind: 'codeMode', lines: [{ kind: 'read', paths: ['scripts/release.ps1'] }, { kind: 'searched', pattern: 'Publish-Runtime' }] },
      { kind: 'ran', command: PUBLISH_COMMAND, output: PUBLISH_OUTPUT },
      { kind: 'chart', text: 'Publish time per runtime' },
      { kind: 'image', prompt: 'A release banner with green hills' },
      { kind: 'assistant', text: RELEASE_REPLY },
    ],
    continuation: 'Sure, here’s more detail.',
  },
  {
    id: 'vite-upgrade',
    context: 0.86,
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
    pending: [
      {
        kind: 'approval',
        requestId: 'approval_vite',
        approvalType: 'shell',
        operation: 'pnpm add -D vite@6.4.3 @vitejs/plugin-react@4.3.4',
        target: 'dotcraft/desktop',
        reason: 'Installs two packages from the network.',
      },
    ],
    continuation: 'Both packages are installed. Running `pnpm build` now to check that the desktop bundle still compiles.',
  },
  {
    id: 'docs-build',
    context: 0.3,
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
    pending: [
      {
        kind: 'question',
        requestId: 'question_docs',
        isBlocking: true,
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
          {
            id: 'ci_trigger',
            header: 'CI trigger',
            question: 'When should CI build the docs?',
            options: [
              { label: 'Every pull request', description: 'Catches broken links before merging' },
              { label: 'Only on main', description: 'Faster pull requests, later feedback' },
            ],
            isOther: true,
            isSecret: false,
          },
        ],
      },
    ],
    continuation: 'Thanks. I’ll remove the other lockfile and add a docs job to the CI workflow.',
  },
  {
    id: 'turn-diff-flake',
    context: 0.61,
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
    context: 0.45,
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
    context: 0.2,
    title: 'Retune the sidebar icon motion',
    project: 'design-lab',
    minutesAgo: 9,
    lines: [
      { kind: 'user', text: 'Slow the sidebar icon motion down a little and update the tokens to match.' },
      { kind: 'read', paths: ['tokens/motion.css'] },
      {
        kind: 'assistant',
        text: 'I’ve drafted slower curves. The motion tokens are marked as protected in this project, so the edit needs your approval. I also have a question about the phone app.',
      },
    ],
    pending: [
      {
        kind: 'approval',
        requestId: 'approval_motion',
        approvalType: 'file',
        operation: 'edit',
        target: 'tokens/motion.css',
        reason: 'Changes files this project marks as protected.',
      },
      {
        kind: 'question',
        requestId: 'question_motion',
        isBlocking: false,
        questions: [
          {
            id: 'motion_scope',
            header: 'Phone app',
            question: 'Should the phone app use the slower curves too?',
            options: [
              { label: 'Yes, match Desktop', description: 'One set of motion tokens for both' },
              { label: 'No, keep the phone as it is', description: 'Only the sidebar icons change' },
            ],
            isOther: true,
            isSecret: false,
          },
        ],
      },
    ],
    continuation: 'The tokens are updated. Rebuilding the motion lab so you can compare the curves.',
  },
  {
    id: 'dialog-audit',
    context: 0.52,
    title: 'Audit the dialog headers',
    project: 'design-lab',
    minutesAgo: 0.2,
    lines: [
      { kind: 'user', text: 'Check every dialog in the lab for the shared header treatment.' },
      { kind: 'searched', pattern: 'ModalHeader' },
      { kind: 'read', paths: fileList('src/dialogs', 14) },
      { kind: 'codeMode', running: true, lines: [{ kind: 'searched', pattern: 'DialogBadge' }] },
    ],
    stream:
      'Twelve of the fourteen dialogs use the shared header. The two that don’t are the color picker and the import sync dialog, and both draw a bare icon without the badge.',
    continuation: 'Picking up from here.',
  },
  {
    id: 'release-summary',
    context: 0.08,
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
    context: 0.7,
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
    context: 0.22,
    title: 'Rename the settings segments',
    project: 'dotcraft',
    minutesAgo: 60 * 26,
    lines: [
      { kind: 'user', text: 'Shorten the Connections segment names so they fit on one line in German.', photos: 2 },
      { kind: 'read', paths: ['locales/de.ts'] },
      { kind: 'edited', files: [{ path: 'locales/de.ts', added: 4, removed: 4 }] },
      { kind: 'assistant', text: 'All four German segment names now fit on one line at the narrowest settings width.' },
    ],
    config: { providerId: 'local', model: 'quill-14b' },
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
        kind: 'plan',
        plan: PAIRING_PLAN,
        steps: ['Turn on phone access in Connections', 'Show a one-time code', 'Scan it and choose Allow', 'List the phone with Remove'],
      },
      {
        kind: 'assistant',
        text: 'Four steps:\n- Turn on phone access in Connections.\n- Choose Add phone to show a one-time code.\n- Scan it with the phone and choose Allow.\n- The phone appears in the list, where you can remove it at any time.',
      },
    ],
    planned: true,
    continuation: 'Sure, here’s more detail.',
  },
]

const EFFORTS = [
  { effort: 'low', label: 'Low' },
  { effort: 'medium', label: 'Medium' },
  { effort: 'high', label: 'High' },
  { effort: 'extraHigh', label: 'Extra High' },
]

const PROVIDERS: FakeProvider[] = [
  {
    id: 'studio',
    displayName: 'Studio Gateway',
    authMethod: 'subscriptionOAuth',
    models: [
      {
        id: 'atlas-2',
        isDefault: true,
        reasoning: { supportsDisable: true, supportedEfforts: EFFORTS, defaultEffort: 'high', supportedOutputs: ['none', 'summary', 'full'], defaultOutput: 'full' },
        speed: { supportedModes: ['standard', 'fast'], defaultMode: 'standard' },
      },
      {
        id: 'atlas-2-mini',
        reasoning: { supportsDisable: false, supportedEfforts: EFFORTS.slice(0, 3), defaultEffort: 'medium', supportedOutputs: ['full'], defaultOutput: 'full' },
      },
      { id: 'atlas-1' },
    ],
  },
  { id: 'local', displayName: 'Local runtime', models: [{ id: 'quill-7b' }, { id: 'quill-14b', isDefault: true }] },
]

const COMMANDS: NonNullable<FakeComputerSeed['commands']> = [
  { name: 'new', description: 'Start a new conversation', category: 'builtin' },
  { name: 'code-review', description: 'Review changed files and report issues', category: 'custom' },
  { name: 'release-check', description: 'Check that a release is ready to publish', category: 'custom' },
  { name: 'triage', description: 'Sort open issues by area and urgency', category: 'custom' },
]

const SKILLS: NonNullable<FakeComputerSeed['skills']> = [
  { name: 'release-notes', description: 'Write release notes from merged changes', enabled: true },
  { name: 'browser', description: 'Open and inspect web pages', enabled: true },
  { name: 'docs-guide', description: 'Write and review documentation pages', enabled: true },
  { name: 'spreadsheet', description: 'Read and edit spreadsheets', enabled: false },
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
  const push = (type: string, payload: Record<string, unknown>, seconds = 1, status = 'completed') => {
    const createdAt = iso(now, chat.minutesAgo + 1, clock)
    clock += seconds
    const completedAt = status === 'completed' ? iso(now, chat.minutesAgo + 1, clock) : null
    items.push({ id: sequenceId('item', items.length + 1), turnId, type, status, payload, createdAt, completedAt })
  }
  const nextCall = () => {
    call += 1
    return sequenceId('call', call)
  }
  const tool = (toolName: string, args: Record<string, unknown>, result: Record<string, unknown> = {}) => {
    const callId = nextCall()
    const presentation = PRESENTATIONS[toolName]
    const shown = presentation ? { presentation } : {}
    push('toolCall', { toolName, providerFlatName: toolName, callId, arguments: args, ...shown })
    push('toolResult', { toolName, providerFlatName: toolName, callId, result: 'ok', success: true, ...shown, ...result })
  }
  const emit = (line: Line) => {
    switch (line.kind) {
      case 'user': {
        const photos = Array.from({ length: line.photos ?? 0 }, () => ({ type: 'image', url: `data:image/png;base64,${MEADOW_PNG}` }))
        push('userMessage', photos.length > 0 ? { text: line.text, nativeInputParts: [{ type: 'text', text: line.text }, ...photos] } : { text: line.text })
        break
      }
      case 'assistant':
        push('agentMessage', { text: line.text })
        break
      case 'reasoning':
        push('reasoningContent', { text: line.text }, line.seconds)
        break
      case 'ran':
        tool('Exec', { command: line.command }, line.output ? { result: line.output } : {})
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
            structuredContent: {
              kind: 'fileChange',
              writeState: 'applied',
              changes: [{ path: file.path, kind: 'update', diff: sampleDiff(file.path, file.added, file.removed), additions: file.added, deletions: file.removed }],
            },
          })
        }
        break
      case 'codeMode': {
        const callId = nextCall()
        const wrapper = { toolName: 'CodeMode', providerFlatName: 'CodeMode', callId, arguments: { code: 'await tools.ReadFile({ path })' } }
        if (line.running) {
          push('toolCall', wrapper, 1, 'started')
          for (const nested of line.lines) emit(nested)
          break
        }
        push('toolCall', { ...wrapper, source: { kind: 'CoreNative', sourceId: 'code-mode' } })
        for (const nested of line.lines) emit(nested)
        push('toolResult', { toolName: 'CodeMode', providerFlatName: 'CodeMode', callId, result: 'done', success: true })
        break
      }
      case 'image':
        push('imageGeneration', { callId: nextCall(), status: 'completed', revisedPrompt: line.prompt, result: MEADOW_PNG, mediaType: 'image/png' }, 8)
        break
      case 'plan':
        tool('CreatePlan', { plan: line.plan, todos: line.steps.map((content, index) => ({ id: `step-${index + 1}`, content })) })
        break
      case 'chart':
        tool('NodeReplJs', { code: 'renderChart()' }, {
          result: '',
          contentItems: [
            { type: 'text', text: line.text },
            { type: 'image', dataBase64: MEADOW_PNG, mediaType: 'image/png' },
          ],
        })
        break
    }
  }
  for (const line of chat.lines) emit(line)
  for (const { kind, ...payload } of chat.pending ?? []) push(kind === 'approval' ? 'approvalRequest' : 'userInputRequest', payload)
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
    pending: chat.pending ?? [],
    stream: chat.stream ?? null,
    continuation: chat.continuation,
    config: { ...chat.config, ...(chat.planned ? { mode: 'plan' } : {}) },
    ...(chat.planned ? { planned: true } : {}),
    ...(chat.context === undefined ? {} : { context: Math.round(chat.context * CONTEXT_TOKENS) }),
  }
}

const CONTEXT_TOKENS = 400_000

const RELEASE_SCRIPT = [
  'param(',
  '  [Parameter(Mandatory)] [string] $Version,',
  "  [string[]] $Runtime = @('win-x64', 'linux-x64'),",
  '  [switch] $SkipSymbols,',
  "  [string] $OutputDirectory = 'artifacts/release'",
  ')',
  '',
  "$ErrorActionPreference = 'Stop'",
  '',
  'foreach ($rid in $Runtime) {',
  '  Publish-Runtime -Version $Version -Runtime $rid -Output (Join-Path $OutputDirectory $rid)',
  '  Sign-Binaries -Path (Join-Path $OutputDirectory $rid)',
  '}',
  '',
  'if (-not $SkipSymbols) { Upload-Symbols -Version $Version }',
  'Write-ReleaseNotes -Version $Version -Template scripts/release-notes.md',
  '',
].join('\n')

const RELEASE_NOTES = ['# DotCraft {{version}}', '', '## Highlights', '', '{{highlights}}', '', '## Fixes', '', '{{fixes}}', ''].join('\n')

function studioFiles(): Record<string, string> {
  return {
    'D:/Projects/dotcraft/scripts/release.ps1': textBase64(RELEASE_SCRIPT),
    'D:/Projects/dotcraft/scripts/release-notes.md': textBase64(RELEASE_NOTES),
    'D:/Projects/dotcraft/docs/release-banner.png': MEADOW_PNG,
    'D:/Projects/dotcraft/artifacts/release/app.zip': 'UEsDBBQAAAAIAAAAIQA=',
    'D:/Projects/dotcraft/artifacts/release/publish.log': textBase64('Build succeeded'),
  }
}

function accountUsage(now: Date): Record<string, unknown> {
  const later = (hours: number) => new Date(now.getTime() + hours * 3_600_000).toISOString()
  return {
    available: true,
    planType: 'plus',
    primary: { usedPercent: 27, windowSeconds: 18_000, resetAt: later(3) },
    secondary: { usedPercent: 9, windowSeconds: 604_800, resetAt: later(6 * 24) },
  }
}

const PRESENTATIONS: Record<string, { presentationId: string; options?: { operation: string } }> = {
  Exec: { presentationId: 'core.shell' },
  WriteFile: { presentationId: 'core.file-write', options: { operation: 'write' } },
  EditFile: { presentationId: 'core.file-write', options: { operation: 'edit' } },
  WebSearch: { presentationId: 'core.web', options: { operation: 'search' } },
  WebFetch: { presentationId: 'core.web', options: { operation: 'fetch' } },
  ReadFile: { presentationId: 'core.read-file' },
  GrepFiles: { presentationId: 'core.read-file' },
  FindFiles: { presentationId: 'core.read-file' },
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
    path: `D:/Projects/${id}`,
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
    providers: PROVIDERS,
    commands: COMMANDS,
    skills: SKILLS,
    files: studioFiles(),
    tooLargeFiles: ['D:/Projects/dotcraft/artifacts/release/publish.log'],
    accountUsage: accountUsage(now),
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
