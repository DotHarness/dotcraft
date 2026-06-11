/**
 * Canned demo content, authored in the AppServer wire shape that
 * `conversationStore.setTurns` ingests (the same conversion path real
 * sessions go through). Timestamps are fixed offsets from load time so
 * sidebar grouping ("Today") stays fresh while a given load renders
 * deterministically.
 */
import type { AppLocale } from '../../../../desktop/src/shared/locales'
import { DEMO_WORKSPACE_PATH } from '../mockApi'

const NOW = Date.now()

function minutesAgo(minutes: number): string {
  return new Date(NOW - minutes * 60_000).toISOString()
}

export interface DemoPlan {
  title: string
  overview: string
  content: string
  todos: Array<{ id: string; content: string; status: 'pending' | 'in_progress' | 'completed' }>
}

/** Full plan markdown in the product's CreatePlan convention: one H1 title,
 * then concise sections (`content` carries the section body). */
export function planToMarkdown(plan: DemoPlan): string {
  return `# ${plan.title}\n\n${plan.content}`
}

export interface DemoContextUsage {
  tokens: number
  contextWindow: number
  autoCompactThreshold: number
  warningThreshold: number
  errorThreshold: number
  percentLeft: number
}

export interface DemoThread {
  id: string
  displayName: string
  mode: 'agent' | 'plan'
  createdAt: string
  lastActiveAt: string
  /** Wire-shaped turns fed to conversationStore.setTurns. */
  turns: Array<Record<string, unknown>>
  plan?: DemoPlan
  contextUsage: DemoContextUsage
}

// ---------------------------------------------------------------------------
// Shared code samples (locale-independent)
// ---------------------------------------------------------------------------

const WORKER_OLD_TEXT = `async function syncWithRetry(batch: ChangeSet): Promise<void> {
  for (let attempt = 0; attempt < MAX_RETRIES; attempt++) {
    try {
      await pushBatch(batch)
      return
    } catch (err) {
      if (!isTransient(err)) throw err
    }
  }
  throw new SyncExhaustedError(batch.id)
}`

const WORKER_NEW_TEXT = `async function syncWithRetry(batch: ChangeSet): Promise<void> {
  for (let attempt = 0; attempt < MAX_RETRIES; attempt++) {
    try {
      await pushBatch(batch)
      return
    } catch (err) {
      if (!isTransient(err)) throw err
      // Exponential backoff with full jitter, capped at 30s.
      const ceiling = Math.min(BASE_DELAY_MS * 2 ** attempt, MAX_DELAY_MS)
      await sleep(Math.random() * ceiling)
    }
  }
  throw new SyncExhaustedError(batch.id)
}`

const WORKER_TEST_CONTENT = `import { describe, expect, it, vi } from 'vitest'
import { syncWithRetry } from './worker'

describe('syncWithRetry', () => {
  it('backs off exponentially between transient failures', async () => {
    vi.useFakeTimers()
    const delays: number[] = []
    vi.spyOn(global.Math, 'random').mockReturnValue(1)

    const push = vi
      .fn()
      .mockRejectedValueOnce(transientError())
      .mockRejectedValueOnce(transientError())
      .mockResolvedValueOnce(undefined)

    const run = syncWithRetry(batchFixture(), { push, onDelay: (ms) => delays.push(ms) })
    await vi.runAllTimersAsync()
    await run

    expect(delays).toEqual([200, 400])
    expect(push).toHaveBeenCalledTimes(3)
  })

  it('rethrows immediately on permanent errors', async () => {
    const push = vi.fn().mockRejectedValue(permanentError())
    await expect(syncWithRetry(batchFixture(), { push })).rejects.toThrow('403')
    expect(push).toHaveBeenCalledTimes(1)
  })
})`

const RG_OUTPUT = `src/sync/worker.ts
42:async function syncWithRetry(batch: ChangeSet): Promise<void> {
43:  for (let attempt = 0; attempt < MAX_RETRIES; attempt++) {
58:const MAX_RETRIES = 5`

