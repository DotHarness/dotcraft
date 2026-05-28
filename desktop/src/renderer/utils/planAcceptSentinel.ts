import type { AppLocale } from '../../shared/locales'

export const ACCEPT_PLAN_SENTINEL_EN = '<Accept plan and execute immediately>'
export const ACCEPT_PLAN_SENTINEL_ZH = '<接受计划并立即执行>'

export function isAcceptPlanSentinel(text: string): boolean {
  return text === ACCEPT_PLAN_SENTINEL_EN || text === ACCEPT_PLAN_SENTINEL_ZH
}

export function acceptPlanSentinelFor(locale: AppLocale): string {
  return locale === 'zh-Hans' ? ACCEPT_PLAN_SENTINEL_ZH : ACCEPT_PLAN_SENTINEL_EN
}
