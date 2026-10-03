export const TOKEN_HUD_LOCALES = ['en', 'zh-Hans', 'ja', 'ko', 'es', 'fr', 'de'] as const

export type TokenHudLocale = (typeof TOKEN_HUD_LOCALES)[number]

export interface TokenHudStrings {
  readonly hudLabel: string
  readonly total: string
  readonly cache: string
  readonly latency: string
  readonly speedLabel: string
  readonly totalLabel: string
  readonly cacheLabel: string
  readonly latencyLabel: string
  readonly speedPending: string
  readonly toggleCommand: string
  readonly toggleDescription: string
  readonly shownToast: string
  readonly hiddenToast: string
  readonly threadUsageTitle: string
  readonly threadUsageEmpty: string
  readonly turnsUnit: string
  readonly unknownModel: string
}

const CATALOG: Record<TokenHudLocale, TokenHudStrings> = {
  en: {
    hudLabel: 'Token HUD',
    total: 'total',
    cache: 'cache',
    latency: 'TTFT',
    speedLabel: 'Generation speed',
    totalLabel: 'Workspace total tokens',
    cacheLabel: 'Cache hit rate',
    latencyLabel: 'Time to first token',
    speedPending: 'Generation speed is being measured',
    toggleCommand: 'Token HUD: show or hide',
    toggleDescription: 'Shows or hides the performance readout.',
    shownToast: 'Token HUD is on.',
    hiddenToast: 'Token HUD is off.',
    threadUsageTitle: 'Usage in this chat',
    threadUsageEmpty: 'No usage in this chat yet.',
    turnsUnit: 'turns',
    unknownModel: 'Unknown model'
  },
  'zh-Hans': {
    hudLabel: 'Token 状态条',
    total: '累计',
    cache: '缓存',
    latency: '首 token',
    speedLabel: '生成速度',
    totalLabel: '工作区累计 token',
    cacheLabel: '缓存命中率',
    latencyLabel: '首 token 延迟',
    speedPending: '正在测量生成速度',
    toggleCommand: 'Token 状态条：显示或隐藏',
    toggleDescription: '显示或隐藏性能读数。',
    shownToast: 'Token 状态条已开启。',
    hiddenToast: 'Token 状态条已关闭。',
    threadUsageTitle: '本线程用量',
    threadUsageEmpty: '本线程还没有用量。',
    turnsUnit: '回合',
    unknownModel: '未知模型'
  },
  ja: {
    hudLabel: 'トークン HUD',
    total: '合計',
    cache: 'キャッシュ',
    latency: 'TTFT',
    speedLabel: '生成速度',
    totalLabel: 'ワークスペースの合計トークン',
    cacheLabel: 'キャッシュヒット率',
    latencyLabel: '最初のトークンまでの時間',
    speedPending: '生成速度を測定中',
    toggleCommand: 'トークン HUD: 表示 / 非表示',
    toggleDescription: 'パフォーマンス表示を切り替えます。',
    shownToast: 'トークン HUD をオンにしました。',
    hiddenToast: 'トークン HUD をオフにしました。',
    threadUsageTitle: 'このチャットの使用量',
    threadUsageEmpty: 'このチャットにはまだ使用量がありません。',
    turnsUnit: 'ターン',
    unknownModel: '不明なモデル'
  },
  ko: {
    hudLabel: '토큰 HUD',
    total: '누적',
    cache: '캐시',
    latency: 'TTFT',
    speedLabel: '생성 속도',
    totalLabel: '작업 공간 누적 토큰',
    cacheLabel: '캐시 적중률',
    latencyLabel: '첫 토큰까지 걸린 시간',
    speedPending: '생성 속도를 측정하는 중',
    toggleCommand: '토큰 HUD: 표시 / 숨기기',
    toggleDescription: '성능 표시를 켜거나 끕니다.',
    shownToast: '토큰 HUD를 켰습니다.',
    hiddenToast: '토큰 HUD를 껐습니다.',
    threadUsageTitle: '이 채팅의 사용량',
    threadUsageEmpty: '이 채팅에는 아직 사용량이 없습니다.',
    turnsUnit: '턴',
    unknownModel: '알 수 없는 모델'
  },
  es: {
    hudLabel: 'HUD de tokens',
    total: 'total',
    cache: 'caché',
    latency: 'TTFT',
    speedLabel: 'Velocidad de generación',
    totalLabel: 'Tokens totales del espacio de trabajo',
    cacheLabel: 'Tasa de aciertos de caché',
    latencyLabel: 'Tiempo hasta el primer token',
    speedPending: 'Midiendo la velocidad de generación',
    toggleCommand: 'HUD de tokens: mostrar u ocultar',
    toggleDescription: 'Muestra u oculta el indicador de rendimiento.',
    shownToast: 'HUD de tokens activado.',
    hiddenToast: 'HUD de tokens desactivado.',
    threadUsageTitle: 'Uso en este chat',
    threadUsageEmpty: 'Este chat aún no tiene uso.',
    turnsUnit: 'turnos',
    unknownModel: 'Modelo desconocido'
  },
  fr: {
    hudLabel: 'HUD des jetons',
    total: 'total',
    cache: 'cache',
    latency: 'TTFT',
    speedLabel: 'Vitesse de génération',
    totalLabel: 'Total des jetons de l’espace de travail',
    cacheLabel: 'Taux de réussite du cache',
    latencyLabel: 'Temps jusqu’au premier jeton',
    speedPending: 'Mesure de la vitesse de génération',
    toggleCommand: 'HUD des jetons : afficher ou masquer',
    toggleDescription: 'Affiche ou masque le relevé de performances.',
    shownToast: 'HUD des jetons activé.',
    hiddenToast: 'HUD des jetons désactivé.',
    threadUsageTitle: 'Consommation dans cette discussion',
    threadUsageEmpty: 'Aucune consommation dans cette discussion pour l’instant.',
    turnsUnit: 'tours',
    unknownModel: 'Modèle inconnu'
  },
  de: {
    hudLabel: 'Token-HUD',
    total: 'gesamt',
    cache: 'Cache',
    latency: 'TTFT',
    speedLabel: 'Generierungstempo',
    totalLabel: 'Token-Gesamtnutzung des Arbeitsbereichs',
    cacheLabel: 'Cache-Trefferquote',
    latencyLabel: 'Zeit bis zum ersten Token',
    speedPending: 'Generierungstempo wird gemessen',
    toggleCommand: 'Token-HUD: ein- oder ausblenden',
    toggleDescription: 'Blendet die Leistungsanzeige ein oder aus.',
    shownToast: 'Token-HUD ist an.',
    hiddenToast: 'Token-HUD ist aus.',
    threadUsageTitle: 'Verbrauch in diesem Chat',
    threadUsageEmpty: 'In diesem Chat gibt es noch keinen Verbrauch.',
    turnsUnit: 'Runden',
    unknownModel: 'Unbekanntes Modell'
  }
}

export function stringsFor(locale: string): TokenHudStrings {
  const exact = CATALOG[locale as TokenHudLocale]
  if (exact !== undefined) return exact
  const base = locale.split('-')[0]
  const match = TOKEN_HUD_LOCALES.find((candidate) => candidate.split('-')[0] === base)
  return match !== undefined ? CATALOG[match] : CATALOG.en
}

export function translationsOf(key: keyof TokenHudStrings): Record<string, string> {
  return Object.fromEntries(TOKEN_HUD_LOCALES.map((locale) => [locale, CATALOG[locale][key]]))
}
