export const TOKEN_HUD_LOCALES = ['en', 'zh-Hans', 'ja', 'ko', 'es', 'fr', 'de'] as const

export type TokenHudLocale = (typeof TOKEN_HUD_LOCALES)[number]

export interface TokenHudStrings {
  readonly settingsLabel: string
  readonly settingsTitle: string
  readonly settingsDescription: string
  readonly hudLabel: string
  readonly total: string
  readonly cache: string
  readonly speedLabel: string
  readonly totalLabel: string
  readonly cacheLabel: string
  readonly speedPending: string
  readonly speedUnavailable: string
  readonly visibleLabel: string
  readonly visibleDescription: string
  readonly generalGroup: string
  readonly opacityLabel: string
  readonly toggleCommand: string
  readonly toggleDescription: string
  readonly shownToast: string
  readonly hiddenToast: string
}

const CATALOG: Record<TokenHudLocale, TokenHudStrings> = {
  en: {
    settingsLabel: 'Token HUD',
    settingsTitle: 'Token HUD',
    settingsDescription: 'See generation speed, workspace token use, and cache efficiency.',
    hudLabel: 'Token HUD',
    total: 'total',
    cache: 'cache',
    speedLabel: 'Generation speed',
    totalLabel: 'Workspace total tokens',
    cacheLabel: 'Cache hit rate',
    speedPending: 'Generation speed is being measured',
    speedUnavailable: 'Generation speed is unavailable',
    visibleLabel: 'Show Token HUD',
    visibleDescription: 'The status readout stays click-through.',
    generalGroup: 'General',
    opacityLabel: 'Opacity',
    toggleCommand: 'Token HUD: show or hide',
    toggleDescription: 'Shows or hides the performance readout.',
    shownToast: 'Token HUD is on.',
    hiddenToast: 'Token HUD is off.'
  },
  'zh-Hans': {
    settingsLabel: 'Token 状态条',
    settingsTitle: 'Token 状态条',
    settingsDescription: '查看生成速度、工作区 token 累计用量和缓存效率。',
    hudLabel: 'Token 状态条',
    total: '累计',
    cache: '缓存',
    speedLabel: '生成速度',
    totalLabel: '工作区累计 token',
    cacheLabel: '缓存命中率',
    speedPending: '正在测量生成速度',
    speedUnavailable: '暂无生成速度',
    visibleLabel: '显示 Token 状态条',
    visibleDescription: '状态条不会挡住点击。',
    generalGroup: '通用',
    opacityLabel: '不透明度',
    toggleCommand: 'Token 状态条：显示或隐藏',
    toggleDescription: '显示或隐藏性能读数。',
    shownToast: 'Token 状态条已开启。',
    hiddenToast: 'Token 状态条已关闭。'
  },
  ja: {
    settingsLabel: 'トークン HUD',
    settingsTitle: 'トークン HUD',
    settingsDescription: '生成速度、ワークスペースのトークン使用量、キャッシュ効率を表示します。',
    hudLabel: 'トークン HUD',
    total: '合計',
    cache: 'キャッシュ',
    speedLabel: '生成速度',
    totalLabel: 'ワークスペースの合計トークン',
    cacheLabel: 'キャッシュヒット率',
    speedPending: '生成速度を測定中',
    speedUnavailable: '生成速度を利用できません',
    visibleLabel: 'トークン HUD を表示',
    visibleDescription: 'ステータス表示はクリックを妨げません。',
    generalGroup: '一般',
    opacityLabel: '不透明度',
    toggleCommand: 'トークン HUD: 表示 / 非表示',
    toggleDescription: 'パフォーマンス表示を切り替えます。',
    shownToast: 'トークン HUD をオンにしました。',
    hiddenToast: 'トークン HUD をオフにしました。'
  },
  ko: {
    settingsLabel: '토큰 HUD',
    settingsTitle: '토큰 HUD',
    settingsDescription: '생성 속도, 작업 공간 토큰 사용량, 캐시 효율을 표시합니다.',
    hudLabel: '토큰 HUD',
    total: '누적',
    cache: '캐시',
    speedLabel: '생성 속도',
    totalLabel: '작업 공간 누적 토큰',
    cacheLabel: '캐시 적중률',
    speedPending: '생성 속도를 측정하는 중',
    speedUnavailable: '생성 속도를 사용할 수 없음',
    visibleLabel: '토큰 HUD 표시',
    visibleDescription: '상태 표시는 클릭을 가로채지 않습니다.',
    generalGroup: '일반',
    opacityLabel: '불투명도',
    toggleCommand: '토큰 HUD: 표시 / 숨기기',
    toggleDescription: '성능 표시를 켜거나 끕니다.',
    shownToast: '토큰 HUD를 켰습니다.',
    hiddenToast: '토큰 HUD를 껐습니다.'
  },
  es: {
    settingsLabel: 'HUD de tokens',
    settingsTitle: 'HUD de tokens',
    settingsDescription: 'Muestra la velocidad de generación, el uso de tokens y la eficiencia de la caché.',
    hudLabel: 'HUD de tokens',
    total: 'total',
    cache: 'caché',
    speedLabel: 'Velocidad de generación',
    totalLabel: 'Tokens totales del espacio de trabajo',
    cacheLabel: 'Tasa de aciertos de caché',
    speedPending: 'Midiendo la velocidad de generación',
    speedUnavailable: 'Velocidad de generación no disponible',
    visibleLabel: 'Mostrar HUD de tokens',
    visibleDescription: 'El indicador no intercepta los clics.',
    generalGroup: 'General',
    opacityLabel: 'Opacidad',
    toggleCommand: 'HUD de tokens: mostrar u ocultar',
    toggleDescription: 'Muestra u oculta el indicador de rendimiento.',
    shownToast: 'HUD de tokens activado.',
    hiddenToast: 'HUD de tokens desactivado.'
  },
  fr: {
    settingsLabel: 'HUD des jetons',
    settingsTitle: 'HUD des jetons',
    settingsDescription: 'Affiche la vitesse de génération, les jetons utilisés et l’efficacité du cache.',
    hudLabel: 'HUD des jetons',
    total: 'total',
    cache: 'cache',
    speedLabel: 'Vitesse de génération',
    totalLabel: 'Total des jetons de l’espace de travail',
    cacheLabel: 'Taux de réussite du cache',
    speedPending: 'Mesure de la vitesse de génération',
    speedUnavailable: 'Vitesse de génération indisponible',
    visibleLabel: 'Afficher le HUD des jetons',
    visibleDescription: 'Le relevé n’intercepte pas les clics.',
    generalGroup: 'Général',
    opacityLabel: 'Opacité',
    toggleCommand: 'HUD des jetons : afficher ou masquer',
    toggleDescription: 'Affiche ou masque le relevé de performances.',
    shownToast: 'HUD des jetons activé.',
    hiddenToast: 'HUD des jetons désactivé.'
  },
  de: {
    settingsLabel: 'Token-HUD',
    settingsTitle: 'Token-HUD',
    settingsDescription: 'Zeigt Generierungstempo, Tokenverbrauch und Cache-Effizienz des Arbeitsbereichs.',
    hudLabel: 'Token-HUD',
    total: 'gesamt',
    cache: 'Cache',
    speedLabel: 'Generierungstempo',
    totalLabel: 'Token-Gesamtnutzung des Arbeitsbereichs',
    cacheLabel: 'Cache-Trefferquote',
    speedPending: 'Generierungstempo wird gemessen',
    speedUnavailable: 'Generierungstempo nicht verfügbar',
    visibleLabel: 'Token-HUD anzeigen',
    visibleDescription: 'Die Statusanzeige fängt keine Klicks ab.',
    generalGroup: 'Allgemein',
    opacityLabel: 'Deckkraft',
    toggleCommand: 'Token-HUD: ein- oder ausblenden',
    toggleDescription: 'Blendet die Leistungsanzeige ein oder aus.',
    shownToast: 'Token-HUD ist an.',
    hiddenToast: 'Token-HUD ist aus.'
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
