import { demoLocale } from '../mockApi'
import type { Lang } from './content/types'

export type Feature = 'generative-ui' | 'visualization' | 'agent-builder' | 'computer-use'
export type AnswerMode = 'plain' | 'interactive'

export interface Selection {
  example: string
  mode: AnswerMode
  /** A named point in a scripted example to open at, used when a docs page walks through it step by step. */
  chapter?: string
}

const FEATURES: readonly Feature[] = ['generative-ui', 'visualization', 'agent-builder', 'computer-use']
const params = new URLSearchParams(window.location.search)
const requestedFeature = params.get('feature') as Feature | null

export const feature: Feature = requestedFeature && FEATURES.includes(requestedFeature) ? requestedFeature : 'generative-ui'
export const lang: Lang = demoLocale.startsWith('zh') ? 'zh' : 'en'
export const initialSelection: Selection = {
  example: params.get('example') ?? '',
  mode: params.get('mode') === 'plain' ? 'plain' : 'interactive',
  chapter: params.get('chapter') ?? undefined
}

export const SHOWCASE_THREAD_ID = 'showcase-thread'
export const SHOWCASE_TURN_ID = 'showcase-turn'
