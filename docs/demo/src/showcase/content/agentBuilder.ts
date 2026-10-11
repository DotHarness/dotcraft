import type { Lang } from './types'

export type StarterKey = 'review' | 'docs' | 'triage'

export interface BuilderEdit {
  tool: string
  field: string
  args: Record<string, unknown>
  change: Record<string, unknown>
}

export interface BuilderScript {
  opening: string
  edits: BuilderEdit[]
  closing: string
}

interface AgentPlan {
  name: string
  description: string
  instructions: string
  tools: string[]
  skills: string[]
  mcp: string[]
  approval: 'prompt' | 'autoApprove'
}

export const toolCatalog = [
  { name: 'ReadFile', description: 'Read a file from the workspace.' },
  { name: 'GrepFiles', description: 'Search file contents with a pattern.' },
  { name: 'FindFiles', description: 'Find files by name or glob.' },
  { name: 'EditFile', description: 'Make a targeted edit to a file.' },
  { name: 'WriteFile', description: 'Create or replace a workspace file.' },
  { name: 'Exec', description: 'Run a shell command.' },
  { name: 'WebSearch', description: 'Search the web for current information.' },
  { name: 'WebFetch', description: 'Fetch a page and read it as text.' },
  { name: 'TodoWrite', description: 'Keep the task list up to date.' },
  { name: 'RequestUserInput', description: 'Ask the person a question mid-task.' }
]

export const skillCatalog = [
  { name: 'review-checklist', displayName: 'Review checklist', shortDescription: 'What to check before approving a change.', source: 'workspace', enabled: true },
  { name: 'docs-style', displayName: 'Docs style guide', shortDescription: 'Voice, headings, and terms for the docs.', source: 'workspace', enabled: true },
  { name: 'bug-report-format', displayName: 'Bug report format', shortDescription: 'How a good bug report is structured.', source: 'user', enabled: true }
]

export const mcpCatalog = [{ name: 'issue-tracker', enabled: true, transport: 'stdio', origin: { kind: 'workspace' } }]

const plans: Record<StarterKey, Record<Lang, AgentPlan>> = {
  review: {
    en: {
      name: 'Code Reviewer',
      description: 'Reviews changes for correctness, risks, and missing tests without editing files.',
      instructions: [
        'You review code changes before they merge. You never edit files.',
        '',
        '- Lead with problems that would break behavior, then risks, then missing tests.',
        '- Point to the exact file and line for every finding.',
        '- Say plainly when a change looks good.'
      ].join('\n'),
      tools: ['ReadFile', 'GrepFiles', 'FindFiles'],
      skills: ['review-checklist'],
      mcp: [],
      approval: 'autoApprove'
    },
    zh: {
      name: '代码审查员',
      description: '只读审查代码改动，关注正确性、风险和缺失的测试。',
      instructions: [
        '你负责在代码合并前审查改动，从不修改文件。',
        '',
        '- 先说会破坏行为的问题，再说风险，最后说缺少的测试。',
        '- 每条意见都指出具体的文件和行号。',
        '- 改动没问题时直接说明。'
      ].join('\n'),
      tools: ['ReadFile', 'GrepFiles', 'FindFiles'],
      skills: ['review-checklist'],
      mcp: [],
      approval: 'autoApprove'
    }
  },
  docs: {
    en: {
      name: 'Docs Writer',
      description: 'Writes and updates documentation in the project’s established style.',
      instructions: [
        'You write and update the project’s documentation.',
        '',
        '- Read the nearest existing pages first and match their voice and structure.',
        '- Lead each page with what the reader can do, then how to do it.',
        '- Keep examples short and check that every command still works.'
      ].join('\n'),
      tools: ['ReadFile', 'GrepFiles', 'FindFiles', 'EditFile', 'WriteFile'],
      skills: ['docs-style'],
      mcp: [],
      approval: 'prompt'
    },
    zh: {
      name: '文档编写员',
      description: '按项目既有风格撰写和更新文档。',
      instructions: [
        '你负责撰写和更新项目文档。',
        '',
        '- 动笔前先读最相近的现有页面，保持语气和结构一致。',
        '- 每页先讲读者能做什么，再讲怎么做。',
        '- 示例保持简短，并确认每条命令都还能用。'
      ].join('\n'),
      tools: ['ReadFile', 'GrepFiles', 'FindFiles', 'EditFile', 'WriteFile'],
      skills: ['docs-style'],
      mcp: [],
      approval: 'prompt'
    }
  },
  triage: {
    en: {
      name: 'Bug Triage',
      description: 'Reviews incoming bugs, sets a priority, and logs them in the issue tracker.',
      instructions: [
        'You triage incoming bug reports.',
        '',
        '- Reproduce the bug from the report when you can, and say when you can’t.',
        '- Set a priority from P0 to P3 based on impact and how many people it affects.',
        '- Log each bug with steps to reproduce, expected and actual behavior, and the priority.'
      ].join('\n'),
      tools: ['ReadFile', 'GrepFiles', 'Exec', 'RequestUserInput'],
      skills: ['bug-report-format'],
      mcp: ['issue-tracker'],
      approval: 'prompt'
    },
    zh: {
      name: '缺陷分诊员',
      description: '审查新提交的缺陷，确定优先级并记录到问题跟踪系统。',
      instructions: [
        '你负责分诊新提交的缺陷报告。',
        '',
        '- 能复现就按报告复现，复现不了要说明。',
        '- 根据影响范围和受影响人数，给出 P0 到 P3 的优先级。',
        '- 记录每个缺陷时写清复现步骤、预期与实际表现，以及优先级。'
      ].join('\n'),
      tools: ['ReadFile', 'GrepFiles', 'Exec', 'RequestUserInput'],
      skills: ['bug-report-format'],
      mcp: ['issue-tracker'],
      approval: 'prompt'
    }
  }
}

