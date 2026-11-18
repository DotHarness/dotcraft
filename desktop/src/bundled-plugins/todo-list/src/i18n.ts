import type { DesktopPluginLocale } from '@dotcraft/plugin'

export interface TodoListStrings {
  readonly cardLabel: string
  readonly count: string
  readonly foldedDone: string
  readonly statusDone: string
  readonly statusActive: string
  readonly statusStopped: string
  readonly statusPending: string
  readonly statusCancelled: string
}

const CATALOG: Record<DesktopPluginLocale, TodoListStrings> = {
  en: {
    cardLabel: 'To-dos',
    count: '{done} of {total} done',
    foldedDone: '{count} completed',
    statusDone: 'done',
    statusActive: 'in progress',
    statusStopped: 'stopped',
    statusPending: 'not started',
    statusCancelled: 'cancelled'
  },
  'zh-Hans': {
    cardLabel: '待办',
    count: '已完成 {done}/{total}',
    foldedDone: '已完成 {count} 项',
    statusDone: '已完成',
    statusActive: '进行中',
    statusStopped: '已停止',
    statusPending: '未开始',
    statusCancelled: '已取消'
  },
  ja: {
    cardLabel: 'ToDo',
    count: '{total} 件中 {done} 件完了',
    foldedDone: '{count} 件完了',
    statusDone: '完了',
    statusActive: '進行中',
    statusStopped: '停止',
    statusPending: '未着手',
    statusCancelled: '取り消し'
  },
  ko: {
    cardLabel: '할 일',
    count: '{total}개 중 {done}개 완료',
    foldedDone: '{count}개 완료',
    statusDone: '완료',
    statusActive: '진행 중',
    statusStopped: '중지됨',
    statusPending: '시작 전',
    statusCancelled: '취소됨'
  },
  es: {
    cardLabel: 'Tareas',
    count: '{done} de {total} completadas',
    foldedDone: '{count} completadas',
    statusDone: 'completada',
    statusActive: 'en curso',
    statusStopped: 'detenida',
    statusPending: 'sin empezar',
    statusCancelled: 'cancelada'
  },
  fr: {
    cardLabel: 'Tâches',
    count: '{done} sur {total} terminées',
    foldedDone: '{count} terminées',
    statusDone: 'terminée',
    statusActive: 'en cours',
    statusStopped: 'arrêtée',
    statusPending: 'non commencée',
    statusCancelled: 'annulée'
  },
  de: {
    cardLabel: 'Aufgaben',
    count: '{done} von {total} erledigt',
    foldedDone: '{count} erledigt',
    statusDone: 'erledigt',
    statusActive: 'in Arbeit',
    statusStopped: 'angehalten',
    statusPending: 'nicht begonnen',
    statusCancelled: 'abgebrochen'
  }
}

export function stringsFor(locale: DesktopPluginLocale): TodoListStrings {
  return CATALOG[locale]
}

export function format(template: string, values: Record<string, number>): string {
  return template.replace(/\{(\w+)\}/g, (_, key: string) => String(values[key]))
}
