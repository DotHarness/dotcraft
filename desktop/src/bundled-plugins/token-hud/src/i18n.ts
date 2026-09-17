export const TOKEN_HUD_LOCALES = ['en', 'zh-Hans', 'ja', 'ko', 'es', 'fr', 'de'] as const

export type TokenHudLocale = (typeof TOKEN_HUD_LOCALES)[number]

export interface TokenHudStrings {
  readonly settingsLabel: string
  readonly settingsTitle: string
  readonly settingsDescription: string
  readonly hudLabel: string
  readonly total: string
  readonly cache: string
  readonly latency: string
  readonly speedLabel: string
  readonly totalLabel: string
  readonly cacheLabel: string
  readonly latencyLabel: string
  readonly speedPending: string
  readonly visibleLabel: string
  readonly visibleDescription: string
  readonly generalGroup: string
  readonly opacityLabel: string
  readonly toggleCommand: string
  readonly toggleDescription: string
  readonly shownToast: string
  readonly hiddenToast: string
  readonly threadUsageTitle: string
  readonly threadUsageEmpty: string
  readonly turnsUnit: string
  readonly unknownModel: string
  readonly reasoningLow: string
  readonly reasoningMedium: string
  readonly reasoningHigh: string
  readonly reasoningExtraHigh: string
  readonly speedStandard: string
  readonly speedFast: string
}

