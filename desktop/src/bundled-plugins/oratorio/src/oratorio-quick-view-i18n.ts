import { useMemo } from 'react'
import { oratorioHost } from './runtime'

const locales = ['en', 'zh-Hans', 'ja', 'ko', 'es', 'fr', 'de'] as const
type Locale = (typeof locales)[number]
type Row = readonly [string, string, string, string, string, string, string]

const rows = {
  localTask: ['Local task', '本地任务', 'ローカルタスク', '로컬 작업', 'Tarea local', 'Tâche locale', 'Lokale Aufgabe'],
  taskIdentity: ['Task identity', '任务标识', 'タスク識別情報', '작업 식별 정보', 'Identidad de la tarea', 'Identité de la tâche', 'Aufgabenidentität'],
  summary: ['Summary', '摘要', '概要', '요약', 'Resumen', 'Résumé', 'Zusammenfassung'],
  result: ['Result', '结果', '結果', '결과', 'Resultado', 'Résultat', 'Ergebnis'],
  noSummary: ['No summary', '暂无摘要', '概要はありません', '요약 없음', 'Sin resumen', 'Aucun résumé', 'Keine Zusammenfassung'],
  loadingResult: ['Loading result', '正在加载结果', '結果を読み込み中', '결과 불러오는 중', 'Cargando resultado', 'Chargement du résultat', 'Ergebnis wird geladen'],
  feedbackForDotCraft: ['Feedback for DotCraft', '给 DotCraft 的反馈', 'DotCraft へのフィードバック', 'DotCraft에 대한 피드백', 'Comentarios para DotCraft', 'Retour pour DotCraft', 'Feedback für DotCraft'],
  actionFailed: [
    'The managed service did not accept this action. Task state was not changed.',
    '托管服务未接受此操作，任务状态未发生变化。',
    '管理サービスがこの操作を受け付けなかったため、タスクの状態は変更されませんでした。',
    '관리형 서비스가 이 작업을 수락하지 않아 작업 상태가 변경되지 않았습니다.',
    'El servicio administrado no aceptó esta acción. El estado de la tarea no cambió.',
    'Le service géré n’a pas accepté cette action. L’état de la tâche n’a pas changé.',
    'Der verwaltete Dienst hat diese Aktion nicht angenommen. Der Aufgabenstatus wurde nicht geändert.',
  ],
} as const satisfies Record<string, Row>

export type OratorioQuickViewMessageKey = keyof typeof rows

export function useOratorioQuickViewT(): (key: OratorioQuickViewMessageKey) => string {
  const locale = oratorioHost().environment.locale as Locale
  return useMemo(() => {
    const index = Math.max(0, locales.indexOf(locale))
    return (key) => rows[key][index]
  }, [locale])
}
