export const WALLPAPER_LOCALES = ['en', 'zh-Hans', 'ja', 'ko', 'es', 'fr', 'de'] as const

export type WallpaperLocale = (typeof WALLPAPER_LOCALES)[number]

export interface WallpaperStrings {
  readonly settingsLabel: string
  readonly settingsTitle: string
  readonly settingsDescription: string
  readonly enableLabel: string
  readonly enableDescription: string
  readonly sceneGroup: string
  readonly lightTab: string
  readonly darkTab: string
  readonly noneTile: string
  readonly addImage: string
  readonly removeImage: string
  readonly adjustGroup: string
  readonly blurLabel: string
  readonly dimLabel: string
  readonly surfaceLabel: string
  readonly surfaceDescription: string
  readonly fitLabel: string
  readonly fitCover: string
  readonly fitContain: string
  readonly fitTile: string
  readonly nextCommand: string
  readonly nextDescription: string
  readonly toggleCommand: string
  readonly toggleDescription: string
  readonly toggledOn: string
  readonly toggledOff: string
  readonly imageTooLarge: string
  readonly removeTitle: string
  readonly removeMessage: string
  readonly openSettings: string
}

const CATALOG: Record<WallpaperLocale, WallpaperStrings> = {
  en: {
    settingsLabel: 'Wallpaper',
    settingsTitle: 'Wallpaper',
    settingsDescription: 'Put a picture behind DotCraft instead of the flat background colour.',
    enableLabel: 'Show wallpaper',
    enableDescription: 'Turn the picture off without losing your choices.',
    sceneGroup: 'Scene',
    lightTab: 'Light theme',
    darkTab: 'Dark theme',
    noneTile: 'None',
    addImage: 'Add image',
    removeImage: 'Remove',
    adjustGroup: 'Adjust',
    blurLabel: 'Blur',
    dimLabel: 'Dim',
    surfaceLabel: 'Surface opacity',
    surfaceDescription: 'How solid the sidebar, chat, and Composer stay over the picture.',
    fitLabel: 'Fit',
    fitCover: 'Fill',
    fitContain: 'Fit',
    fitTile: 'Tile',
    nextCommand: 'Wallpaper: next scene',
    nextDescription: 'Cycles the scene for the current theme.',
    toggleCommand: 'Wallpaper: show or hide',
    toggleDescription: 'Turns the wallpaper off and back on.',
    toggledOn: 'Wallpaper is on.',
    toggledOff: 'Wallpaper is off.',
    imageTooLarge: 'That image is larger than 16 MB.',
    removeTitle: 'Remove this image?',
    removeMessage: 'It is deleted from this computer and cannot be recovered.',
    openSettings: 'Open settings'
  },
  'zh-Hans': {
    settingsLabel: '壁纸',
    settingsTitle: '壁纸',
    settingsDescription: '用一张图片代替 DotCraft 的纯色背景。',
    enableLabel: '显示壁纸',
    enableDescription: '临时关掉图片，但保留你的选择。',
    sceneGroup: '画面',
    lightTab: '浅色主题',
    darkTab: '深色主题',
    noneTile: '不使用',
    addImage: '添加图片',
    removeImage: '移除',
    adjustGroup: '调节',
    blurLabel: '模糊',
    dimLabel: '压暗',
    surfaceLabel: '界面不透明度',
    surfaceDescription: '侧边栏、对话区和输入框在图片上保留多少实色。',
    fitLabel: '填充方式',
    fitCover: '铺满',
    fitContain: '完整显示',
    fitTile: '平铺',
    nextCommand: '壁纸：换下一张',
    nextDescription: '为当前主题切换到下一张画面。',
    toggleCommand: '壁纸：显示或隐藏',
    toggleDescription: '快速开关壁纸。',
    toggledOn: '壁纸已开启。',
    toggledOff: '壁纸已关闭。',
    imageTooLarge: '这张图片超过 16 MB。',
    removeTitle: '要移除这张图片吗？',
    removeMessage: '图片会从本机删除，无法恢复。',
    openSettings: '打开设置'
  },
  ja: {
    settingsLabel: '壁紙',
    settingsTitle: '壁紙',
    settingsDescription: '単色の背景の代わりに画像を表示します。',
    enableLabel: '壁紙を表示',
    enableDescription: '設定を保ったまま画像だけをオフにします。',
    sceneGroup: 'シーン',
    lightTab: 'ライトテーマ',
    darkTab: 'ダークテーマ',
    noneTile: 'なし',
    addImage: '画像を追加',
    removeImage: '削除',
    adjustGroup: '調整',
    blurLabel: 'ぼかし',
    dimLabel: '暗さ',
    surfaceLabel: '画面の不透明度',
    surfaceDescription: 'サイドバー・チャット・コンポーザーをどれだけ不透明に保つか。',
    fitLabel: '表示方法',
    fitCover: '全体を覆う',
    fitContain: '全体を表示',
    fitTile: 'タイル',
    nextCommand: '壁紙: 次のシーン',
    nextDescription: '現在のテーマのシーンを切り替えます。',
    toggleCommand: '壁紙: 表示 / 非表示',
    toggleDescription: '壁紙をすばやく切り替えます。',
    toggledOn: '壁紙をオンにしました。',
    toggledOff: '壁紙をオフにしました。',
    imageTooLarge: 'この画像は 16 MB を超えています。',
    removeTitle: 'この画像を削除しますか？',
    removeMessage: 'この端末から削除され、元に戻せません。',
    openSettings: '設定を開く'
  },
  ko: {
    settingsLabel: '배경 이미지',
    settingsTitle: '배경 이미지',
    settingsDescription: '단색 배경 대신 사진을 사용합니다.',
    enableLabel: '배경 이미지 표시',
    enableDescription: '설정을 유지한 채 이미지만 끕니다.',
    sceneGroup: '장면',
    lightTab: '라이트 테마',
    darkTab: '다크 테마',
    noneTile: '없음',
    addImage: '이미지 추가',
    removeImage: '삭제',
    adjustGroup: '조정',
    blurLabel: '흐림',
    dimLabel: '어둡게',
    surfaceLabel: '화면 불투명도',
    surfaceDescription: '사이드바, 대화, 작성기가 얼마나 불투명하게 남을지 정합니다.',
    fitLabel: '맞춤',
    fitCover: '채우기',
    fitContain: '전체 보기',
    fitTile: '바둑판',
    nextCommand: '배경 이미지: 다음 장면',
    nextDescription: '현재 테마의 장면을 전환합니다.',
    toggleCommand: '배경 이미지: 표시 / 숨기기',
    toggleDescription: '배경 이미지를 빠르게 켜고 끕니다.',
    toggledOn: '배경 이미지를 켰습니다.',
    toggledOff: '배경 이미지를 껐습니다.',
    imageTooLarge: '이 이미지는 16MB를 넘습니다.',
    removeTitle: '이 이미지를 삭제할까요?',
    removeMessage: '이 컴퓨터에서 삭제되며 복구할 수 없습니다.',
    openSettings: '설정 열기'
  },
  es: {
    settingsLabel: 'Fondo',
    settingsTitle: 'Fondo',
    settingsDescription: 'Pon una imagen detrás de DotCraft en lugar del color plano.',
    enableLabel: 'Mostrar el fondo',
    enableDescription: 'Apaga la imagen sin perder tus elecciones.',
    sceneGroup: 'Escena',
    lightTab: 'Tema claro',
    darkTab: 'Tema oscuro',
    noneTile: 'Ninguna',
    addImage: 'Añadir imagen',
    removeImage: 'Quitar',
    adjustGroup: 'Ajustes',
    blurLabel: 'Desenfoque',
    dimLabel: 'Oscurecer',
    surfaceLabel: 'Opacidad de la interfaz',
    surfaceDescription: 'Cuánto color sólido conservan la barra lateral, el chat y el Composer.',
    fitLabel: 'Encaje',
    fitCover: 'Rellenar',
    fitContain: 'Ajustar',
    fitTile: 'Mosaico',
    nextCommand: 'Fondo: siguiente escena',
    nextDescription: 'Cambia la escena del tema actual.',
    toggleCommand: 'Fondo: mostrar u ocultar',
    toggleDescription: 'Activa y desactiva el fondo.',
    toggledOn: 'Fondo activado.',
    toggledOff: 'Fondo desactivado.',
    imageTooLarge: 'Esa imagen supera los 16 MB.',
    removeTitle: '¿Quitar esta imagen?',
    removeMessage: 'Se borra de este equipo y no se puede recuperar.',
    openSettings: 'Abrir ajustes'
  },
  fr: {
    settingsLabel: 'Fond d’écran',
    settingsTitle: 'Fond d’écran',
    settingsDescription: 'Placez une image derrière DotCraft à la place de la couleur unie.',
    enableLabel: 'Afficher le fond',
    enableDescription: 'Coupez l’image sans perdre vos choix.',
    sceneGroup: 'Scène',
    lightTab: 'Thème clair',
    darkTab: 'Thème sombre',
    noneTile: 'Aucune',
    addImage: 'Ajouter une image',
    removeImage: 'Retirer',
    adjustGroup: 'Réglages',
    blurLabel: 'Flou',
    dimLabel: 'Assombrir',
    surfaceLabel: 'Opacité de l’interface',
    surfaceDescription: 'Ce que la barre latérale, la discussion et le Composer gardent d’opaque.',
    fitLabel: 'Cadrage',
    fitCover: 'Remplir',
    fitContain: 'Contenir',
    fitTile: 'Mosaïque',
    nextCommand: 'Fond : scène suivante',
    nextDescription: 'Change la scène du thème actuel.',
    toggleCommand: 'Fond : afficher ou masquer',
    toggleDescription: 'Active et désactive le fond.',
    toggledOn: 'Fond activé.',
    toggledOff: 'Fond désactivé.',
    imageTooLarge: 'Cette image dépasse 16 Mo.',
    removeTitle: 'Retirer cette image ?',
    removeMessage: 'Elle est supprimée de cet ordinateur et ne peut pas être récupérée.',
    openSettings: 'Ouvrir les réglages'
  },
  de: {
    settingsLabel: 'Hintergrundbild',
    settingsTitle: 'Hintergrundbild',
    settingsDescription: 'Statt der einfarbigen Fläche ein Bild hinter DotCraft legen.',
    enableLabel: 'Hintergrundbild zeigen',
    enableDescription: 'Schaltet das Bild ab, ohne die Auswahl zu verlieren.',
    sceneGroup: 'Szene',
    lightTab: 'Helles Theme',
    darkTab: 'Dunkles Theme',
    noneTile: 'Keins',
    addImage: 'Bild hinzufügen',
    removeImage: 'Entfernen',
    adjustGroup: 'Anpassen',
    blurLabel: 'Weichzeichnen',
    dimLabel: 'Abdunkeln',
    surfaceLabel: 'Deckkraft der Oberfläche',
    surfaceDescription: 'Wie deckend Seitenleiste, Chat und Composer über dem Bild bleiben.',
    fitLabel: 'Anpassung',
    fitCover: 'Füllen',
    fitContain: 'Einpassen',
    fitTile: 'Kacheln',
    nextCommand: 'Hintergrundbild: nächste Szene',
    nextDescription: 'Wechselt die Szene des aktuellen Themes.',
    toggleCommand: 'Hintergrundbild: ein- oder ausblenden',
    toggleDescription: 'Schaltet das Hintergrundbild um.',
    toggledOn: 'Hintergrundbild ist an.',
    toggledOff: 'Hintergrundbild ist aus.',
    imageTooLarge: 'Dieses Bild ist größer als 16 MB.',
    removeTitle: 'Dieses Bild entfernen?',
    removeMessage: 'Es wird von diesem Rechner gelöscht und lässt sich nicht wiederherstellen.',
    openSettings: 'Einstellungen öffnen'
  }
}

export function stringsFor(locale: string): WallpaperStrings {
  const exact = CATALOG[locale as WallpaperLocale]
  if (exact !== undefined) return exact
  const base = locale.split('-')[0]
  const match = WALLPAPER_LOCALES.find((candidate) => candidate.split('-')[0] === base)
  return match !== undefined ? CATALOG[match] : CATALOG.en
}

export function translationsOf(key: keyof WallpaperStrings): Record<string, string> {
  return Object.fromEntries(WALLPAPER_LOCALES.map((locale) => [locale, CATALOG[locale][key]]))
}
