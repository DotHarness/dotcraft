import { withBase } from 'vitepress'

export type LiveFeature = 'generative-ui' | 'visualization' | 'agent-builder'
export type LiveLang = 'en' | 'zh'

export const BUILDER_WIDTH = 1000
export const BUILDER_HEIGHT = 640
export const BUILDER_MIN_ZOOM = 0.55

export const LIVE_COPY = {
  en: {
    examples: 'Examples',
    answerStyle: 'Answer style',
    plain: 'Text answer',
    interactive: 'Interactive answer',
    loading: 'Loading the live example',
    unavailable: 'This live example couldn’t load.',
    frame: 'Live example: {label}',
    builder: 'Agent Builder'
  },
  zh: {
    examples: '示例',
    answerStyle: '回答形式',
    plain: '文字回答',
    interactive: '交互式回答',
    loading: '正在加载实时示例',
    unavailable: '实时示例加载失败。',
    frame: '实时示例：{label}',
    builder: 'Agent Builder'
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
  timer: { en: 'Design mockups', zh: '设计稿' }
}

const SHOWCASE_TITLE = '<title>DotCraft Live Example</title>'
let showcase: Promise<string | null> | null = null

/** The dev server answers unknown paths with the site shell, so the page title confirms the build exists. */
export function findShowcase(): Promise<string | null> {
  showcase ??= fetch(withBase('/demo/showcase.html'))
    .then(async (response) => (response.ok && (await response.text()).includes(SHOWCASE_TITLE) ? withBase('/demo/showcase.html') : null))
    .catch(() => null)
  return showcase
}
