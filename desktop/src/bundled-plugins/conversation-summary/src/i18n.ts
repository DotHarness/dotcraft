export const SUMMARY_LOCALES = ['en', 'zh-Hans', 'ja', 'ko', 'es', 'fr', 'de'] as const

export type SummaryLocale = (typeof SUMMARY_LOCALES)[number]

export interface SummaryStrings {
  readonly panelLabel: string
  readonly toggle: string
  readonly togglePinned: string
  readonly toggleCommand: string
  readonly toggleDescription: string
  readonly empty: string
  readonly changes: string
  readonly subagents: string
  readonly scheduled: string
  readonly plan: string
  readonly sources: string
  readonly working: string
  readonly done: string
  readonly openSubagents: string
  readonly paused: string
  readonly planUntitled: string
  readonly viewAll: string
  readonly showLess: string
}

const CATALOG: Record<SummaryLocale, SummaryStrings> = {
  en: {
    panelLabel: 'Summary',
    toggle: 'Toggle summary',
    togglePinned: 'Toggle pinned summary',
    toggleCommand: 'Toggle summary',
    toggleDescription: 'Shows or hides the summary beside the conversation.',
    empty: 'Nothing to summarize yet',
    changes: 'Changes',
    subagents: 'Subagents',
    scheduled: 'Scheduled tasks',
    plan: 'Plan',
    sources: 'Sources',
    working: '{count} working',
    done: '{count} done',
    openSubagents: 'Open subagents',
    paused: 'Paused',
    planUntitled: 'Untitled plan',
    viewAll: 'View all',
    showLess: 'Show less'
  },
  'zh-Hans': {
    panelLabel: '摘要',
    toggle: '切换摘要',
    togglePinned: '切换固定摘要',
    toggleCommand: '切换摘要',
    toggleDescription: '显示或隐藏对话旁的摘要。',
    empty: '暂时没有可摘要的内容',
    changes: '更改',
    subagents: '子智能体',
    scheduled: '定时任务',
    plan: '计划',
    sources: '来源',
    working: '{count} 个进行中',
    done: '{count} 个已完成',
    openSubagents: '打开子智能体',
    paused: '已暂停',
    planUntitled: '未命名计划',
    viewAll: '查看全部',
    showLess: '收起'
  },
  ja: {
    panelLabel: 'サマリー',
    toggle: 'サマリーを切り替え',
    togglePinned: 'サマリーの固定を切り替え',
    toggleCommand: 'サマリーを切り替え',
    toggleDescription: '会話の横にサマリーを表示または非表示にします。',
    empty: 'まだまとめる内容がありません',
    changes: '変更点',
    subagents: 'サブエージェント',
    scheduled: 'スケジュール済みタスク',
    plan: 'プラン',
    sources: 'ソース',
    working: '{count} 件作業中',
    done: '{count} 件完了',
    openSubagents: 'サブエージェントを開く',
    paused: '一時停止中',
    planUntitled: '無題のプラン',
    viewAll: 'すべて表示',
    showLess: '表示を減らす'
  },
  ko: {
    panelLabel: '요약',
    toggle: '요약 전환',
    togglePinned: '고정 요약 전환',
    toggleCommand: '요약 전환',
    toggleDescription: '대화 옆의 요약을 표시하거나 숨깁니다.',
    empty: '아직 요약할 내용이 없습니다',
    changes: '변경 사항',
    subagents: '하위 에이전트',
    scheduled: '예약된 작업',
    plan: '계획',
    sources: '출처',
    working: '{count}개 작업 중',
    done: '{count}개 완료',
    openSubagents: '하위 에이전트 열기',
    paused: '일시 중지됨',
    planUntitled: '제목 없는 계획',
    viewAll: '모두 보기',
    showLess: '간단히 보기'
  },
  es: {
    panelLabel: 'Resumen',
    toggle: 'Mostrar u ocultar el resumen',
    togglePinned: 'Fijar o soltar el resumen',
    toggleCommand: 'Mostrar u ocultar el resumen',
    toggleDescription: 'Muestra u oculta el resumen junto a la conversación.',
    empty: 'Aún no hay nada que resumir',
    changes: 'Cambios',
    subagents: 'Subagentes',
    scheduled: 'Tareas programadas',
    plan: 'Plan',
    sources: 'Fuentes',
    working: '{count} trabajando',
    done: '{count} terminados',
    openSubagents: 'Abrir subagentes',
    paused: 'En pausa',
    planUntitled: 'Plan sin título',
    viewAll: 'Ver todo',
    showLess: 'Mostrar menos'
  },
  fr: {
    panelLabel: 'Résumé',
    toggle: 'Afficher ou masquer le résumé',
    togglePinned: 'Épingler ou détacher le résumé',
    toggleCommand: 'Afficher ou masquer le résumé',
    toggleDescription: 'Affiche ou masque le résumé à côté de la conversation.',
    empty: 'Rien à résumer pour l’instant',
    changes: 'Changements',
    subagents: 'Sous-agents',
    scheduled: 'Tâches planifiées',
    plan: 'Plan',
    sources: 'Sources',
    working: '{count} en cours',
    done: '{count} terminés',
    openSubagents: 'Ouvrir les sous-agents',
    paused: 'En pause',
    planUntitled: 'Plan sans titre',
    viewAll: 'Tout afficher',
    showLess: 'Afficher moins'
  },
  de: {
    panelLabel: 'Übersicht',
    toggle: 'Übersicht ein- oder ausblenden',
    togglePinned: 'Übersicht anheften oder lösen',
    toggleCommand: 'Übersicht ein- oder ausblenden',
    toggleDescription: 'Blendet die Übersicht neben der Unterhaltung ein oder aus.',
    empty: 'Noch nichts zusammenzufassen',
    changes: 'Änderungen',
    subagents: 'Subagenten',
    scheduled: 'Geplante Aufgaben',
    plan: 'Plan',
    sources: 'Quellen',
    working: '{count} in Arbeit',
    done: '{count} fertig',
    openSubagents: 'Subagenten öffnen',
    paused: 'Pausiert',
    planUntitled: 'Unbenannter Plan',
    viewAll: 'Alle anzeigen',
    showLess: 'Weniger anzeigen'
  }
}

export function stringsFor(locale: string): SummaryStrings {
  const exact = CATALOG[locale as SummaryLocale]
  if (exact !== undefined) return exact
  const base = locale.split('-')[0]
  const match = SUMMARY_LOCALES.find((candidate) => candidate.split('-')[0] === base)
  return match !== undefined ? CATALOG[match] : CATALOG.en
}

export function translationsOf(key: keyof SummaryStrings): Record<string, string> {
  return Object.fromEntries(SUMMARY_LOCALES.map((locale) => [locale, CATALOG[locale][key]]))
}

export function fill(template: string, values: Record<string, string | number>): string {
  return template.replace(/\{(\w+)\}/g, (match, key: string) => (key in values ? String(values[key]) : match))
}
