import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const tokensCssPath = resolve(__dirname, '../styles/tokens.css')
const conversationColumnPath = resolve(__dirname, '../components/conversation/ConversationColumn.tsx')
const composerShellPath = resolve(__dirname, '../components/conversation/ComposerShell.tsx')
const inputComposerPath = resolve(__dirname, '../components/conversation/InputComposer.tsx')
const subAgentDockPath = resolve(__dirname, '../components/conversation/SubAgentDock.tsx')
const backgroundActivityDockLayoutPath = resolve(__dirname, '../components/conversation/backgroundActivityDockLayout.ts')
const approvalPolicyPickerPath = resolve(__dirname, '../components/conversation/ApprovalPolicyPicker.tsx')
const planApprovalComposerPath = resolve(__dirname, '../components/conversation/PlanApprovalComposer.tsx')
const requestUserInputComposerPath = resolve(__dirname, '../components/conversation/RequestUserInputComposer.tsx')
const approvalDecisionComposerPath = resolve(__dirname, '../components/conversation/ApprovalDecisionComposer.tsx')
const composerChoiceRowPath = resolve(__dirname, '../components/conversation/ComposerChoiceRow.tsx')
const decisionComposerChromePath = resolve(__dirname, '../components/conversation/DecisionComposerChrome.tsx')
const messageStreamPath = resolve(__dirname, '../components/conversation/MessageStream.tsx')
const agentResponseBlockPath = resolve(__dirname, '../components/conversation/AgentResponseBlock.tsx')
const richInputAreaPath = resolve(__dirname, '../components/conversation/RichInputArea.tsx')
const conversationWelcomePath = resolve(__dirname, '../components/conversation/ConversationWelcome.tsx')
const thinkingIndicatorPath = resolve(__dirname, '../components/conversation/ThinkingIndicator.tsx')
const terminalViewerPath = resolve(__dirname, '../components/detail/viewers/TerminalViewerTab.tsx')
const newTaskDialogPath = resolve(__dirname, '../components/automations/NewTaskDialog.tsx')
const dashboardPath = resolve(__dirname, '../../../../src/DotCraft.Core/Resources/DashBoard.html')
const dashboardLoginPath = resolve(__dirname, '../../../../src/DotCraft.Core/Resources/DashBoardLogin.html')

const systemSansStack = '-apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif'
const systemMonoStack = 'ui-monospace, "SFMono-Regular", "SF Mono", Menlo,\n    Consolas, "Liberation Mono", monospace'
const systemMonoStackInline = 'ui-monospace, "SFMono-Regular", "SF Mono", Menlo, Consolas, "Liberation Mono", monospace'
const cjkFallbacks = [
  'Microsoft YaHei UI',
  'PingFang SC',
  'Hiragino Sans GB',
  'Noto Sans CJK SC',
  'Source Han Sans SC',
  'Hiragino Sans',
  'Hiragino Kaku Gothic ProN',
  'Yu Gothic UI',
  'Yu Gothic',
  'Meiryo',
  'Noto Sans CJK JP',
  'Source Han Sans JP',
  'Apple SD Gothic Neo',
  'Malgun Gothic',
  'Noto Sans CJK KR',
  'Source Han Sans KR'
]

function readSource(path: string): string {
  return readFileSync(path, 'utf8').replace(/\r\n/g, '\n')
}

function sourceSlice(source: string, startMarker: string, endMarker: string): string {
  const start = source.indexOf(startMarker)
  expect(start).toBeGreaterThanOrEqual(0)
  const end = source.indexOf(endMarker, start)
  expect(end).toBeGreaterThan(start)
  return source.slice(start, end)
}

