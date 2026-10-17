import { useMemo } from 'react'
import { oratorioHost } from './runtime'

const locales = ['en', 'zh-Hans', 'ja', 'ko', 'es', 'fr', 'de'] as const
type Locale = (typeof locales)[number]
type Row = readonly [string, string, string, string, string, string, string]

const rows = {
  boardLabel: ['Oratorio task board', 'Oratorio 任务看板', 'Oratorio タスクボード', 'Oratorio 작업 보드', 'Tablero de tareas de Oratorio', 'Tableau des tâches Oratorio', 'Oratorio-Aufgabenboard'],
  boardControls: ['Board controls', '看板控件', 'ボードの操作', '보드 컨트롤', 'Controles del tablero', 'Commandes du tableau', 'Board-Steuerung'],
  taskView: ['Task view', '任务视图', 'タスク表示', '작업 보기', 'Vista de tareas', 'Vue des tâches', 'Aufgabenansicht'],
  modeActive: ['Active', '活跃', 'アクティブ', '활성', 'Activas', 'Actives', 'Aktiv'],
  modeAll: ['All', '全部', 'すべて', '전체', 'Todas', 'Toutes', 'Alle'],
  modeCancelled: ['Cancelled', '已取消', 'キャンセル済み', '취소됨', 'Canceladas', 'Annulées', 'Abgebrochen'],
  modeArchived: ['Archived', '已归档', 'アーカイブ済み', '보관됨', 'Archivadas', 'Archivées', 'Archiviert'],
  searchTasks: ['Search tasks', '搜索任务', 'タスクを検索', '작업 검색', 'Buscar tareas', 'Rechercher des tâches', 'Aufgaben suchen'],
  searchPlaceholder: ['Search tasks · source:github · label:frontend', '搜索任务 · source:github · label:frontend', 'タスクを検索 · source:github · label:frontend', '작업 검색 · source:github · label:frontend', 'Buscar tareas · source:github · label:frontend', 'Rechercher des tâches · source:github · label:frontend', 'Aufgaben suchen · source:github · label:frontend'],
  repository: ['Repository', '仓库', 'リポジトリ', '저장소', 'Repositorio', 'Dépôt', 'Repository'],
  allRepositories: ['All repositories', '全部仓库', 'すべてのリポジトリ', '모든 저장소', 'Todos los repositorios', 'Tous les dépôts', 'Alle Repositories'],
  assignee: ['Assignee', '负责人', '担当者', '담당자', 'Responsable', 'Responsable', 'Zuständig'],
  allAssignees: ['All assignees', '全部负责人', 'すべての担当者', '모든 담당자', 'Todos los responsables', 'Tous les responsables', 'Alle Zuständigen'],
  unassigned: ['Unassigned', '未分配', '未割り当て', '미배정', 'Sin asignar', 'Non attribuée', 'Nicht zugewiesen'],
  newLocalTask: ['New local task', '新建本地任务', '新しいローカルタスク', '새 로컬 작업', 'Nueva tarea local', 'Nouvelle tâche locale', 'Neue lokale Aufgabe'],
  syncSources: ['Sync sources', '同步来源', 'ソースを同期', '소스 동기화', 'Sincronizar fuentes', 'Synchroniser les sources', 'Quellen synchronisieren'],
  oratorioSettings: ['Oratorio settings', 'Oratorio 设置', 'Oratorio の設定', 'Oratorio 설정', 'Ajustes de Oratorio', 'Réglages Oratorio', 'Oratorio-Einstellungen'],
  laneTodo: ['To do', '待办', '未着手', '할 일', 'Por hacer', 'À faire', 'Zu erledigen'],
  laneInProgress: ['In progress', '进行中', '進行中', '진행 중', 'En curso', 'En cours', 'In Arbeit'],
  laneInReview: ['In review', '待审阅', 'レビュー中', '검토 중', 'En revisión', 'En revue', 'In Prüfung'],
  laneDone: ['Done', '已完成', '完了', '완료', 'Hecho', 'Terminé', 'Erledigt'],
  noTasks: ['No tasks', '没有任务', 'タスクはありません', '작업 없음', 'Sin tareas', 'Aucune tâche', 'Keine Aufgaben'],
  noMatchingTasks: ['No matching tasks', '没有匹配的任务', '一致するタスクはありません', '일치하는 작업 없음', 'Ninguna tarea coincide', 'Aucune tâche correspondante', 'Keine passenden Aufgaben'],
  loadMore: ['Load more', '加载更多', 'さらに読み込む', '더 불러오기', 'Cargar más', 'Charger plus', 'Mehr laden'],
  loadingBoard: ['Loading board', '正在加载看板', 'ボードを読み込み中', '보드 불러오는 중', 'Cargando tablero', 'Chargement du tableau', 'Board wird geladen'],
  sourcesUpdated: ['Sources updated', '来源已更新', 'ソースを更新しました', '소스를 업데이트했습니다', 'Fuentes actualizadas', 'Sources mises à jour', 'Quellen aktualisiert'],
  sourceSyncFailed: ['Source sync failed', '来源同步失败', 'ソースの同期に失敗しました', '소스 동기화에 실패했습니다', 'No se pudieron sincronizar las fuentes', 'La synchronisation des sources a échoué', 'Quellensynchronisierung fehlgeschlagen'],
  orderNotSaved: ['Task order could not be saved', '无法保存任务顺序', 'タスクの順序を保存できませんでした', '작업 순서를 저장하지 못했습니다', 'No se pudo guardar el orden de las tareas', 'Impossible d’enregistrer l’ordre des tâches', 'Aufgabenreihenfolge konnte nicht gespeichert werden'],
  cannotMove: ['{task} can’t move to {lane}', '{task} 不能移到“{lane}”', '{task} は「{lane}」に移動できません', '{task}을(를) {lane}(으)로 옮길 수 없습니다', '{task} no se puede mover a {lane}', '{task} ne peut pas passer à {lane}', '{task} kann nicht nach „{lane}“ verschoben werden'],
  cancelRunMessage: ['Cancel the active run for {task}?', '取消 {task} 正在进行的运行？', '{task} の実行中の処理をキャンセルしますか？', '{task}의 진행 중인 실행을 취소할까요?', '¿Cancelar la ejecución activa de {task}?', 'Annuler l’exécution en cours de {task} ?', 'Laufende Ausführung für {task} abbrechen?'],
  taskMovedTo: ['Task moved to {lane}', '任务已移到“{lane}”', 'タスクを「{lane}」に移動しました', '작업을 {lane}(으)로 옮겼습니다', 'Tarea movida a {lane}', 'Tâche déplacée vers {lane}', 'Aufgabe nach „{lane}“ verschoben'],
  actionNotCompleted: ['Task action could not be completed', '无法完成任务操作', 'タスクの操作を完了できませんでした', '작업 동작을 완료하지 못했습니다', 'No se pudo completar la acción', 'Impossible de terminer l’action', 'Aufgabenaktion konnte nicht abgeschlossen werden'],
  localTaskCreated: ['Local task created', '已创建本地任务', 'ローカルタスクを作成しました', '로컬 작업을 만들었습니다', 'Tarea local creada', 'Tâche locale créée', 'Lokale Aufgabe erstellt'],
  localTaskNotCreated: ['Local task could not be created', '无法创建本地任务', 'ローカルタスクを作成できませんでした', '로컬 작업을 만들지 못했습니다', 'No se pudo crear la tarea local', 'Impossible de créer la tâche locale', 'Lokale Aufgabe konnte nicht erstellt werden'],
  detailsNotLoaded: ['Task details could not be loaded', '无法加载任务详情', 'タスクの詳細を読み込めませんでした', '작업 세부 정보를 불러오지 못했습니다', 'No se pudieron cargar los detalles de la tarea', 'Impossible de charger les détails de la tâche', 'Aufgabendetails konnten nicht geladen werden'],
  boardErrorTitle: ['Oratorio couldn’t load this board', 'Oratorio 无法加载这个看板', 'Oratorio はこのボードを読み込めませんでした', 'Oratorio가 이 보드를 불러오지 못했습니다', 'Oratorio no pudo cargar este tablero', 'Oratorio n’a pas pu charger ce tableau', 'Oratorio konnte dieses Board nicht laden'],
  boardErrorDescription: ['The Oratorio server didn’t answer. Your filters stay as they are.', 'Oratorio 服务器没有响应。你的筛选条件会保持不变。', 'Oratorio サーバーが応答しませんでした。フィルターはそのまま保持されます。', 'Oratorio 서버가 응답하지 않았습니다. 필터는 그대로 유지됩니다.', 'El servidor de Oratorio no respondió. Tus filtros se mantienen.', 'Le serveur Oratorio n’a pas répondu. Vos filtres sont conservés.', 'Der Oratorio-Server hat nicht geantwortet. Ihre Filter bleiben erhalten.'],
  retry: ['Retry', '重试', '再試行', '다시 시도', 'Reintentar', 'Réessayer', 'Erneut versuchen'],
  stateTodo: ['To do', '待办', '未着手', '할 일', 'Por hacer', 'À faire', 'Zu erledigen'],
  stateQueued: ['Queued', '排队中', '待機中', '대기 중', 'En cola', 'En file d’attente', 'In Warteschlange'],
  stateRunning: ['Running', '运行中', '実行中', '실행 중', 'En ejecución', 'En cours d’exécution', 'Läuft'],
  stateAwaitingReview: ['Awaiting review', '等待审阅', 'レビュー待ち', '검토 대기', 'Pendiente de revisión', 'En attente de revue', 'Wartet auf Prüfung'],
  stateApproved: ['Approved', '已批准', '承認済み', '승인됨', 'Aprobada', 'Approuvée', 'Genehmigt'],
  stateRejected: ['Rejected', '已拒绝', '却下', '거부됨', 'Rechazada', 'Refusée', 'Abgelehnt'],
  stateFailed: ['Failed', '失败', '失敗', '실패', 'Fallida', 'Échec', 'Fehlgeschlagen'],
  stateArchived: ['Archived', '已归档', 'アーカイブ済み', '보관됨', 'Archivada', 'Archivée', 'Archiviert'],
  stateCancelled: ['Cancelled', '已取消', 'キャンセル済み', '취소됨', 'Cancelada', 'Annulée', 'Abgebrochen'],
  lifecycleMerged: ['Merged', '已合并', 'マージ済み', '병합됨', 'Fusionada', 'Fusionnée', 'Zusammengeführt'],
  lifecycleClosed: ['Closed', '已关闭', 'クローズ済み', '닫힘', 'Cerrada', 'Fermée', 'Geschlossen'],
  sourceLocal: ['Local', '本地', 'ローカル', '로컬', 'Local', 'Local', 'Lokal'],
  metaSynced: ['synced {time}', '{time}同步', '{time}に同期', '{time} 동기화', 'sincronizada {time}', 'synchronisée {time}', 'synchronisiert {time}'],
  metaUpdated: ['updated {time}', '{time}更新', '{time}に更新', '{time} 업데이트', 'actualizada {time}', 'mise à jour {time}', 'aktualisiert {time}'],
  kindIssue: ['Issue', 'Issue', 'Issue', '이슈', 'Issue', 'Issue', 'Issue'],
  kindMergeRequest: ['Merge request', 'Merge request', 'マージリクエスト', '머지 리퀘스트', 'Merge request', 'Merge request', 'Merge Request'],
  kindPullRequest: ['Pull request', 'Pull request', 'プルリクエスト', '풀 리퀘스트', 'Pull request', 'Pull request', 'Pull Request'],
  kindTask: ['Task', '任务', 'タスク', '작업', 'Tarea', 'Tâche', 'Aufgabe'],
  localTask: ['Local task', '本地任务', 'ローカルタスク', '로컬 작업', 'Tarea local', 'Tâche locale', 'Lokale Aufgabe'],
  fullDetails: ['Full details', '完整详情', '詳細をすべて表示', '전체 세부 정보', 'Detalles completos', 'Détails complets', 'Alle Details'],
  close: ['Close', '关闭', '閉じる', '닫기', 'Cerrar', 'Fermer', 'Schließen'],
  summary: ['Summary', '摘要', '概要', '요약', 'Resumen', 'Résumé', 'Zusammenfassung'],
  activityWriting: ['Writing', '正在撰写', '書いています', '작성 중', 'Escribiendo', 'Rédaction', 'Schreibt'],
  activityThinking: ['Thinking', '正在思考', '考えています', '생각 중', 'Pensando', 'Réflexion', 'Denkt nach'],
  activityRunningCommand: ['Running command', '正在运行命令', 'コマンドを実行中', '명령 실행 중', 'Ejecutando comando', 'Exécution d’une commande', 'Führt Befehl aus'],
  activityUsingTool: ['Using tool', '正在使用工具', 'ツールを使用中', '도구 사용 중', 'Usando herramienta', 'Utilisation d’un outil', 'Verwendet Werkzeug'],
  activityWorking: ['Working', '处理中', '処理中', '작업 중', 'Trabajando', 'En cours', 'Arbeitet'],
  actionImplement: ['Implement', '实现', '実装', '구현', 'Implementar', 'Implémenter', 'Umsetzen'],
  actionAutoTarget: ['Auto PR / MR', '自动 PR / MR', '自動 PR / MR', '자동 PR / MR', 'PR / MR automático', 'PR / MR automatique', 'Automatischer PR / MR'],
  actionReviewOnly: ['Review only', '仅审阅', 'レビューのみ', '검토만', 'Solo revisar', 'Revue uniquement', 'Nur prüfen'],
  actionOpenThread: ['Open thread', '打开线程', 'スレッドを開く', '스레드 열기', 'Abrir hilo', 'Ouvrir le fil', 'Thread öffnen'],
  actionCancelRun: ['Cancel run', '取消运行', '実行をキャンセル', '실행 취소', 'Cancelar ejecución', 'Annuler l’exécution', 'Ausführung abbrechen'],
  actionApprove: ['Approve', '批准', '承認', '승인', 'Aprobar', 'Approuver', 'Genehmigen'],
  actionRequestChanges: ['Request changes', '要求修改', '変更を依頼', '변경 요청', 'Pedir cambios', 'Demander des modifications', 'Änderungen anfordern'],
  actionReject: ['Reject', '拒绝', '却下', '거부', 'Rechazar', 'Refuser', 'Ablehnen'],
  actionReReview: ['Review new revision', '审阅新版本', '新しいリビジョンをレビュー', '새 리비전 검토', 'Revisar nueva revisión', 'Revoir la nouvelle révision', 'Neue Revision prüfen'],
  actionArchive: ['Archive', '归档', 'アーカイブ', '보관', 'Archivar', 'Archiver', 'Archivieren'],
  actionReopen: ['Reopen', '重新打开', '再オープン', '다시 열기', 'Reabrir', 'Rouvrir', 'Wieder öffnen'],
  toastApproved: ['Task approved', '任务已批准', 'タスクを承認しました', '작업을 승인했습니다', 'Tarea aprobada', 'Tâche approuvée', 'Aufgabe genehmigt'],
  toastRejected: ['Task rejected', '任务已拒绝', 'タスクを却下しました', '작업을 거부했습니다', 'Tarea rechazada', 'Tâche refusée', 'Aufgabe abgelehnt'],
  toastChangesRequested: ['Changes requested', '已要求修改', '変更を依頼しました', '변경을 요청했습니다', 'Cambios solicitados', 'Modifications demandées', 'Änderungen angefordert'],
  toastArchived: ['Task archived', '任务已归档', 'タスクをアーカイブしました', '작업을 보관했습니다', 'Tarea archivada', 'Tâche archivée', 'Aufgabe archiviert'],
  toastReopened: ['Task reopened', '任务已重新打开', 'タスクを再オープンしました', '작업을 다시 열었습니다', 'Tarea reabierta', 'Tâche rouverte', 'Aufgabe wieder geöffnet'],
  toastRunCancelled: ['Run cancelled', '运行已取消', '実行をキャンセルしました', '실행을 취소했습니다', 'Ejecución cancelada', 'Exécution annulée', 'Ausführung abgebrochen'],
  toastRetryQueued: ['Retry queued', '已加入重试队列', '再試行をキューに追加しました', '재시도를 대기열에 추가했습니다', 'Reintento en cola', 'Nouvelle tentative en file d’attente', 'Wiederholung eingereiht'],
  toastWorkQueued: ['Agent work queued', 'Agent 工作已加入队列', 'エージェントの作業をキューに追加しました', '에이전트 작업을 대기열에 추가했습니다', 'Trabajo del agente en cola', 'Travail de l’agent en file d’attente', 'Agentenarbeit eingereiht'],
  requestChangesFeedback: ['Request changes feedback', '修改要求反馈', '変更依頼のフィードバック', '변경 요청 피드백', 'Comentarios de la solicitud de cambios', 'Retour de demande de modifications', 'Feedback zur Änderungsanforderung'],
  feedbackPlaceholder: ['What should Oratorio change?', '希望 Oratorio 改什么？', 'Oratorio に何を変えてほしいですか？', 'Oratorio가 무엇을 바꾸면 좋을까요?', '¿Qué debería cambiar Oratorio?', 'Que doit changer Oratorio ?', 'Was soll Oratorio ändern?'],
  cancel: ['Cancel', '取消', 'キャンセル', '취소', 'Cancelar', 'Annuler', 'Abbrechen'],
  taskDetailsFor: ['Task details for {task}', '{task} 的任务详情', '{task} のタスク詳細', '{task} 작업 세부 정보', 'Detalles de la tarea {task}', 'Détails de la tâche {task}', 'Aufgabendetails für {task}'],
  loadingDetails: ['Loading task details…', '正在加载任务详情…', 'タスクの詳細を読み込み中…', '작업 세부 정보를 불러오는 중…', 'Cargando detalles de la tarea…', 'Chargement des détails de la tâche…', 'Aufgabendetails werden geladen…'],
  backToBoard: ['Back to board', '返回看板', 'ボードに戻る', '보드로 돌아가기', 'Volver al tablero', 'Retour au tableau', 'Zurück zum Board'],
  actionRejected: ['Oratorio rejected the action.', 'Oratorio 拒绝了此操作。', 'Oratorio がこの操作を拒否しました。', 'Oratorio가 이 작업을 거부했습니다.', 'Oratorio rechazó la acción.', 'Oratorio a refusé l’action.', 'Oratorio hat die Aktion abgelehnt.'],
  noDescription: ['No description provided.', '未提供描述。', '説明はありません。', '설명이 없습니다.', 'No hay descripción.', 'Aucune description fournie.', 'Keine Beschreibung vorhanden.'],
  factTests: ['Tests', '测试', 'テスト', '테스트', 'Pruebas', 'Tests', 'Tests'],
  refresh: ['Refresh', '刷新', '更新', '새로 고침', 'Actualizar', 'Actualiser', 'Aktualisieren'],
  purposeReview: ['Review', '评审', 'レビュー', '검토', 'Revisión', 'Revue', 'Review'],
  purposeImplementation: ['Implementation', '实现', '実装', '구현', 'Implementación', 'Implémentation', 'Implementierung'],
  runQueued: ['Queued', '排队中', '待機中', '대기 중', 'En cola', 'En file d’attente', 'In Warteschlange'],
  runRunning: ['Running', '运行中', '実行中', '실행 중', 'En ejecución', 'En cours', 'Läuft'],
  runSucceeded: ['Succeeded', '已完成', '完了', '완료', 'Completada', 'Réussie', 'Erfolgreich'],
  runCancelled: ['Cancelled', '已取消', 'キャンセル済み', '취소됨', 'Cancelada', 'Annulée', 'Abgebrochen'],
  runTimedOut: ['Timed out', '已超时', 'タイムアウト', '시간 초과', 'Tiempo agotado', 'Délai dépassé', 'Zeitüberschreitung'],
  draftPublished: ['Published', '已发布', '公開済み', '게시됨', 'Publicada', 'Publiée', 'Veröffentlicht'],
  draftPublishFailed: ['Publishing failed', '发布失败', '公開に失敗しました', '게시 실패', 'Error al publicar', 'Échec de la publication', 'Veröffentlichung fehlgeschlagen'],
  countMajor: ['{n} major', '{n} 个严重', '重大 {n} 件', '중대 {n}건', '{n} graves', '{n} majeurs', '{n} schwerwiegend'],
  countMinor: ['{n} minor', '{n} 个次要', '軽微 {n} 件', '경미 {n}건', '{n} menores', '{n} mineurs', '{n} geringfügig'],
  countSuggestion: ['{n} suggestion', '{n} 条建议', '提案 {n} 件', '제안 {n}건', '{n} sugerencia', '{n} suggestion', '{n} Vorschlag'],
  countSuggestions: ['{n} suggestions', '{n} 条建议', '提案 {n} 件', '제안 {n}건', '{n} sugerencias', '{n} suggestions', '{n} Vorschläge'],
  severityMajor: ['Major', '严重', '重大', '중대', 'Grave', 'Majeur', 'Schwerwiegend'],
  severityMinor: ['Minor', '次要', '軽微', '경미', 'Menor', 'Mineur', 'Geringfügig'],
  severitySuggestion: ['Suggestion', '建议', '提案', '제안', 'Sugerencia', 'Suggestion', 'Vorschlag'],
  resolutionFixed: ['Fixed', '已修复', '修正済み', '수정됨', 'Corregido', 'Corrigé', 'Behoben'],
  resolutionDismissed: ['Dismissed', '已忽略', '却下', '무시됨', 'Descartado', 'Ignoré', 'Verworfen'],
  discard: ['Discard', '丢弃', '破棄', '폐기', 'Descartar', 'Abandonner', 'Verwerfen'],
  save: ['Save', '保存', '保存', '저장', 'Guardar', 'Enregistrer', 'Speichern'],
  dismiss: ['Dismiss', '忽略', '却下', '무시', 'Descartar', 'Ignorer', 'Verwerfen'],
  markFixed: ['Mark fixed', '标记为已修复', '修正済みにする', '수정됨으로 표시', 'Marcar como corregido', 'Marquer comme corrigé', 'Als behoben markieren'],
  implementationDelivered: ['Delivered', '已交付', '納品済み', '전달됨', 'Entregada', 'Livrée', 'Ausgeliefert'],
  implementationFailed: ['Delivery failed', '交付失败', '納品に失敗しました', '전달 실패', 'Error en la entrega', 'Échec de la livraison', 'Auslieferung fehlgeschlagen'],
  openPullRequest: ['Open pull request', '打开 Pull request', 'プルリクエストを開く', '풀 리퀘스트 열기', 'Abrir pull request', 'Ouvrir la pull request', 'Pull Request öffnen'],
  deliver: ['Deliver', '交付', '納品', '전달', 'Entregar', 'Livrer', 'Ausliefern'],
  followUpCreated: ['Task created', '已创建任务', 'タスク作成済み', '작업 생성됨', 'Tarea creada', 'Tâche créée', 'Aufgabe erstellt'],
  followUpBody: ['Follow-up {title}', '后续任务：{title}', 'フォローアップ：{title}', '후속 작업: {title}', 'Seguimiento: {title}', 'Suite : {title}', 'Folgeaufgabe: {title}'],
  createLocalTask: ['Create local task', '创建本地任务', 'ローカルタスクを作成', '로컬 작업 만들기', 'Crear tarea local', 'Créer une tâche locale', 'Lokale Aufgabe erstellen'],
  oratorio: ['Oratorio', 'Oratorio', 'Oratorio', 'Oratorio', 'Oratorio', 'Oratorio', 'Oratorio'],
  you: ['You', '你', 'あなた', '나', 'Tú', 'Vous', 'Sie'],
  discussionPlaceholder: ['Add feedback or ask Oratorio…', '添加反馈或向 Oratorio 提问…', 'フィードバックを追加するか、Oratorio に質問…', '피드백을 남기거나 Oratorio에게 질문하세요…', 'Añade comentarios o pregunta a Oratorio…', 'Ajoutez un retour ou posez une question à Oratorio…', 'Feedback hinzufügen oder Oratorio fragen…'],
  discussionReply: ['Discussion reply', '讨论回复', 'ディスカッションへの返信', '토론 답글', 'Respuesta en la conversación', 'Réponse à la discussion', 'Antwort in der Diskussion'],
  addComment: ['Add comment', '添加评论', 'コメントを追加', '댓글 추가', 'Añadir comentario', 'Ajouter un commentaire', 'Kommentar hinzufügen'],
  askAgent: ['Ask Oratorio', '询问 Oratorio', 'Oratorio に質問', 'Oratorio에게 질문', 'Preguntar a Oratorio', 'Demander à Oratorio', 'Oratorio fragen'],
  open: ['Open', '打开', '開く', '열기', 'Abrir', 'Ouvrir', 'Öffnen'],
  toastSourceRefreshed: ['Source details refreshed', '来源信息已刷新', 'ソースの詳細を更新しました', '소스 세부 정보를 새로 고쳤습니다', 'Detalles de la fuente actualizados', 'Détails de la source actualisés', 'Quelldetails aktualisiert'],
  toastDraftDiscarded: ['Review draft discarded', '评审草稿已丢弃', 'レビューの下書きを破棄しました', '검토 초안을 폐기했습니다', 'Borrador de revisión descartado', 'Brouillon de revue abandonné', 'Review-Entwurf verworfen'],
  toastReviewPublished: ['Review published', '评审已发布', 'レビューを公開しました', '검토를 게시했습니다', 'Revisión publicada', 'Revue publiée', 'Review veröffentlicht'],
  toastFindingDismissed: ['Finding dismissed', '问题已忽略', '指摘を却下しました', '지적 사항을 무시했습니다', 'Hallazgo descartado', 'Constat ignoré', 'Befund verworfen'],
  toastFindingResolved: ['Finding marked fixed', '问题已标记为修复', '指摘を修正済みにしました', '지적 사항을 수정됨으로 표시했습니다', 'Hallazgo marcado como corregido', 'Constat marqué comme corrigé', 'Befund als behoben markiert'],
  toastFindingReopened: ['Finding reopened', '问题已重新打开', '指摘を再オープンしました', '지적 사항을 다시 열었습니다', 'Hallazgo reabierto', 'Constat rouvert', 'Befund wieder geöffnet'],
  toastDelivered: ['Implementation delivered', '实现已交付', '実装を納品しました', '구현을 전달했습니다', 'Implementación entregada', 'Implémentation livrée', 'Implementierung ausgeliefert'],
  toastFollowUpDiscarded: ['Follow-up discarded', '后续任务已丢弃', 'フォローアップを破棄しました', '후속 작업을 폐기했습니다', 'Seguimiento descartado', 'Suite abandonnée', 'Folgeaufgabe verworfen'],
  toastFollowUpSaved: ['Follow-up saved', '后续任务已保存', 'フォローアップを保存しました', '후속 작업을 저장했습니다', 'Seguimiento guardado', 'Suite enregistrée', 'Folgeaufgabe gespeichert'],
  toastCommentAdded: ['Comment added', '评论已添加', 'コメントを追加しました', '댓글을 추가했습니다', 'Comentario añadido', 'Commentaire ajouté', 'Kommentar hinzugefügt'],
  toastDiscussionStarted: ['Asked Oratorio', '已向 Oratorio 提问', 'Oratorio に質問しました', 'Oratorio에게 질문했습니다', 'Pregunta enviada a Oratorio', 'Question envoyée à Oratorio', 'Oratorio gefragt'],
  toastWriteRetried: ['Update to the source retried', '已重试写回来源', 'ソースへの反映を再試行しました', '소스 반영을 다시 시도했습니다', 'Cambio en la fuente reintentado', 'Mise à jour de la source relancée', 'Änderung an der Quelle wiederholt'],
  changeStatus: ['Change task status', '更改任务状态', 'タスクの状態を変更', '작업 상태 변경', 'Cambiar el estado de la tarea', 'Modifier l’état de la tâche', 'Aufgabenstatus ändern'],
  openOnProvider: ['Open on {provider}', '在 {provider} 中打开', '{provider} で開く', '{provider}에서 열기', 'Abrir en {provider}', 'Ouvrir sur {provider}', 'Auf {provider} öffnen'],
  moreStartOptions: ['More ways to start', '更多开始方式', 'その他の開始方法', '다른 시작 방법', 'Más formas de empezar', 'Autres façons de démarrer', 'Weitere Startoptionen'],
  actionReview: ['Review', '审阅', 'レビュー', '검토', 'Revisar', 'Examiner', 'Prüfen'],
  submitReview: ['Submit review', '提交审阅', 'レビューを送信', '검토 제출', 'Enviar revisión', 'Envoyer l’examen', 'Prüfung absenden'],
  optionalComment: ['Optional comment', '可选评论', 'コメント（任意）', '댓글(선택)', 'Comentario opcional', 'Commentaire facultatif', 'Optionaler Kommentar'],
  sectionActivity: ['Activity', '动态', 'アクティビティ', '활동', 'Actividad', 'Activité', 'Aktivität'],
  sectionChanges: ['Changes', '更改', '変更', '변경 사항', 'Cambios', 'Modifications', 'Änderungen'],
  sectionLabels: ['Labels', '标签', 'ラベル', '레이블', 'Etiquetas', 'Libellés', 'Labels'],
  sectionSource: ['Source', '来源', 'ソース', '소스', 'Origen', 'Source', 'Quelle'],
  reviewDraftTitle: ['Oratorio’s review', 'Oratorio 的评审', 'Oratorio のレビュー', 'Oratorio의 검토', 'Revisión de Oratorio', 'Examen d’Oratorio', 'Prüfung von Oratorio'],
  followUpLabel: ['Follow-up', '后续任务', 'フォローアップ', '후속 작업', 'Seguimiento', 'Suivi', 'Folgeaufgabe'],
  eventApproved: ['{author} approved these changes', '{author} 批准了这些更改', '{author} がこの変更を承認しました', '{author} 님이 이 변경 사항을 승인했습니다', '{author} aprobó estos cambios', '{author} a approuvé ces modifications', '{author} hat diese Änderungen genehmigt'],
  eventRequestedChanges: ['{author} requested changes', '{author} 要求修改', '{author} が変更を依頼しました', '{author} 님이 변경을 요청했습니다', '{author} solicitó cambios', '{author} a demandé des modifications', '{author} hat Änderungen angefordert'],
  eventRejected: ['{author} rejected this task', '{author} 拒绝了此任务', '{author} がこのタスクを却下しました', '{author} 님이 이 작업을 거부했습니다', '{author} rechazó esta tarea', '{author} a rejeté cette tâche', '{author} hat diese Aufgabe abgelehnt'],
  eventReopened: ['{author} reopened this task', '{author} 重新打开了此任务', '{author} がこのタスクを再開しました', '{author} 님이 이 작업을 다시 열었습니다', '{author} reabrió esta tarea', '{author} a rouvert cette tâche', '{author} hat diese Aufgabe wieder geöffnet'],
  eventReReview: ['{author} asked for a new review', '{author} 请求重新审阅', '{author} が再レビューを依頼しました', '{author} 님이 다시 검토를 요청했습니다', '{author} pidió una nueva revisión', '{author} a demandé un nouvel examen', '{author} hat eine neue Prüfung angefordert'],
  writeFailed: ['Couldn’t update {provider}', '未能写回 {provider}', '{provider} を更新できませんでした', '{provider}을(를) 업데이트하지 못했습니다', 'No se pudo actualizar {provider}', 'Impossible de mettre à jour {provider}', '{provider} konnte nicht aktualisiert werden'],
  oratorioReplying: ['Oratorio is replying…', 'Oratorio 正在回复…', 'Oratorio が返信しています…', 'Oratorio가 답변하는 중…', 'Oratorio está respondiendo…', 'Oratorio répond…', 'Oratorio antwortet…'],
  runNotStarted: ['Not started', '未开始', '未開始', '시작 안 함', 'Sin iniciar', 'Pas commencé', 'Nicht gestartet'],
  moreFiles: ['{n} more', '还有 {n} 个', '他 {n} 件', '외 {n}개', '{n} más', '{n} de plus', '{n} weitere'],
  resultNoIssues: ['No issues found', '没有发现问题', '問題は見つかりませんでした', '문제가 발견되지 않음', 'No se encontraron problemas', 'Aucun problème trouvé', 'Keine Probleme gefunden'],
  resultDelivered: ['Delivered', '已交付', '納品済み', '전달됨', 'Entregado', 'Livré', 'Ausgeliefert'],
  resultFindings: ['Review suggestions to look at', '有评审建议待查看', '確認が必要なレビューの提案', '확인할 검토 제안', 'Sugerencias de revisión por revisar', 'Suggestions d’examen à consulter', 'Prüfvorschläge ansehen'],
  resultChangesReady: ['Changes ready to look at', '改动待查看', '確認待ちの変更', '확인할 변경 사항', 'Cambios listos para revisar', 'Modifications prêtes à consulter', 'Änderungen bereit zur Ansicht'],
  resultFollowUps: ['Follow-ups waiting for you', '有后续任务待处理', '対応待ちのフォローアップ', '처리할 후속 작업', 'Seguimientos pendientes', 'Suivis en attente', 'Folgeaufgaben warten auf dich'],
} as const satisfies Record<string, Row>

