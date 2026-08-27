import { useMemo } from 'react'
import { oratorioHost } from './runtime'

const locales = ['en', 'zh-Hans', 'ja', 'ko', 'es', 'fr', 'de'] as const
type Locale = (typeof locales)[number]
type Row = readonly [string, string, string, string, string, string, string]

const rows = {
  newTask: ['New local task', '新建本地任务', '新しいローカルタスク', '새 로컬 작업', 'Nueva tarea local', 'Nouvelle tâche locale', 'Neue lokale Aufgabe'],
  close: ['Close', '关闭', '閉じる', '닫기', 'Cerrar', 'Fermer', 'Schließen'],
  title: ['Title', '标题', 'タイトル', '제목', 'Título', 'Titre', 'Titel'],
  titlePlaceholder: ['What needs to be done?', '需要完成什么？', '何をする必要がありますか？', '무엇을 해야 하나요?', '¿Qué hay que hacer?', 'Que faut-il faire ?', 'Was muss erledigt werden?'],
  description: ['Description', '描述', '説明', '설명', 'Descripción', 'Description', 'Beschreibung'],
  descriptionPlaceholder: ['Add enough context to triage this task.', '添加足够的信息以便分派此任务。', 'このタスクを整理できるだけの情報を追加します。', '이 작업을 분류할 수 있도록 충분한 맥락을 추가하세요.', 'Añade contexto suficiente para clasificar esta tarea.', 'Ajoutez assez de contexte pour qualifier cette tâche.', 'Fügen Sie genug Kontext zur Einordnung der Aufgabe hinzu.'],
  repository: ['Repository', '仓库', 'リポジトリ', '저장소', 'Repositorio', 'Dépôt', 'Repository'],
  noRepository: ['No repository · local only', '无仓库 · 仅本地', 'リポジトリなし · ローカルのみ', '저장소 없음 · 로컬 전용', 'Sin repositorio · solo local', 'Aucun dépôt · local uniquement', 'Kein Repository · nur lokal'],
  labels: ['Labels', '标签', 'ラベル', '라벨', 'Etiquetas', 'Libellés', 'Labels'],
  suggestedLabels: ['Suggested', '建议', '候補', '추천', 'Sugeridas', 'Suggestions', 'Vorschläge'],
  addLabel: ['Add label', '添加标签', 'ラベルを追加', '라벨 추가', 'Añadir etiqueta', 'Ajouter un libellé', 'Label hinzufügen'],
  labelPlaceholder: ['Label name', '标签名称', 'ラベル名', '라벨 이름', 'Nombre de etiqueta', 'Nom du libellé', 'Labelname'],
  assignee: ['Assignee', '负责人', '担当者', '담당자', 'Responsable', 'Responsable', 'Zuständig'],
  optionalAssignee: ['Optional assignee', '可选负责人', '任意の担当者', '선택적 담당자', 'Responsable opcional', 'Responsable facultatif', 'Optional zuständig'],
  baseBranch: ['Base branch', '基础分支', 'ベースブランチ', '기본 브랜치', 'Rama base', 'Branche de base', 'Basis-Branch'],
  repositoryDefault: ['Repository default', '仓库默认分支', 'リポジトリのデフォルト', '저장소 기본값', 'Predeterminada del repositorio', 'Branche par défaut du dépôt', 'Repository-Standard'],
  cancel: ['Cancel', '取消', 'キャンセル', '취소', 'Cancelar', 'Annuler', 'Abbrechen'],
  createTask: ['Create task', '创建任务', 'タスクを作成', '작업 만들기', 'Crear tarea', 'Créer la tâche', 'Aufgabe erstellen'],
  noDescription: ['No description provided.', '未提供描述。', '説明はありません。', '설명이 없습니다.', 'Sin descripción.', 'Aucune description.', 'Keine Beschreibung angegeben.'],
  removeLabel: ['Remove label', '移除标签', 'ラベルを削除', '라벨 제거', 'Quitar etiqueta', 'Supprimer le libellé', 'Label entfernen'],
} as const satisfies Record<string, Row>

export type OratorioLocalTaskMessageKey = keyof typeof rows

export function useOratorioLocalTaskT(): (key: OratorioLocalTaskMessageKey) => string {
  const locale = oratorioHost().environment.locale as Locale
  return useMemo(() => {
    const index = Math.max(0, locales.indexOf(locale))
    return (key) => rows[key][index]
  }, [locale])
}