describe('conversation typography tokens', () => {
  it('uses system font stacks with locale-aware CJK fallbacks', () => {
    const tokensCss = readSource(tokensCssPath)
    const dashboardSource = readSource(dashboardPath)
    const dashboardLoginSource = readSource(dashboardLoginPath)
    const terminalViewerSource = readSource(terminalViewerPath)
    const newTaskDialogSource = readSource(newTaskDialogPath)

    expect(tokensCss).toContain(`--font-sans-default: ${systemSansStack}`)
    expect(tokensCss).toContain(`--font-mono-default: ${systemMonoStack}`)
    expect(tokensCss).toContain('--font-ui: var(--font-sans-default)')
    expect(tokensCss).toContain('--font-body: var(--font-sans-default)')
    expect(tokensCss).toContain('--font-sans: var(--font-sans-default)')
    expect(tokensCss).toContain('--font-mono: var(--font-mono-default)')
    expect(tokensCss).toContain('--font-sans-zh:')
    expect(tokensCss).toContain('--font-sans-ja:')
    expect(tokensCss).toContain('--font-sans-ko:')
    for (const fontName of cjkFallbacks) {
      expect(tokensCss).toContain(fontName)
    }
    for (const [selector, token] of [
      [':root:lang(zh)', '--font-sans-zh'],
      [':root:lang(ja)', '--font-sans-ja'],
      [':root:lang(ko)', '--font-sans-ko']
    ] as const) {
      const block = sourceSlice(tokensCss, `${selector} {`, '}\n')
      expect(block).toContain(`--font-ui: var(${token})`)
      expect(block).toContain(`--font-body: var(${token})`)
      expect(block).toContain(`--font-sans: var(${token})`)
    }
    expect(tokensCss).not.toContain('@font-face')
    expect(tokensCss).not.toContain('.woff')
    expect(tokensCss).not.toContain('.ttf')
    expect(dashboardSource).toContain(`--font-sans: ${systemSansStack}`)
    expect(dashboardSource).toContain(`--font-mono: ${systemMonoStackInline}`)
    expect(dashboardLoginSource).toContain(`--font-sans: ${systemSansStack}`)
    expect(dashboardLoginSource).toContain(`--font-mono: ${systemMonoStackInline}`)
    expect(terminalViewerSource).toContain(systemMonoStackInline)
    expect(newTaskDialogSource).toContain("fontFamily: 'var(--font-mono)'")

    for (const fontName of cjkFallbacks) {
      expect(dashboardSource).not.toContain(fontName)
      expect(dashboardLoginSource).not.toContain(fontName)
    }
    expect(dashboardSource).not.toContain('font-smoothing')
    expect(dashboardLoginSource).not.toContain('font-smoothing')
  })

  it('uses conversation font, spacing, and code sizing tokens', () => {
    const tokensCss = readSource(tokensCssPath)

    expect(tokensCss).toContain('--conversation-font-size: 14px')
    expect(tokensCss).toContain('--conversation-line-height: 1.5')
    expect(tokensCss).toContain('--conversation-font-weight: 400')
    expect(tokensCss).toContain('--conversation-reading-width: 48rem')
    expect(tokensCss).toContain('--conversation-block-gap: 12px')
    expect(tokensCss).toContain('--conversation-tool-assistant-gap: 16px')
    expect(tokensCss).toContain('--conversation-tool-run-gap: 4px')
    expect(tokensCss).toContain('--composer-placeholder:')
    expect(tokensCss).toContain('--composer-footer-text:')
    expect(tokensCss).toContain('--composer-footer-muted:')
    expect(tokensCss).toContain('--composer-footer-highlight:')
    expect(tokensCss).toContain('--composer-control-text:')
    expect(tokensCss).toContain('--composer-control-muted:')
    expect(tokensCss).toContain('var(--text-primary) 66%, var(--bg-primary)')
    expect(tokensCss).toContain('--text-body-size: var(--conversation-font-size)')
    expect(tokensCss).toContain('--text-body-line-height: var(--conversation-line-height)')
    expect(tokensCss).toContain('--text-code-size: 12px')
    expect(tokensCss).toContain('--text-code-line-height: 1.44')
    expect(tokensCss).not.toContain('--conversation-font-weight: 430')
    expect(tokensCss).not.toContain('--text-body-size: 13px')
    expect(tokensCss).not.toContain('--conversation-reading-width: 780px')
    expect(tokensCss).not.toContain('-webkit-font-smoothing: antialiased')
  })

  it('keeps composer input on a dedicated control surface instead of the glass surface', () => {
    const tokensCss = readSource(tokensCssPath)
    const composerShellSource = readSource(composerShellPath)

    expect(tokensCss).toContain('--composer-input-background: #2b2b2b')
    expect(tokensCss).toContain('--composer-input-background: #ffffff')
    expect(tokensCss).toContain('--composer-input-border:')
    expect(tokensCss).toContain('--composer-input-border-focus:')
    expect(tokensCss).toContain('--composer-input-shadow:')

    expect(composerShellSource).toContain('var(--composer-input-background)')
    expect(composerShellSource).toContain('var(--composer-input-border)')
    expect(composerShellSource).toContain('var(--composer-input-border-focus)')
    expect(composerShellSource).toContain('var(--composer-input-shadow)')
    expect(composerShellSource).not.toContain('var(--glass-surface)')
    expect(composerShellSource).not.toContain('var(--glass-surface-strong)')
    expect(composerShellSource).not.toContain('backdropFilter')
    expect(composerShellSource).not.toContain('WebkitBackdropFilter')
    expect(composerShellSource).toContain("padding: '0 0 14px'")
    expect(composerShellSource).toContain("isolation: 'isolate'")
    expect(composerShellSource).toContain("bottom: 'calc(100% - 1px)'")
    expect(composerShellSource).toContain('zIndex: 0')
    expect(composerShellSource).toContain('zIndex: 1')
    expect(composerShellSource).not.toContain('topAccessoryOverlapPx')
    expect(composerShellSource).not.toContain("padding: '0 14px 14px'")
    expect(composerShellSource).not.toContain("padding: topAccessoryVisible ? '0 14px 14px' : '14px 14px'")
  })

  it('layers the background activity dock behind the foreground composer without moving it down', () => {
    const inputComposerSource = readSource(inputComposerPath)
    const subAgentDockSource = readSource(subAgentDockPath)
    const backgroundActivityDockLayoutSource = readSource(backgroundActivityDockLayoutPath)
    const messageStreamSource = readSource(messageStreamPath)

    expect(inputComposerSource).not.toContain('BACKGROUND_ACTIVITY_DOCK_OVERLAP_PX')
    expect(subAgentDockSource).toContain("margin: '0 auto -1px'")
    expect(backgroundActivityDockLayoutSource).not.toContain('BACKGROUND_ACTIVITY_DOCK_OVERLAP_PX')
    expect(backgroundActivityDockLayoutSource).not.toContain('estimateBackgroundActivityDockVisibleHeightPx')
    expect(messageStreamSource).toContain('estimateBackgroundActivityDockHeightPx')
    expect(messageStreamSource).not.toContain('estimateBackgroundActivityDockVisibleHeightPx')
  })

  it('applies conversation spacing tokens to the message stream rhythm', () => {
    const messageStreamSource = readSource(messageStreamPath)
    const agentResponseBlockSource = readSource(agentResponseBlockPath)
    const thinkingIndicatorSource = readSource(thinkingIndicatorPath)

    expect(messageStreamSource).toContain("padding: '32px clamp(20px, 4vw, 40px) 28px'")
    expect(messageStreamSource).toContain("gap: 'var(--conversation-block-gap)'")
    expect(messageStreamSource).not.toContain("linear-gradient(transparent, var(--main-surface-muted))")
    expect(agentResponseBlockSource).toContain("gap: 'var(--conversation-tool-assistant-gap)'")
    expect(agentResponseBlockSource).toContain("gap: 'var(--conversation-tool-run-gap)'")
    expect(agentResponseBlockSource).toContain('ConversationNodeFlow')
    expect(thinkingIndicatorSource).not.toContain("marginBottom: '6px'")
    expect(messageStreamSource).not.toContain("padding: '28px clamp(20px, 4vw, 44px)'")
    expect(messageStreamSource).not.toContain("gap: '16px'")
  })

  it('uses dedicated footer colors so the model is the only non-semantic highlight', () => {
    const inputComposerSource = readSource(inputComposerPath)
    const approvalPolicyPickerSource = readSource(approvalPolicyPickerPath)
    const richInputAreaSource = readSource(richInputAreaPath)
    const welcomeSource = readSource(conversationWelcomePath)

    expect(richInputAreaSource).toContain('var(--composer-placeholder, var(--text-dimmed))')
    expect(inputComposerSource).toContain('var(--composer-footer-highlight)')
    expect(inputComposerSource).toContain('var(--composer-footer-muted)')
    expect(approvalPolicyPickerSource).toContain('var(--composer-footer-text)')
    expect(approvalPolicyPickerSource).toContain('var(--warning)')
    expect(welcomeSource).toContain('var(--composer-footer-highlight)')
    expect(welcomeSource).toContain('var(--composer-footer-muted)')
  })

  it('centers the conversation and composer in one max-width reading column', () => {
    const columnSource = readSource(conversationColumnPath)
    const messageStreamSource = readSource(messageStreamPath)
    const inputComposerSource = readSource(inputComposerPath)
    const planApprovalComposerSource = readSource(planApprovalComposerPath)
    const requestUserInputComposerSource = readSource(requestUserInputComposerPath)
    const approvalDecisionComposerSource = readSource(approvalDecisionComposerPath)
    const welcomeSource = readSource(conversationWelcomePath)

    expect(columnSource).toContain("maxWidth: 'var(--conversation-reading-width)'")
    expect(columnSource).toContain("margin: '0 auto'")
    expect(messageStreamSource).toContain('<ConversationColumn')
    expect(inputComposerSource).toContain('<ConversationColumn>')
    expect(planApprovalComposerSource).toContain('<ConversationColumn>')
    expect(requestUserInputComposerSource).toContain('<ConversationColumn>')
    expect(approvalDecisionComposerSource).toContain('<ConversationColumn>')
    expect(inputComposerSource).toContain("padding: '0 clamp(20px, 4vw, 40px)'")
    expect(planApprovalComposerSource).toContain("padding: '0 clamp(20px, 4vw, 40px)'")
    expect(requestUserInputComposerSource).toContain("padding: '0 clamp(20px, 4vw, 40px)'")
    expect(approvalDecisionComposerSource).toContain("padding: '0 clamp(20px, 4vw, 40px)'")
    expect(welcomeSource).toContain("maxWidth: 'var(--conversation-reading-width)'")
    expect(welcomeSource).not.toContain("maxWidth: '720px'")
  })

  it('uses compact decision hierarchy for request user input', () => {
    const requestUserInputComposerSource = readSource(requestUserInputComposerPath)
    const composerChoiceRowSource = readSource(composerChoiceRowPath)
    const decisionComposerChromeSource = readSource(decisionComposerChromePath)
    const titleStyleBlock = sourceSlice(
      decisionComposerChromeSource,
      'export const decisionComposerTitleStyle: CSSProperties = {',
      '\n}\n\nexport const decisionComposerFooterActionsStyle'
    )
    const bodyStyleBlock = sourceSlice(
      decisionComposerChromeSource,
      'export const decisionComposerBodyStyle: CSSProperties = {',
      '\n}\n\nexport const decisionComposerChoiceListStyle'
    )
    const choiceListStyleBlock = sourceSlice(
      decisionComposerChromeSource,
      'export const decisionComposerChoiceListStyle: CSSProperties = {',
      '\n}\n\nexport const decisionComposerTitleRowStyle'
    )
    const questionStyleBlock = sourceSlice(
      requestUserInputComposerSource,
      'const questionStyle: CSSProperties = {',
      '\n}\n\nconst questionProgressStyle'
    )
    const otherInputStyleBlock = sourceSlice(
      requestUserInputComposerSource,
      'function otherInputStyle',
      '\n}\n'
    )
    const choiceRowStyleBlock = sourceSlice(
      composerChoiceRowSource,
      'export function composerChoiceRowStyle',
      '\n}\n\nfunction composerChoiceRowBackground'
    )
    const choiceNumberStyleBlock = sourceSlice(
      composerChoiceRowSource,
      'export function composerChoiceNumberStyle',
      '\n}\n\nfunction composerChoiceLabelWrapStyle'
    )
    const choiceLabelStyleBlock = sourceSlice(
      composerChoiceRowSource,
      'export function composerChoiceLabelStyle',
      '\n}\n\nconst composerChoiceInfoIconStyle'
    )

    expect(requestUserInputComposerSource).toContain("const questionStyle: CSSProperties")
    expect(requestUserInputComposerSource).toContain("from './DecisionComposerChrome'")
    expect(requestUserInputComposerSource).toContain('decisionComposerBodyStyle')
    expect(requestUserInputComposerSource).toContain('decisionComposerChoiceListStyle')
    expect(requestUserInputComposerSource).toContain('DecisionDismissButton')
    expect(requestUserInputComposerSource).toContain('DecisionSubmitButton')
    expect(composerChoiceRowSource).toContain("export type ComposerChoiceDensity = 'compact' | 'decision'")
    expect(requestUserInputComposerSource).toContain('density="decision"')
    expect(requestUserInputComposerSource).toContain("composerChoiceRowStyle(selected, false, highlighted, 'decision')")
    expect(bodyStyleBlock).toContain("gap: '8px'")
    expect(choiceListStyleBlock).toContain("gap: '4px'")
    expect(titleStyleBlock).toContain("fontSize: '14px'")
    expect(titleStyleBlock).toContain('fontWeight: 600')
    expect(titleStyleBlock).toContain("lineHeight: '20px'")
    expect(questionStyleBlock).toContain('...decisionComposerTitleStyle')
    expect(otherInputStyleBlock).toContain("color: active ? 'var(--text-primary)' : 'var(--text-dimmed)'")
    expect(otherInputStyleBlock).toContain("fontSize: 'var(--text-body-size)'")
    expect(otherInputStyleBlock).toContain("lineHeight: 'var(--text-body-line-height)'")
    expect(otherInputStyleBlock).toContain("fontWeight: 'var(--conversation-font-weight)'")
    expect(choiceRowStyleBlock).toContain("density: ComposerChoiceDensity = 'compact'")
    expect(choiceRowStyleBlock).toContain("minHeight: decision ? '40px' : '32px'")
    expect(choiceRowStyleBlock).toContain("padding: decision ? '0 10px' : '4px 8px'")
    expect(choiceRowStyleBlock).toContain("borderRadius: decision ? '10px' : '8px'")
    expect(choiceNumberStyleBlock).toContain("fontSize: decision ? '13px' : 'var(--text-body-size)'")
    expect(choiceNumberStyleBlock).toContain("lineHeight: decision ? '24px' : 'var(--text-body-line-height)'")
    expect(choiceNumberStyleBlock).toContain("fontWeight: 'var(--conversation-font-weight)'")
    expect(choiceLabelStyleBlock).toContain("fontSize: 'var(--text-body-size)'")
    expect(choiceLabelStyleBlock).toContain("fontWeight: decision && selected ? 600 : 'var(--conversation-font-weight)'")
    expect(choiceLabelStyleBlock).toContain("lineHeight: 'var(--text-body-line-height)'")
    expect(choiceRowStyleBlock).not.toContain("minHeight: '44px'")
    expect(requestUserInputComposerSource).not.toContain("<div style={{ display: 'grid', gap: '10px' }}>")
  })

  it('uses the shared decision chrome for plan approval', () => {
    const planApprovalComposerSource = readSource(planApprovalComposerPath)
    const decisionComposerChromeSource = readSource(decisionComposerChromePath)

    expect(planApprovalComposerSource).toContain("from './DecisionComposerChrome'")
    expect(planApprovalComposerSource).toContain('DecisionDismissButton')
    expect(planApprovalComposerSource).toContain('DecisionSubmitButton')
    expect(planApprovalComposerSource).toContain('decisionComposerTitleStyle')
    expect(planApprovalComposerSource).toContain('PlanAdjustmentRow')
    expect(planApprovalComposerSource).toContain('ComposerChoiceArrowHints')
    expect(planApprovalComposerSource).toContain("event.key === 'ArrowDown'")
    expect(planApprovalComposerSource).toContain("event.key === 'ArrowUp'")
    expect(decisionComposerChromeSource).toContain("border: '1px solid var(--text-primary)'")
    expect(decisionComposerChromeSource).toContain("background: 'var(--text-primary)'")
    expect(decisionComposerChromeSource).toContain("color: 'var(--bg-primary)'")
    expect(decisionComposerChromeSource).toContain('<CornerDownLeft size={14}')
  })

  it('keeps approval decision composer on the compact choice density by default', () => {
    const approvalDecisionComposerSource = readSource(approvalDecisionComposerPath)
    const composerChoiceRowSource = readSource(composerChoiceRowPath)

    expect(approvalDecisionComposerSource).toContain("fontSize: 'var(--text-body-size)'")
    expect(approvalDecisionComposerSource).toContain("lineHeight: 'var(--text-body-line-height)'")
    expect(composerChoiceRowSource).toContain("fontSize: 'var(--text-body-size)'")
    expect(composerChoiceRowSource).toContain("lineHeight: 'var(--text-body-line-height)'")
    expect(approvalDecisionComposerSource).toContain("const questionStyle: CSSProperties")
    expect(approvalDecisionComposerSource).toContain('function detailValueStyle')
    expect(approvalDecisionComposerSource).not.toContain('density="decision"')
  })

  it('keeps shared composer choices highlighted on hover, focus, and selection', () => {
    const requestUserInputComposerSource = readSource(requestUserInputComposerPath)
    const composerChoiceRowSource = readSource(composerChoiceRowPath)

    expect(composerChoiceRowSource).toContain('const highlighted = !disabled && (hovered || focused)')
    expect(composerChoiceRowSource).toContain('composerChoiceRowStyle(selected, disabled, highlighted, density)')
    expect(composerChoiceRowSource).toContain('function composerChoiceRowBackground')
    expect(composerChoiceRowSource).toContain('if (selected && highlighted)')
    expect(composerChoiceRowSource).toContain("if (selected) return 'color-mix(in srgb, var(--bg-tertiary) 82%, var(--text-primary) 8%)'")
    expect(composerChoiceRowSource).toContain("if (highlighted) return 'color-mix(in srgb, var(--bg-tertiary) 62%, transparent)'")
    expect(composerChoiceRowSource).toContain("return '1px solid color-mix(in srgb, var(--text-primary) 10%, transparent)'")
    expect(composerChoiceRowSource).toContain("return '1px solid transparent'")
    expect(composerChoiceRowSource).toContain("transition: 'background 120ms ease, border-color 120ms ease, box-shadow 120ms ease'")
    expect(requestUserInputComposerSource).toContain("composerChoiceRowStyle(selected, false, highlighted, 'decision')")
    expect(composerChoiceRowSource).not.toContain("background: selected ? 'var(--bg-tertiary)' : 'transparent'")
  })

  it('trims trailing markdown block margin', () => {
    const tokensCss = readSource(tokensCssPath)

    expect(tokensCss).toContain('.markdown-body > :last-child')
    expect(tokensCss).toContain('margin-bottom: 0 !important')
  })

  it('runs tool text shimmer in the intended direction at a calmer speed', () => {
    const tokensCss = readSource(tokensCssPath)

    expect(tokensCss).toContain('background-position: 240px 50%')
    expect(tokensCss).toContain('animation: tool-running-gradient 4.8s linear infinite')
    expect(tokensCss).not.toContain('background-position: -240px 50%')
    expect(tokensCss).not.toContain('animation: tool-running-gradient 1.8s linear infinite')
  })

  it('uses a seamless fixed-period automation tab shimmer', () => {
    const tokensCss = readSource(tokensCssPath)

    expect(tokensCss).toContain('@keyframes dotcraft-automation-tab-flow')
    expect(tokensCss).toContain('background-position: 96px 50%')
    expect(tokensCss).toContain('.dotcraft-automation-viewer-tab')
    expect(tokensCss).toContain('animation: none !important')
    expect(tokensCss).not.toContain('background-position: 220% 50%')
  })
})