const copy = {
  en: {
    opening: (name: string) => `Setting up ${name}.`,
    closing: 'Done. Review the draft on the left, change anything you like, then choose Create.',
    customName: 'My Agent',
    customInstructions: (text: string) => `You help with this: ${text}\n\n- Ask one clarifying question when a request is ambiguous.\n- Finish with a short summary of what you did.`,
    followUpOpening: 'Adding that to the instructions.',
    followUpClosing: 'Done. The new guidance is at the end of the instructions.'
  },
  zh: {
    opening: (name: string) => `正在创建「${name}」。`,
    closing: '好了。看看左边的草稿，想改哪里都可以，确认后选择创建。',
    customName: '我的助手',
    customInstructions: (text: string) => `你负责这件事：${text}\n\n- 需求不明确时，先问一个澄清问题。\n- 完成后用几句话总结做了什么。`,
    followUpOpening: '把这条加到指令里。',
    followUpClosing: '好了，新的要求已经加在指令末尾。'
  }
}

function set(tool: string, field: string, key: string, value: string): BuilderEdit {
  return { tool, field, args: { [key]: value }, change: { op: 'set', value } }
}

function add(tool: string, field: string, names: string[]): BuilderEdit {
  return { tool, field, args: { names }, change: { op: 'add', values: names, list: names } }
}

function planEdits(plan: AgentPlan): BuilderEdit[] {
  return [
    set('SetAgentName', 'name', 'name', plan.name),
    set('SetAgentDescription', 'description', 'description', plan.description),
    set('SetAgentInstructions', 'instructions', 'instructions', plan.instructions),
    {
      tool: 'SetAgentToolPolicy',
      field: 'tools.policy',
      args: { mode: 'allowList', names: plan.tools },
      change: { op: 'set', mode: 'allowList', list: plan.tools }
    },
    ...(plan.skills.length > 0 ? [add('AddAgentSkills', 'skills.preload', plan.skills)] : []),
    ...(plan.mcp.length > 0 ? [add('AddAgentMcpServers', 'mcp.servers', plan.mcp)] : []),
    set('SetAgentApproval', 'approval', 'policy', plan.approval)
  ]
}

export function starterScript(key: StarterKey | null, lang: Lang, request: string): BuilderScript {
  const text = copy[lang]
  const plan: AgentPlan = key ? plans[key][lang] : {
    name: text.customName,
    description: request.length > 120 ? `${request.slice(0, 117)}…` : request,
    instructions: text.customInstructions(request),
    tools: ['ReadFile', 'GrepFiles', 'FindFiles', 'WebSearch', 'TodoWrite'],
    skills: [],
    mcp: [],
    approval: 'prompt'
  }
  return { opening: text.opening(plan.name), edits: planEdits(plan), closing: text.closing }
}

export function followUpScript(lang: Lang, request: string, instructions: string): BuilderScript {
  const text = copy[lang]
  const value = `${instructions.trimEnd()}\n- ${request}`
  return {
    opening: text.followUpOpening,
    edits: [{ tool: 'AppendAgentInstructions', field: 'instructions', args: { text: `- ${request}` }, change: { op: 'append', value } }],
    closing: text.followUpClosing
  }
}