const TEST_OUTPUT = `> orbit@1.4.2 test
> vitest run src/sync

 ✓ src/sync/worker.test.ts (2 tests) 311ms
   ✓ syncWithRetry > backs off exponentially between transient failures
   ✓ syncWithRetry > rethrows immediately on permanent errors

 Test Files  1 passed (1)
      Tests  2 passed (2)
   Duration  1.92s`

const GIT_LOG_OUTPUT = `f3a92c1 feat: gallery keyboard navigation (#214)
8b04d77 fix: debounce search suggestions (#213)
51c2e90 perf: lazy-load gallery thumbnails (#212)
2ad1f04 docs: self-hosting guide for the sync server (#210)`

// ---------------------------------------------------------------------------
// Turn builders
// ---------------------------------------------------------------------------

/**
 * Canned turns bypass wire conversion in `setTurns` (their `items` are already
 * arrays), so every item needs an explicit completed status — without one,
 * streaming-detection selectors treat tool calls as still in flight.
 */
function completed(turns: Array<Record<string, unknown>>): Array<Record<string, unknown>> {
  return turns.map((turn) => ({
    ...turn,
    items: (turn.items as Array<Record<string, unknown>>).map((item) => ({
      status: 'completed',
      ...item
    }))
  }))
}

interface RetryThreadStrings {
  userMessage: string
  reasoning: string
  locateMessage: string
  summaryMessage: string
}

function buildRetryTurns(threadId: string, s: RetryThreadStrings): Array<Record<string, unknown>> {
  const t = (id: string): string => `${threadId}-${id}`
  return completed([
    {
      id: t('turn-1'),
      threadId,
      status: 'completed',
      startedAt: minutesAgo(132),
      completedAt: minutesAgo(128),
      tokenUsage: { inputTokens: 18420, outputTokens: 2210 },
      items: [
        {
          id: t('i-user'),
          type: 'userMessage',
          text: s.userMessage,
          createdAt: minutesAgo(132)
        },
        {
          id: t('i-reasoning'),
          type: 'reasoningContent',
          reasoning: s.reasoning,
          createdAt: minutesAgo(132),
          completedAt: minutesAgo(131),
          elapsedSeconds: 9
        },
        {
          id: t('i-rg-call'),
          type: 'toolCall',
          toolName: 'Exec',
          toolCallId: t('c-rg'),
          arguments: { command: 'rg -n "retry" src/sync/worker.ts' },
          createdAt: minutesAgo(131)
        },
        {
          id: t('i-rg-exec'),
          type: 'commandExecution',
          toolCallId: t('c-rg'),
          command: 'rg -n "retry" src/sync/worker.ts',
          workingDirectory: DEMO_WORKSPACE_PATH,
          aggregatedOutput: RG_OUTPUT,
          exitCode: 0,
          executionStatus: 'completed',
          createdAt: minutesAgo(131)
        },
        {
          id: t('i-rg-result'),
          type: 'toolResult',
          toolCallId: t('c-rg'),
          result: RG_OUTPUT,
          success: true,
          createdAt: minutesAgo(131),
          completedAt: minutesAgo(131)
        },
        {
          id: t('i-locate'),
          type: 'agentMessage',
          text: s.locateMessage,
          createdAt: minutesAgo(130)
        },
        {
          id: t('i-edit-call'),
          type: 'toolCall',
          toolName: 'EditFile',
          toolCallId: t('c-edit'),
          arguments: {
            path: 'src/sync/worker.ts',
            oldText: WORKER_OLD_TEXT,
            newText: WORKER_NEW_TEXT
          },
          createdAt: minutesAgo(130)
        },
        {
          id: t('i-edit-result'),
          type: 'toolResult',
          toolCallId: t('c-edit'),
          result: 'Successfully edited src/sync/worker.ts',
          success: true,
          createdAt: minutesAgo(130),
          completedAt: minutesAgo(130)
        },
        {
          id: t('i-write-call'),
          type: 'toolCall',
          toolName: 'WriteFile',
          toolCallId: t('c-write'),
          arguments: {
            path: 'src/sync/worker.test.ts',
            content: WORKER_TEST_CONTENT
          },
          createdAt: minutesAgo(129)
        },
        {
          id: t('i-write-result'),
          type: 'toolResult',
          toolCallId: t('c-write'),
          result: 'Wrote 29 lines to src/sync/worker.test.ts',
          success: true,
          createdAt: minutesAgo(129),
          completedAt: minutesAgo(129)
        },
        {
          id: t('i-test-call'),
          type: 'toolCall',
          toolName: 'Exec',
          toolCallId: t('c-test'),
          arguments: { command: 'npm test -- src/sync' },
          createdAt: minutesAgo(129)
        },
        {
          id: t('i-test-exec'),
          type: 'commandExecution',
          toolCallId: t('c-test'),
          command: 'npm test -- src/sync',
          workingDirectory: DEMO_WORKSPACE_PATH,
          aggregatedOutput: TEST_OUTPUT,
          exitCode: 0,
          executionStatus: 'completed',
          createdAt: minutesAgo(129)
        },
        {
          id: t('i-test-result'),
          type: 'toolResult',
          toolCallId: t('c-test'),
          result: TEST_OUTPUT,
          success: true,
          createdAt: minutesAgo(128),
          completedAt: minutesAgo(128)
        },
        {
          id: t('i-summary'),
          type: 'agentMessage',
          text: s.summaryMessage,
          createdAt: minutesAgo(128)
        }
      ]
    }
  ])
}

