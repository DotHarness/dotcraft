import type { ConfigLocalizedText } from "@dotcraft/channel";

export const USER_SCOPE_DOMAIN_TEXT: Record<string, { label: ConfigLocalizedText; description: ConfigLocalizedText }> = {
  base: {
    label: { en: "Base", "zh-Hans": "多维表格", ja: "Base テーブル", ko: "Base 테이블", es: "Tablas de Base", fr: "Tables Base", de: "Base-Tabellen" },
    description: { en: "Read tables, records, views and dashboards", "zh-Hans": "读取数据表、记录、视图和仪表盘", ja: "テーブル、レコード、ビュー、ダッシュボードを読み取る", ko: "테이블, 레코드, 보기, 대시보드 읽기", es: "Leer tablas, registros, vistas y paneles", fr: "Lire les tables, enregistrements, vues et tableaux de bord", de: "Tabellen, Datensätze, Ansichten und Dashboards lesen" },
  },
  calendar: {
    label: { en: "Calendar", "zh-Hans": "日历", ja: "カレンダー", ko: "캘린더", es: "Calendario", fr: "Calendrier", de: "Kalender" },
    description: { en: "Read events and free/busy", "zh-Hans": "读取日程和忙闲", ja: "予定と空き状況を読み取る", ko: "일정과 바쁨/한가함 읽기", es: "Leer eventos y disponibilidad", fr: "Lire les événements et les disponibilités", de: "Termine und Verfügbarkeit lesen" },
  },
  contact: {
    label: { en: "Contacts", "zh-Hans": "通讯录", ja: "連絡先", ko: "연락처", es: "Contactos", fr: "Annuaire", de: "Kontakte" },
    description: { en: "Look up people and bots", "zh-Hans": "查找成员和机器人", ja: "メンバーとボットを検索する", ko: "구성원과 봇 찾기", es: "Buscar personas y bots", fr: "Rechercher des personnes et des bots", de: "Personen und Bots suchen" },
  },
  docs: {
    label: { en: "Docs", "zh-Hans": "云文档", ja: "ドキュメント", ko: "문서", es: "Documentos", fr: "Documents", de: "Dokumente" },
    description: { en: "Read and search documents", "zh-Hans": "读取和搜索文档", ja: "ドキュメントを読み取り、検索する", ko: "문서 읽기 및 검색", es: "Leer y buscar documentos", fr: "Lire et rechercher des documents", de: "Dokumente lesen und durchsuchen" },
  },
  drive: {
    label: { en: "Drive", "zh-Hans": "云空间", ja: "クラウドスペース", ko: "클라우드 공간", es: "Espacio en la nube", fr: "Espace cloud", de: "Ablage" },
    description: { en: "Read files, comments and permissions", "zh-Hans": "读取文件、评论和权限", ja: "ファイル、コメント、権限を読み取る", ko: "파일, 댓글, 권한 읽기", es: "Leer archivos, comentarios y permisos", fr: "Lire les fichiers, commentaires et autorisations", de: "Dateien, Kommentare und Berechtigungen lesen" },
  },
  im: {
    label: { en: "Messages and chats", "zh-Hans": "消息与群组", ja: "メッセージとチャット", ko: "메시지와 채팅", es: "Mensajes y chats", fr: "Messages et discussions", de: "Nachrichten und Chats" },
    description: { en: "Read chat history and topic replies, and search messages", "zh-Hans": "读取会话历史和话题回复，搜索消息", ja: "チャット履歴とトピックの返信を読み取り、メッセージを検索する", ko: "채팅 기록과 토픽 답글 읽기, 메시지 검색", es: "Leer historiales y respuestas de temas, y buscar mensajes", fr: "Lire l'historique et les réponses aux sujets, et rechercher des messages", de: "Chatverläufe und Themenantworten lesen, Nachrichten durchsuchen" },
  },
  mail: {
    label: { en: "Mail", "zh-Hans": "邮箱", ja: "メール", ko: "메일", es: "Correo", fr: "Messagerie", de: "E-Mail" },
    description: { en: "Read mailboxes and messages", "zh-Hans": "读取邮箱和邮件", ja: "メールボックスとメールを読み取る", ko: "메일함과 메일 읽기", es: "Leer buzones y mensajes", fr: "Lire les boîtes et les messages", de: "Postfächer und Nachrichten lesen" },
  },
  markdown: {
    label: { en: "Markdown files", "zh-Hans": "Markdown 文件", ja: "Markdown ファイル", ko: "Markdown 파일", es: "Archivos Markdown", fr: "Fichiers Markdown", de: "Markdown-Dateien" },
    description: { en: "Download Markdown files", "zh-Hans": "下载 Markdown 文件", ja: "Markdown ファイルをダウンロードする", ko: "Markdown 파일 다운로드", es: "Descargar archivos Markdown", fr: "Télécharger des fichiers Markdown", de: "Markdown-Dateien herunterladen" },
  },
  minutes: {
    label: { en: "Minutes", "zh-Hans": "妙记", ja: "議事録", ko: "회의록", es: "Actas", fr: "Comptes rendus", de: "Protokolle" },
    description: { en: "Read and search minutes", "zh-Hans": "读取和搜索妙记", ja: "議事録を読み取り、検索する", ko: "회의록 읽기 및 검색", es: "Leer y buscar actas", fr: "Lire et rechercher les comptes rendus", de: "Protokolle lesen und durchsuchen" },
  },
  note: {
    label: { en: "Meeting notes", "zh-Hans": "会议纪要", ja: "会議メモ", ko: "회의 메모", es: "Notas de reunión", fr: "Notes de réunion", de: "Besprechungsnotizen" },
    description: { en: "Read meeting notes", "zh-Hans": "读取会议纪要", ja: "会議メモを読み取る", ko: "회의 메모 읽기", es: "Leer notas de reunión", fr: "Lire les notes de réunion", de: "Besprechungsnotizen lesen" },
  },
  okr: {
    label: { en: "OKR", "zh-Hans": "OKR", ja: "OKR", ko: "OKR", es: "OKR", fr: "OKR", de: "OKR" },
    description: { en: "Read objectives, key results and progress", "zh-Hans": "读取目标、关键结果和进展", ja: "目標、主要な成果、進捗を読み取る", ko: "목표, 핵심 결과, 진행 상황 읽기", es: "Leer objetivos, resultados clave y progreso", fr: "Lire les objectifs, résultats clés et avancées", de: "Ziele, Schlüsselergebnisse und Fortschritt lesen" },
  },
  sheets: {
    label: { en: "Sheets", "zh-Hans": "电子表格", ja: "スプレッドシート", ko: "스프레드시트", es: "Hojas de cálculo", fr: "Feuilles de calcul", de: "Tabellen" },
    description: { en: "Read spreadsheets", "zh-Hans": "读取电子表格", ja: "スプレッドシートを読み取る", ko: "스프레드시트 읽기", es: "Leer hojas de cálculo", fr: "Lire des feuilles de calcul", de: "Tabellen lesen" },
  },
  slides: {
    label: { en: "Slides", "zh-Hans": "幻灯片", ja: "スライド", ko: "슬라이드", es: "Presentaciones", fr: "Présentations", de: "Präsentationen" },
    description: { en: "Read presentations", "zh-Hans": "读取演示文稿", ja: "プレゼンテーションを読み取る", ko: "프레젠테이션 읽기", es: "Leer presentaciones", fr: "Lire des présentations", de: "Präsentationen lesen" },
  },
  task: {
    label: { en: "Tasks", "zh-Hans": "任务", ja: "タスク", ko: "작업", es: "Tareas", fr: "Tâches", de: "Aufgaben" },
    description: { en: "Read tasks and task lists", "zh-Hans": "读取任务和任务清单", ja: "タスクとタスクリストを読み取る", ko: "작업과 작업 목록 읽기", es: "Leer tareas y listas de tareas", fr: "Lire les tâches et listes de tâches", de: "Aufgaben und Aufgabenlisten lesen" },
  },
  vc: {
    label: { en: "Video meetings", "zh-Hans": "视频会议", ja: "ビデオ会議", ko: "화상 회의", es: "Videollamadas", fr: "Visioconférences", de: "Videokonferenzen" },
    description: { en: "Read and search meetings, notes and recordings", "zh-Hans": "读取和搜索会议、纪要和录制", ja: "会議、メモ、録画を読み取り、検索する", ko: "회의, 메모, 녹화 읽기 및 검색", es: "Leer y buscar reuniones, notas y grabaciones", fr: "Lire et rechercher réunions, notes et enregistrements", de: "Besprechungen, Notizen und Aufzeichnungen lesen und durchsuchen" },
  },
  whiteboard: {
    label: { en: "Whiteboards", "zh-Hans": "画板", ja: "ホワイトボード", ko: "화이트보드", es: "Pizarras", fr: "Tableaux blancs", de: "Zeichenflächen" },
    description: { en: "Read whiteboards", "zh-Hans": "读取画板", ja: "ホワイトボードを読み取る", ko: "화이트보드 읽기", es: "Leer pizarras", fr: "Lire les tableaux blancs", de: "Zeichenflächen lesen" },
  },
  wiki: {
    label: { en: "Wiki", "zh-Hans": "知识库", ja: "Wiki", ko: "위키", es: "Wiki", fr: "Wiki", de: "Wiki" },
    description: { en: "Read spaces, pages and members", "zh-Hans": "读取知识空间、页面和成员", ja: "スペース、ページ、メンバーを読み取る", ko: "스페이스, 페이지, 구성원 읽기", es: "Leer espacios, páginas y miembros", fr: "Lire les espaces, pages et membres", de: "Bereiche, Seiten und Mitglieder lesen" },
  },
};