const CATALOG: Record<TokenHudLocale, TokenHudStrings> = {
  en: {
    settingsLabel: 'Token HUD',
    settingsTitle: 'Token HUD',
    settingsDescription: 'See generation speed, workspace token use, and cache efficiency.',
    hudLabel: 'Token HUD',
    total: 'total',
    cache: 'cache',
    latency: 'TTFT',
    speedLabel: 'Generation speed',
    totalLabel: 'Workspace total tokens',
    cacheLabel: 'Cache hit rate',
    latencyLabel: 'Time to first token',
    speedPending: 'Generation speed is being measured',
    visibleLabel: 'Show Token HUD',
    visibleDescription: 'Hover the readout to see this chat’s usage by model.',
    generalGroup: 'General',
    opacityLabel: 'Opacity',
    toggleCommand: 'Token HUD: show or hide',
    toggleDescription: 'Shows or hides the performance readout.',
    shownToast: 'Token HUD is on.',
    hiddenToast: 'Token HUD is off.',
    threadUsageTitle: 'Usage in this chat',
    threadUsageEmpty: 'No usage in this chat yet.',
    turnsUnit: 'turns',
    unknownModel: 'Unknown model',
    reasoningLow: 'Low',
    reasoningMedium: 'Medium',
    reasoningHigh: 'High',
    reasoningExtraHigh: 'Extra high',
    speedStandard: 'Standard',
    speedFast: 'Fast'
  },
  'zh-Hans': {
    settingsLabel: 'Token 状态条',
    settingsTitle: 'Token 状态条',
    settingsDescription: '查看生成速度、工作区 token 累计用量和缓存效率。',
    hudLabel: 'Token 状态条',
    total: '累计',
    cache: '缓存',
    latency: '首 token',
    speedLabel: '生成速度',
    totalLabel: '工作区累计 token',
    cacheLabel: '缓存命中率',
    latencyLabel: '首 token 延迟',
    speedPending: '正在测量生成速度',
    visibleLabel: '显示 Token 状态条',
    visibleDescription: '悬停状态条可查看本线程按模型的用量。',
    generalGroup: '通用',
    opacityLabel: '不透明度',
    toggleCommand: 'Token 状态条：显示或隐藏',
    toggleDescription: '显示或隐藏性能读数。',
    shownToast: 'Token 状态条已开启。',
    hiddenToast: 'Token 状态条已关闭。',
    threadUsageTitle: '本线程用量',
    threadUsageEmpty: '本线程还没有用量。',
    turnsUnit: '回合',
    unknownModel: '未知模型',
    reasoningLow: '低',
    reasoningMedium: '中',
    reasoningHigh: '高',
    reasoningExtraHigh: '超高',
    speedStandard: '标准',
    speedFast: '快速'
  },
  ja: {
    settingsLabel: 'トークン HUD',
    settingsTitle: 'トークン HUD',
    settingsDescription: '生成速度、ワークスペースのトークン使用量、キャッシュ効率を表示します。',
    hudLabel: 'トークン HUD',
    total: '合計',
    cache: 'キャッシュ',
    latency: 'TTFT',
    speedLabel: '生成速度',
    totalLabel: 'ワークスペースの合計トークン',
    cacheLabel: 'キャッシュヒット率',
    latencyLabel: '最初のトークンまでの時間',
    speedPending: '生成速度を測定中',
    visibleLabel: 'トークン HUD を表示',
    visibleDescription: '表示にカーソルを合わせると、このチャットのモデル別使用量を確認できます。',
    generalGroup: '一般',
    opacityLabel: '不透明度',
    toggleCommand: 'トークン HUD: 表示 / 非表示',
    toggleDescription: 'パフォーマンス表示を切り替えます。',
    shownToast: 'トークン HUD をオンにしました。',
    hiddenToast: 'トークン HUD をオフにしました。',
    threadUsageTitle: 'このチャットの使用量',
    threadUsageEmpty: 'このチャットにはまだ使用量がありません。',
    turnsUnit: 'ターン',
    unknownModel: '不明なモデル',
    reasoningLow: '低',
    reasoningMedium: '中',
    reasoningHigh: '高',
    reasoningExtraHigh: '最高',
    speedStandard: '標準',
    speedFast: '高速'
  },
  ko: {
    settingsLabel: '토큰 HUD',
    settingsTitle: '토큰 HUD',
    settingsDescription: '생성 속도, 작업 공간 토큰 사용량, 캐시 효율을 표시합니다.',
    hudLabel: '토큰 HUD',
    total: '누적',
    cache: '캐시',
    latency: 'TTFT',
    speedLabel: '생성 속도',
    totalLabel: '작업 공간 누적 토큰',
    cacheLabel: '캐시 적중률',
    latencyLabel: '첫 토큰까지 걸린 시간',
    speedPending: '생성 속도를 측정하는 중',
    visibleLabel: '토큰 HUD 표시',
    visibleDescription: '표시 위에 마우스를 올리면 이 채팅의 모델별 사용량을 볼 수 있습니다.',
    generalGroup: '일반',
    opacityLabel: '불투명도',
    toggleCommand: '토큰 HUD: 표시 / 숨기기',
    toggleDescription: '성능 표시를 켜거나 끕니다.',
    shownToast: '토큰 HUD를 켰습니다.',
    hiddenToast: '토큰 HUD를 껐습니다.',
    threadUsageTitle: '이 채팅의 사용량',
    threadUsageEmpty: '이 채팅에는 아직 사용량이 없습니다.',
    turnsUnit: '턴',
    unknownModel: '알 수 없는 모델',
    reasoningLow: '낮음',
    reasoningMedium: '중간',
    reasoningHigh: '높음',
    reasoningExtraHigh: '매우 높음',
    speedStandard: '표준',
    speedFast: '빠름'
  },
  es: {
    settingsLabel: 'HUD de tokens',
    settingsTitle: 'HUD de tokens',
    settingsDescription: 'Muestra la velocidad de generación, el uso de tokens y la eficiencia de la caché.',
    hudLabel: 'HUD de tokens',
    total: 'total',
    cache: 'caché',
    latency: 'TTFT',
    speedLabel: 'Velocidad de generación',
    totalLabel: 'Tokens totales del espacio de trabajo',
    cacheLabel: 'Tasa de aciertos de caché',
    latencyLabel: 'Tiempo hasta el primer token',
    speedPending: 'Midiendo la velocidad de generación',
    visibleLabel: 'Mostrar HUD de tokens',
    visibleDescription: 'Pasa el cursor por el indicador para ver el uso de este chat por modelo.',
    generalGroup: 'General',
    opacityLabel: 'Opacidad',
    toggleCommand: 'HUD de tokens: mostrar u ocultar',
    toggleDescription: 'Muestra u oculta el indicador de rendimiento.',
    shownToast: 'HUD de tokens activado.',
    hiddenToast: 'HUD de tokens desactivado.',
    threadUsageTitle: 'Uso en este chat',
    threadUsageEmpty: 'Este chat aún no tiene uso.',
    turnsUnit: 'turnos',
    unknownModel: 'Modelo desconocido',
    reasoningLow: 'Bajo',
    reasoningMedium: 'Medio',
    reasoningHigh: 'Alto',
    reasoningExtraHigh: 'Muy alto',
    speedStandard: 'Estándar',
    speedFast: 'Rápido'
  },
  fr: {
    settingsLabel: 'HUD des jetons',
    settingsTitle: 'HUD des jetons',
    settingsDescription: 'Affiche la vitesse de génération, les jetons utilisés et l’efficacité du cache.',
    hudLabel: 'HUD des jetons',
    total: 'total',
    cache: 'cache',
    latency: 'TTFT',
    speedLabel: 'Vitesse de génération',
    totalLabel: 'Total des jetons de l’espace de travail',
    cacheLabel: 'Taux de réussite du cache',
    latencyLabel: 'Temps jusqu’au premier jeton',
    speedPending: 'Mesure de la vitesse de génération',
    visibleLabel: 'Afficher le HUD des jetons',
    visibleDescription: 'Survolez l’indicateur pour voir la consommation de cette discussion par modèle.',
    generalGroup: 'Général',
    opacityLabel: 'Opacité',
    toggleCommand: 'HUD des jetons : afficher ou masquer',
    toggleDescription: 'Affiche ou masque le relevé de performances.',
    shownToast: 'HUD des jetons activé.',
    hiddenToast: 'HUD des jetons désactivé.',
    threadUsageTitle: 'Consommation dans cette discussion',
    threadUsageEmpty: 'Aucune consommation dans cette discussion pour l’instant.',
    turnsUnit: 'tours',
    unknownModel: 'Modèle inconnu',
    reasoningLow: 'Faible',
    reasoningMedium: 'Moyen',
    reasoningHigh: 'Élevé',
    reasoningExtraHigh: 'Très élevé',
    speedStandard: 'Standard',
    speedFast: 'Rapide'
  },
  de: {
    settingsLabel: 'Token-HUD',
    settingsTitle: 'Token-HUD',
    settingsDescription: 'Zeigt Generierungstempo, Tokenverbrauch und Cache-Effizienz des Arbeitsbereichs.',
    hudLabel: 'Token-HUD',
    total: 'gesamt',
    cache: 'Cache',
    latency: 'TTFT',
    speedLabel: 'Generierungstempo',
    totalLabel: 'Token-Gesamtnutzung des Arbeitsbereichs',
    cacheLabel: 'Cache-Trefferquote',
    latencyLabel: 'Zeit bis zum ersten Token',
    speedPending: 'Generierungstempo wird gemessen',
    visibleLabel: 'Token-HUD anzeigen',
    visibleDescription: 'Fahre über die Anzeige, um den Verbrauch dieses Chats nach Modell zu sehen.',
    generalGroup: 'Allgemein',
    opacityLabel: 'Deckkraft',
    toggleCommand: 'Token-HUD: ein- oder ausblenden',
    toggleDescription: 'Blendet die Leistungsanzeige ein oder aus.',
    shownToast: 'Token-HUD ist an.',
    hiddenToast: 'Token-HUD ist aus.',
    threadUsageTitle: 'Verbrauch in diesem Chat',
    threadUsageEmpty: 'In diesem Chat gibt es noch keinen Verbrauch.',
    turnsUnit: 'Runden',
    unknownModel: 'Unbekanntes Modell',
    reasoningLow: 'Niedrig',
    reasoningMedium: 'Mittel',
    reasoningHigh: 'Hoch',
    reasoningExtraHigh: 'Sehr hoch',
    speedStandard: 'Standard',
    speedFast: 'Schnell'
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