interface PlanThreadStrings {
  userMessage: string
  reasoning: string
  agentMessage: string
  plan: DemoPlan
}

function buildPlanTurns(threadId: string, s: PlanThreadStrings): Array<Record<string, unknown>> {
  const t = (id: string): string => `${threadId}-${id}`
  return completed([
    {
      id: t('turn-1'),
      threadId,
      status: 'completed',
      startedAt: minutesAgo(45),
      completedAt: minutesAgo(43),
      tokenUsage: { inputTokens: 9210, outputTokens: 1480 },
      items: [
        {
          id: t('i-user'),
          type: 'userMessage',
          text: s.userMessage,
          createdAt: minutesAgo(45)
        },
        {
          id: t('i-reasoning'),
          type: 'reasoningContent',
          reasoning: s.reasoning,
          createdAt: minutesAgo(45),
          completedAt: minutesAgo(44),
          elapsedSeconds: 14
        },
        {
          id: t('i-plan-call'),
          type: 'toolCall',
          toolName: 'CreatePlan',
          toolCallId: t('c-plan'),
          arguments: {
            plan: planToMarkdown(s.plan),
            todos: s.plan.todos.map(({ id, content }) => ({ id, content }))
          },
          createdAt: minutesAgo(44)
        },
        {
          id: t('i-plan-result'),
          type: 'toolResult',
          toolCallId: t('c-plan'),
          result: 'Plan created.',
          success: true,
          createdAt: minutesAgo(44),
          completedAt: minutesAgo(44)
        },
        {
          id: t('i-agent'),
          type: 'agentMessage',
          text: s.agentMessage,
          createdAt: minutesAgo(43)
        }
      ]
    }
  ])
}

interface DigestThreadStrings {
  userMessage: string
  triggerLabel: string
  agentMessage: string
}

