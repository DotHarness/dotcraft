/**
 * One-shot page screenshot CLI for DotCraft Desktop.
 *
 * Launches the app, connects, navigates to a target surface via the renderer
 * automation bridge, and writes a PNG. Intended for quick "open page X and look
 * at it" loops without running the full smoke flow.
 *
 * Examples:
 *   npm run shot -- --view automations
 *   npm run shot -- --settings-tab llmService --out llm.png
 *   npm run shot -- --view skills --no-build --full
 *   npm run shot -- --thread <id> --detail-tab changes
 *   npm run shot -- --view conversation --workspace F:\\my-project
 *   npm run shot -- --view automations --remote ws://127.0.0.1:9100/ws
 */

import {
  launchDesktop,
  type AutomationsTab,
  type DetailTab,
  type LaunchOptions,
  type MainView,
  type SettingsTab
} from './harness'

interface CliArgs {
  view?: MainView
  settingsTab?: SettingsTab
  automationsTab?: AutomationsTab
  detailTab?: DetailTab
  thread?: string
  workspace?: string
  out?: string
  build: LaunchOptions['build']
  remote?: string
  waitMs: number
  fullPage: boolean
  connectTimeoutMs: number
}

function parseArgs(argv: string[]): CliArgs {
  const args: CliArgs = { build: 'if-missing', waitMs: 0, fullPage: false, connectTimeoutMs: 30_000 }
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i]
    const next = (): string => {
      const value = argv[++i]
      if (value == null) throw new Error(`Missing value for ${arg}`)
      return value
    }
    switch (arg) {
      case '--view': args.view = next() as MainView; break
      case '--settings-tab': args.settingsTab = next() as SettingsTab; break
      case '--automations-tab': args.automationsTab = next() as AutomationsTab; break
      case '--detail-tab': args.detailTab = next() as DetailTab; break
      case '--thread': args.thread = next(); break
      case '--workspace': args.workspace = next(); break
      case '--out': args.out = next(); break
      case '--remote': args.remote = next(); break
      case '--wait': args.waitMs = Number.parseInt(next(), 10) || 0; break
      case '--connect-timeout': args.connectTimeoutMs = Number.parseInt(next(), 10) || 30_000; break
      case '--build': args.build = 'always'; break
      case '--no-build': args.build = 'never'; break
      case '--full': args.fullPage = true; break
      case '-h':
      case '--help': printHelp(); process.exit(0); break
      default:
        throw new Error(`Unknown argument: ${arg}`)
    }
  }
  return args
}

function printHelp(): void {
  console.log(`Usage: npm run shot -- [options]

Navigation (pick one; --detail-tab can be combined with any):
  --view <name>            conversation | skills | automations | settings | channels | teams
  --settings-tab <tab>     open Settings on a section (general, llmService, mcp, ...)
  --automations-tab <tab>  open Automations on tasks | cron
  --thread <id>            open a thread by id (use --view conversation default surface)
  --detail-tab <tab>       reveal detail panel on changes | plan

Launch:
  --workspace <path>       workspace folder (default: $DOTCRAFT_E2E_WORKSPACE or repo root)
  --remote <ws-url>        connect to an external AppServer over WebSocket
  --build                  force electron-vite build before launch
  --no-build               skip build (reuse existing out/)
  --connect-timeout <ms>   wait-for-connected timeout (default 30000)

Output:
  --out <file>             screenshot path (default: e2e/screenshots/<target>.png)
  --wait <ms>              extra settle delay before capture
  --full                   capture full page instead of viewport`)
}

function defaultOutName(args: CliArgs): string {
  if (args.thread) return `thread-${args.thread}`
  if (args.settingsTab) return `settings-${args.settingsTab}`
  if (args.automationsTab) return `automations-${args.automationsTab}`
  if (args.view) return `view-${args.view}`
  return 'shot'
}

async function main(): Promise<void> {
  const args = parseArgs(process.argv.slice(2))

  const harness = await launchDesktop({
    workspace: args.workspace,
    build: args.build,
    remote: args.remote
  })

  try {
    await harness.waitForConnected(args.connectTimeoutMs)

    if (args.thread) {
      await harness.openThread(args.thread)
    } else if (args.settingsTab) {
      await harness.gotoSettings(args.settingsTab)
    } else if (args.automationsTab) {
      await harness.gotoAutomations(args.automationsTab)
    } else if (args.view) {
      await harness.gotoView(args.view)
    }

    if (args.detailTab) {
      await harness.setDetailTab(args.detailTab)
    }

    if (args.waitMs > 0) {
      await harness.page.waitForTimeout(args.waitMs)
    }

    const out = args.out ?? defaultOutName(args)
    await harness.screenshot(out, { fullPage: args.fullPage })
  } finally {
    await harness.close()
  }
}

main().catch((error) => {
  console.error(`[shot] FAILED: ${String(error)}`)
  process.exit(1)
})