export type OratorioMessageKey = keyof typeof rows
export type OratorioTranslate = (key: OratorioMessageKey, values?: Record<string, string | number>) => string

export interface OratorioI18n {
  t: OratorioTranslate
  relativeTime: (value: string) => string
}

export function useOratorioI18n(): OratorioI18n {
  const locale = oratorioHost().environment.locale as Locale
  return useMemo(() => {
    const index = Math.max(0, locales.indexOf(locale))
    const t: OratorioTranslate = (key, values) => {
      const text: string = rows[key][index]
      return values ? text.replace(/\{(\w+)\}/g, (match, name: string) => name in values ? String(values[name]) : match) : text
    }
    return { t, relativeTime: (value) => formatRelativeTime(value, locales[index], Date.now()) }
  }, [locale])
}

const RELATIVE_UNITS: Array<[Intl.RelativeTimeFormatUnit, number]> = [
  ['year', 31_536_000],
  ['month', 2_592_000],
  ['week', 604_800],
  ['day', 86_400],
  ['hour', 3_600],
  ['minute', 60],
]

export function formatRelativeTime(value: string, locale: string, now: number): string {
  const timestamp = Date.parse(value)
  if (!Number.isFinite(timestamp)) return ''
  const seconds = Math.round((timestamp - now) / 1000)
  const format = new Intl.RelativeTimeFormat(locale, { numeric: 'auto', style: 'short' })
  for (const [unit, size] of RELATIVE_UNITS) {
    if (Math.abs(seconds) >= size) return format.format(Math.round(seconds / size), unit)
  }
  return format.format(0, 'second')
}