function buildDigestTurns(threadId: string, s: DigestThreadStrings): Array<Record<string, unknown>> {
  const t = (id: string): string => `${threadId}-${id}`
  return completed([
    {
      id: t('turn-1'),
      threadId,
      status: 'completed',
      startedAt: minutesAgo(495),
      completedAt: minutesAgo(493),
      tokenUsage: { inputTokens: 6120, outputTokens: 940 },
      items: [
        {
          id: t('i-user'),
          type: 'userMessage',
          text: s.userMessage,
          triggerKind: 'cron',
          triggerLabel: s.triggerLabel,
          createdAt: minutesAgo(495)
        },
        {
          id: t('i-log-call'),
          type: 'toolCall',
          toolName: 'Exec',
          toolCallId: t('c-log'),
          arguments: { command: 'git log --oneline --since=yesterday' },
          createdAt: minutesAgo(495)
        },
        {
          id: t('i-log-exec'),
          type: 'commandExecution',
          toolCallId: t('c-log'),
          command: 'git log --oneline --since=yesterday',
          workingDirectory: DEMO_WORKSPACE_PATH,
          aggregatedOutput: GIT_LOG_OUTPUT,
          exitCode: 0,
          executionStatus: 'completed',
          createdAt: minutesAgo(495)
        },
        {
          id: t('i-log-result'),
          type: 'toolResult',
          toolCallId: t('c-log'),
          result: GIT_LOG_OUTPUT,
          success: true,
          createdAt: minutesAgo(494),
          completedAt: minutesAgo(494)
        },
        {
          id: t('i-agent'),
          type: 'agentMessage',
          text: s.agentMessage,
          createdAt: minutesAgo(493)
        }
      ]
    }
  ])
}

// ---------------------------------------------------------------------------
// Locale content
// ---------------------------------------------------------------------------

function englishThreads(): DemoThread[] {
  const retryId = 'demo-thread-retry'
  const planId = 'demo-thread-plan'
  const digestId = 'demo-thread-digest'

  const plan: DemoPlan = {
    title: 'Ship dark mode',
    overview:
      'Introduce a token-driven dark theme: define semantic color tokens, migrate hardcoded colors, add the toggle, and verify contrast.',
    content: [
      '## Summary',
      '',
      'Introduce a token-driven dark theme: define semantic color tokens, migrate hardcoded colors, add the toggle, and verify contrast.',
      '',
      '## Implementation Changes',
      '',
      '1. Inventory hardcoded colors across `src/components` and map each to a semantic token.',
      '2. Define dark values for every token in `src/styles/tokens.css`.',
      '3. Add a theme toggle in Settings persisted to local storage.',
      '4. Run the contrast audit and fix anything under WCAG AA.',
      '',
      '## Test Plan',
      '',
      '- Snapshot the five highest-traffic screens in both themes.',
      '- Axe contrast audit gate in CI.'
    ].join('\n'),
    todos: [
      { id: 'todo-1', content: 'Inventory hardcoded colors and map to tokens', status: 'completed' },
      { id: 'todo-2', content: 'Define dark values in tokens.css', status: 'completed' },
      { id: 'todo-3', content: 'Add persisted theme toggle in Settings', status: 'in_progress' },
      { id: 'todo-4', content: 'Contrast audit (WCAG AA)', status: 'pending' }
    ]
  }

  return [
    {
      id: retryId,
      displayName: 'Add retry backoff to sync worker',
      mode: 'agent',
      createdAt: minutesAgo(140),
      lastActiveAt: minutesAgo(128),
      contextUsage: {
        tokens: 24830,
        contextWindow: 200000,
        autoCompactThreshold: 160000,
        warningThreshold: 170000,
        errorThreshold: 190000,
        percentLeft: 87
      },
      turns: buildRetryTurns(retryId, {
        userMessage:
          'Our sync worker hammers the API whenever the connection drops — retries fire in a tight loop. Add exponential backoff with jitter, and cover it with a test.',
        reasoning:
          'The retry loop most likely lives in src/sync/worker.ts. I should confirm how transient errors are detected before changing the loop, then add full-jitter backoff so concurrent workers do not synchronize their retries.',
        locateMessage:
          'Found it — `syncWithRetry` in `src/sync/worker.ts:42` retries immediately with no delay. I will add exponential backoff with full jitter (capped at 30s) and keep permanent errors failing fast.',
        summaryMessage: [
          'Done. Two changes:',
          '',
          '- **`src/sync/worker.ts`** — transient failures now back off exponentially with full jitter (`min(200ms · 2^attempt, 30s)`), so a flaky network no longer turns into a request storm. Permanent errors still rethrow immediately.',
          '- **`src/sync/worker.test.ts`** — new tests pin the backoff sequence (200ms, 400ms) and the fail-fast path.',
          '',
          'All tests pass. The diff is staged in the Changes tab if you want to review before committing.'
        ].join('\n')
      })
    },
    {
      id: planId,
      displayName: 'Plan: ship dark mode',
      mode: 'plan',
      createdAt: minutesAgo(50),
      lastActiveAt: minutesAgo(43),
      plan,
      contextUsage: {
        tokens: 10690,
        contextWindow: 200000,
        autoCompactThreshold: 160000,
        warningThreshold: 170000,
        errorThreshold: 190000,
        percentLeft: 94
      },
      turns: buildPlanTurns(planId, {
        userMessage:
          'Users keep asking for dark mode. Before touching code, draft a plan for how we would ship it across the app.',
        reasoning:
          'Dark mode touches every hardcoded color. A safe rollout is token-first: inventory, tokenize, then toggle. The contrast audit should be an explicit final step.',
        agentMessage:
          'I drafted a four-step plan — it is in the **Plan** tab on the right. The riskiest step is the color inventory (step 1); everything after that is mechanical. Approve the plan and I will start with the inventory.',
        plan
      })
    },
    {
      id: digestId,
      displayName: 'Nightly changelog digest',
      mode: 'agent',
      createdAt: minutesAgo(500),
      lastActiveAt: minutesAgo(493),
      contextUsage: {
        tokens: 7110,
        contextWindow: 200000,
        autoCompactThreshold: 160000,
        warningThreshold: 170000,
        errorThreshold: 190000,
        percentLeft: 96
      },
      turns: buildDigestTurns(digestId, {
        userMessage: 'Summarize yesterday’s merged changes for the team channel.',
        triggerLabel: 'nightly-digest',
        agentMessage: [
          'Yesterday on `main` — 4 merges:',
          '',
          '| Change | PR |',
          '| --- | --- |',
          '| Gallery keyboard navigation | #214 |',
          '| Debounced search suggestions | #213 |',
          '| Lazy-loaded gallery thumbnails | #212 |',
          '| Self-hosting guide for the sync server | #210 |',
          '',
          'Highlight: the gallery is now fully keyboard-navigable, which closes the top accessibility request. Digest posted to **#orbit-dev**.'
        ].join('\n')
      })
    }
  ]
}

