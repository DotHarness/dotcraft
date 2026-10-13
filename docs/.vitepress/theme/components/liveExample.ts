import { withBase } from 'vitepress'

export type LiveFeature = 'generative-ui' | 'visualization' | 'agent-builder' | 'computer-use' | 'phone' | 'desktop'
export type LiveLang = 'en' | 'zh'

interface LivePage {
  path: string
  /** The dev server answers unknown paths with the site shell, so the page title confirms the build exists. */
  title: string
}

export interface FixedSize {
  width: number
  height: number
  minZoom: number
}

export interface LiveFeatureSpec {
  page: LivePage
  /** Fixed-size features render at their design size and zoom to fit; others report their content height. */
  fixed?: FixedSize
  exampleParam?: string
  reloadOnSelect?: boolean
  /** `phone` draws a phone bezel; `none` leaves the edge to the page, which draws its own screen. */
  frame?: 'phone' | 'none'
  backdrop?: boolean
}

const SHOWCASE: LivePage = { path: '/demo/showcase.html', title: '<title>DotCraft Live Example</title>' }
const PHONE: LivePage = { path: '/phone-demo/index.html', title: '<title>DotCraft Phone Demo</title>' }
const DESKTOP: LivePage = { path: '/demo/index.html', title: '<title>DotCraft Desktop Demo</title>' }

export const LIVE_FEATURES: Record<LiveFeature, LiveFeatureSpec> = {
  'generative-ui': { page: SHOWCASE },
  visualization: { page: SHOWCASE },
  'agent-builder': { page: SHOWCASE, fixed: { width: 1000, height: 640, minZoom: 0.55 } },
  'computer-use': { page: SHOWCASE, frame: 'none' },
  phone: {
    page: PHONE,
    fixed: { width: 390, height: 760, minZoom: 0.6 },
    exampleParam: 'demo',
    reloadOnSelect: true,
    frame: 'phone',
    backdrop: true
  },
  desktop: {
    page: DESKTOP,
    // Desktop shows its detail panel only from 1250px wide, so the window keeps the homepage width.
    fixed: { width: 1280, height: 800, minZoom: 0.25 },
    exampleParam: 'start',
    reloadOnSelect: true
  }
}

export const LIVE_COPY = {
  en: {
    examples: 'Examples',
    answerStyle: 'Answer style',
    plain: 'Text answer',
    interactive: 'Interactive answer',
    loading: 'Loading the live example',
    unavailable: 'This live example couldn’t load.',
    frame: 'Live example: {label}',
    builder: 'Agent Builder',
    play: 'Play',
    pause: 'Pause',
    replay: 'Replay',
    progress: 'Playback progress'
  },
  zh: {
    examples: '示例',
    answerStyle: '回答形式',
    plain: '文字回答',
    interactive: '交互式回答',
    loading: '正在加载实时示例',
    unavailable: '实时示例加载失败。',
    frame: '实时示例：{label}',
    builder: 'Agent Builder',
    play: '播放',
    pause: '暂停',
    replay: '重播',
    progress: '播放进度'
  }
}

export const EXAMPLE_LABELS: Record<string, Record<LiveLang, string>> = {
  dinner: { en: 'Dinner for friends', zh: '朋友聚餐' },
  plans: { en: 'Phone plans', zh: '手机套餐' },
  trip: { en: 'Trip budget', zh: '旅行预算' },
  'bill-splitter': { en: 'Bill splitter', zh: '分摊账单' },
  mortgage: { en: 'Loan calculator', zh: '贷款计算器' },
  'unit-converter': { en: 'Unit converter', zh: '单位换算' },
  quiz: { en: 'Quiz', zh: '小测验' },
  energy: { en: 'Interactive chart', zh: '交互图表' },
  moon: { en: 'Explainer', zh: '原理演示' },
  timer: { en: 'Design mockups', zh: '设计稿' },
  'review-changes': { en: 'Review changes', zh: '审阅改动' },
  'several-projects': { en: 'Several projects', zh: '多个项目' },
  'token-activity': { en: 'Token activity', zh: 'Token 活动' },
  home: { en: 'Your chats', zh: '你的聊天' },
  unpaired: { en: 'Pair a phone', zh: '配对手机' },
  'cu-sheet': { en: 'Tidy a spreadsheet', zh: '整理表格' },
  'cu-form': { en: 'Fill in a form', zh: '填写表单' },
  'cu-mail': { en: 'Confirm before sending', zh: '发送前确认' }
}

const found = new Map<string, Promise<string | null>>()

export function findPage(page: LivePage): Promise<string | null> {
  let result = found.get(page.path)
  if (!result) {
    const url = withBase(page.path)
    result = fetch(url)
      .then(async (response) => (response.ok && (await response.text()).includes(page.title) ? url : null))
      .catch(() => null)
    found.set(page.path, result)
  }
  return result
}
