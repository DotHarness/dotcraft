import { isProjectActionPlatform } from '../../../shared/projectActions'
import { PLATFORM_LABELS } from '../settings/panels/worktrees/environmentPresentation'

const MAX_OUTPUT_CHARS = 8_000

function fenced(text: string): string {
  const longestRun = Math.max(0, ...(text.match(/`+/g) ?? []).map((run) => run.length))
  const fence = '`'.repeat(Math.max(3, longestRun + 1))
  return `${fence}text\n${text.replace(/\s+$/, '')}\n${fence}`
}

export function setUpEnvironmentPrompt({ path, name, platform }: {
  path: string
  name: string
  platform: string
}): string {
  const platformName = isProjectActionPlatform(platform) ? PLATFORM_LABELS[platform] : platform
  const example = JSON.stringify({
    version: 1,
    name,
    setup: { script: 'npm ci', win32: { script: 'npm ci --no-fund' } },
    actions: [{ name: 'Test', icon: 'test', command: 'npm test' }]
  }, null, 2)
  return [
    `Create a local environment for this project by writing \`${path}\`.`,
    'First inspect the AGENTS.md files, development docs, manifests, scripts, and CI configuration. Then write the file in this JSON format:',
    `\`\`\`json\n${example}\n\`\`\``,
    [
      '- `name`: a clear name for the project.',
      '- `setup.script`: an idempotent, non-interactive script that prepares a fresh worktree. It runs in the folder that contains `.craft`, with `DOTCRAFT_SOURCE_TREE_PATH` set to the original checkout and `DOTCRAFT_WORKTREE_PATH` set to the new worktree.',
      '- `actions`: a few useful commands that exist in the project. `icon` is `run`, `test`, `debug`, or `tool`.',
      `- Add a \`win32\`, \`darwin\`, or \`linux\` override, or an action \`platform\`, only when a command differs by platform. This computer runs ${platformName}.`
    ].join('\n'),
    'Verify the setup and other non-persistent commands where practical. Do not start persistent processes, edit unrelated files, commit, or push.'
  ].join('\n\n')
}

export function repairWorktreeSetupPrompt({ environmentPath, exitCode, error, output }: {
  environmentPath: string
  exitCode?: number
  error?: string | null
  output: string
}): string {
  const reason = exitCode !== undefined
    ? `Exit code: ${exitCode}`
    : `Error: ${error || 'The setup script did not finish.'}`
  const tail = output.slice(-MAX_OUTPUT_CHARS)
  return [
    'Fix this project\'s worktree setup. The setup script failed while a new worktree was being prepared, so the original conversation never started.',
    'Do not continue the original request, and do not run the broken setup before fixing it. Reproduce single steps manually if that helps. Paths below and in the output refer to the original checkout or the failed worktree, so edit the matching files in this worktree. Make the smallest source-controlled fix, then verify that the setup succeeds. If it should not be fixed here, explain exactly what the user should change.',
    `Environment file: ${environmentPath}\n${reason}`,
    `Setup output:\n${fenced(tail || '(no output)')}`
  ].join('\n\n')
}