function chineseThreads(): DemoThread[] {
  const retryId = 'demo-thread-retry'
  const planId = 'demo-thread-plan'
  const digestId = 'demo-thread-digest'

  const plan: DemoPlan = {
    title: '上线深色模式',
    overview:
      '以设计令牌驱动的方式引入深色主题：定义语义化颜色令牌、迁移硬编码颜色、添加切换开关并校验对比度。',
    content: [
      '## 概要',
      '',
      '以设计令牌驱动的方式引入深色主题：定义语义化颜色令牌、迁移硬编码颜色、添加切换开关并校验对比度。',
      '',
      '## 实施步骤',
      '',
      '1. 清点 `src/components` 中的硬编码颜色，并为每处映射语义化令牌。',
      '2. 在 `src/styles/tokens.css` 中为全部令牌补充深色取值。',
      '3. 在设置页添加主题切换开关，并持久化到本地存储。',
      '4. 运行对比度审计，修复所有低于 WCAG AA 的项。',
      '',
      '## 测试计划',
      '',
      '- 对流量最高的五个页面做双主题快照对比。',
      '- 在 CI 中加入 Axe 对比度审计门禁。'
    ].join('\n'),
    todos: [
      { id: 'todo-1', content: '清点硬编码颜色并映射到令牌', status: 'completed' },
      { id: 'todo-2', content: '在 tokens.css 中定义深色取值', status: 'completed' },
      { id: 'todo-3', content: '在设置页添加可持久化的主题开关', status: 'in_progress' },
      { id: 'todo-4', content: '对比度审计（WCAG AA）', status: 'pending' }
    ]
  }

  return [
    {
      id: retryId,
      displayName: '为同步任务添加重试退避',
      mode: 'agent',
      createdAt: minutesAgo(140),
      lastActiveAt: minutesAgo(128),
      contextUsage: {
        tokens: 24830,
        contextWindow: 200000,
        autoCompactThreshold: 160000,
        warningThreshold: 170000,
        errorThreshold: 190000,
        percentLeft: 87
      },
      turns: buildRetryTurns(retryId, {
        userMessage:
          '网络一断，同步任务就在死循环里疯狂重试、打爆 API。请加上带抖动的指数退避，并补一个测试。',
        reasoning:
          '重试循环大概率在 src/sync/worker.ts。先确认瞬时错误的判定方式再改循环，然后用全抖动退避，避免多个 worker 的重试节奏同步。',
        locateMessage:
          '找到了——`src/sync/worker.ts:42` 的 `syncWithRetry` 在无延迟地立即重试。我会加入全抖动的指数退避（上限 30 秒），并保持永久性错误快速失败。',
        summaryMessage: [
          '完成，两处改动：',
          '',
          '- **`src/sync/worker.ts`** —— 瞬时失败现在按全抖动指数退避（`min(200ms · 2^attempt, 30s)`），网络抖动不会再演变成请求风暴；永久性错误仍然立即抛出。',
          '- **`src/sync/worker.test.ts`** —— 新增测试固定了退避序列（200ms、400ms）和快速失败路径。',
          '',
          '测试全部通过。改动已经在右侧 Changes 标签页中，可以在提交前审阅。'
        ].join('\n')
      })
    },
    {
      id: planId,
      displayName: '计划：上线深色模式',
      mode: 'plan',
      createdAt: minutesAgo(50),
      lastActiveAt: minutesAgo(43),
      plan,
      contextUsage: {
        tokens: 10690,
        contextWindow: 200000,
        autoCompactThreshold: 160000,
        warningThreshold: 170000,
        errorThreshold: 190000,
        percentLeft: 94
      },
      turns: buildPlanTurns(planId, {
        userMessage: '用户一直在要深色模式。先别动代码，起草一份全应用范围的上线计划。',
        reasoning:
          '深色模式会触及所有硬编码颜色。稳妥的路线是令牌优先：先清点、再令牌化、最后加开关。对比度审计应作为明确的收尾步骤。',
        agentMessage:
          '我起草了一份四步计划——见右侧 **Plan** 标签页。风险最高的是第 1 步颜色清点，其余步骤都是机械操作。确认计划后我就从清点开始。',
        plan
      })
    },
    {
      id: digestId,
      displayName: '每日更新摘要',
      mode: 'agent',
      createdAt: minutesAgo(500),
      lastActiveAt: minutesAgo(493),
      contextUsage: {
        tokens: 7110,
        contextWindow: 200000,
        autoCompactThreshold: 160000,
        warningThreshold: 170000,
        errorThreshold: 190000,
        percentLeft: 96
      },
      turns: buildDigestTurns(digestId, {
        userMessage: '汇总昨天合并的改动，发到团队频道。',
        triggerLabel: 'nightly-digest',
        agentMessage: [
          '昨天 `main` 分支共 4 个合并：',
          '',
          '| 改动 | PR |',
          '| --- | --- |',
          '| 图库键盘导航 | #214 |',
          '| 搜索建议防抖 | #213 |',
          '| 图库缩略图懒加载 | #212 |',
          '| 同步服务自托管指南 | #210 |',
          '',
          '亮点：图库现已支持完整键盘导航，解决了呼声最高的无障碍诉求。摘要已发布到 **#orbit-dev**。'
        ].join('\n')
      })
    }
  ]
}

export function getDemoThreads(locale: AppLocale): DemoThread[] {
  return locale === 'zh-Hans' ? chineseThreads() : englishThreads()
}
