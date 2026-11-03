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
  chooseRepository: ['Choose a repository', '选择仓库', 'リポジトリを選択', '저장소 선택', 'Elige un repositorio', 'Choisir un dépôt', 'Repository auswählen'],
  loadingRepositories: ['Loading repositories…', '正在加载仓库…', 'リポジトリを読み込み中…', '저장소를 불러오는 중…', 'Cargando repositorios…', 'Chargement des dépôts…', 'Repositories werden geladen…'],
  noRepositories: ['No repository has a workspace yet', '还没有配置了工作区的仓库', 'ワークスペースのあるリポジトリがありません', '작업 공간이 연결된 저장소가 없습니다', 'Ningún repositorio tiene workspace', 'Aucun dépôt n’a de workspace', 'Kein Repository hat einen Workspace'],
  repositoriesFailed: ['Couldn’t load repositories', '无法加载仓库', 'リポジトリを読み込めません', '저장소를 불러올 수 없습니다', 'No se pudieron cargar los repositorios', 'Impossible de charger les dépôts', 'Repositories konnten nicht geladen werden'],
  openSettings: ['Open settings', '打开设置', '設定を開く', '설정 열기', 'Abrir ajustes', 'Ouvrir les réglages', 'Einstellungen öffnen'],
  details: ['Details', '详情', '詳細', '세부 정보', 'Detalles', 'Détails', 'Details'],
  advanced: ['Advanced', '高级', '詳細設定', '고급', 'Avanzado', 'Avancé', 'Erweitert'],
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
