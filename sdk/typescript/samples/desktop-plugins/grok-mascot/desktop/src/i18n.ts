import type { GrokColor, GrokShape } from './appearance'
import type { DesktopPluginMascotActivity } from '@dotcraft/plugin'

export const GROK_LOCALES = ['en', 'zh-Hans', 'ja', 'ko', 'es', 'fr', 'de'] as const

export type GrokLocale = (typeof GROK_LOCALES)[number]

export interface GrokStrings {
  readonly mascotLabel: string
  readonly settingsLabel: string
  readonly settingsTitle: string
  readonly settingsDescription: string
  readonly characterGroup: string
  readonly effectsGroup: string
  readonly automatic: string
  readonly colorLabel: string
  readonly colorDescription: string
  readonly shapeLabel: string
  readonly shapeDescription: string
  readonly statusRingLabel: string
  readonly statusRingDescription: string
  readonly previewLabel: string
  readonly previewStates: Record<DesktopPluginMascotActivity, string>
  readonly nextColorCommand: string
  readonly nextColorDescription: string
  readonly colorToast: string
  readonly colors: Record<GrokColor, string>
  readonly shapes: Record<GrokShape, string>
}

export type GrokTextKey = Exclude<keyof GrokStrings, 'colors' | 'shapes' | 'previewStates'>

const CATALOG: Record<GrokLocale, GrokStrings> = {
  en: {
    mascotLabel: 'Grok, the Composer companion',
    settingsLabel: 'Grok Mascot',
    settingsTitle: 'Grok Mascot',
    settingsDescription: 'Choose the color and shape of your Composer companion.',
    characterGroup: 'Character',
    effectsGroup: 'Effects',
    automatic: 'Automatic',
    colorLabel: 'Color',
    colorDescription: 'Automatic gives every workspace its own color.',
    shapeLabel: 'Shape',
    shapeDescription: 'Automatic gives every workspace its own silhouette.',
    statusRingLabel: 'Status ring',
    statusRingDescription: 'Draws a ring around the mascot when a turn succeeds or fails.',
    previewLabel: 'Preview',
    previewStates: {
      idle: 'Idle', focused: 'Focused', dragging: 'Dragging', working: 'Working',
      decision: 'Decision', success: 'Success', error: 'Error', sleeping: 'Sleeping'
    },
    nextColorCommand: 'Grok: next color',
    nextColorDescription: 'Cycles the mascot color without opening Settings.',
    colorToast: 'Grok is now {color}.',
    colors: {
      black: 'Black', brown: 'Brown', red: 'Red', orange: 'Orange', yellow: 'Yellow', green: 'Green',
      cyan: 'Cyan', blue: 'Blue', violet: 'Violet', magenta: 'Magenta', gray: 'Gray'
    },
    shapes: {
      blob: 'Blob', pebble: 'Pebble', squircle: 'Squircle', tablet: 'Tablet',
      wedge: 'Wedge', hex: 'Hex', cloud: 'Cloud', teardrop: 'Teardrop'
    }
  },
  'zh-Hans': {
    mascotLabel: 'Grok，输入框伙伴',
    settingsLabel: 'Grok吉祥物',
    settingsTitle: 'Grok吉祥物',
    settingsDescription: '选择输入框伙伴的颜色与形状。',
    characterGroup: '角色',
    effectsGroup: '特效',
    automatic: '自动',
    colorLabel: '颜色',
    colorDescription: '选择自动时，每个工作区都有专属颜色。',
    shapeLabel: '形状',
    shapeDescription: '选择自动时，每个工作区都有专属轮廓。',
    statusRingLabel: '状态光环',
    statusRingDescription: '任务成功或失败时，在吉祥物周围画一圈光环。',
    previewLabel: '预览',
    previewStates: {
      idle: '空闲', focused: '聚焦', dragging: '拖动', working: '工作中',
      decision: '待确认', success: '成功', error: '错误', sleeping: '休眠'
    },
    nextColorCommand: 'Grok：切换颜色',
    nextColorDescription: '不打开设置也能切换吉祥物颜色。',
    colorToast: 'Grok换成了{color}。',
    colors: {
      black: '黑色', brown: '棕色', red: '红色', orange: '橙色', yellow: '黄色', green: '绿色',
      cyan: '青色', blue: '蓝色', violet: '紫色', magenta: '品红', gray: '灰色'
    },
    shapes: {
      blob: '团块', pebble: '卵石', squircle: '圆角方', tablet: '胶囊',
      wedge: '楔形', hex: '六边形', cloud: '云朵', teardrop: '水滴'
    }
  },
  ja: {
    mascotLabel: 'Grok — コンポーザーの相棒',
    settingsLabel: 'Grok マスコット',
    settingsTitle: 'Grok マスコット',
    settingsDescription: 'コンポーザーの相棒の色と形を選びます。',
    characterGroup: 'キャラクター',
    effectsGroup: 'エフェクト',
    automatic: '自動',
    colorLabel: '色',
    colorDescription: '自動にすると、ワークスペースごとに色が変わります。',
    shapeLabel: '形',
    shapeDescription: '自動にすると、ワークスペースごとにシルエットが変わります。',
    statusRingLabel: 'ステータスリング',
    statusRingDescription: 'ターンの成功・失敗時にマスコットの周りへリングを描きます。',
    previewLabel: 'プレビュー',
    previewStates: {
      idle: '待機', focused: 'フォーカス', dragging: 'ドラッグ', working: '作業中',
      decision: '確認待ち', success: '成功', error: 'エラー', sleeping: 'スリープ'
    },
    nextColorCommand: 'Grok: 色を切り替え',
    nextColorDescription: '設定を開かずに色を切り替えます。',
    colorToast: 'Grokは{color}になりました。',
    colors: {
      black: 'ブラック', brown: 'ブラウン', red: 'レッド', orange: 'オレンジ', yellow: 'イエロー',
      green: 'グリーン', cyan: 'シアン', blue: 'ブルー', violet: 'バイオレット', magenta: 'マゼンタ',
      gray: 'グレー'
    },
    shapes: {
      blob: 'ブロブ', pebble: 'ペブル', squircle: 'スクワークル', tablet: 'タブレット',
      wedge: 'ウェッジ', hex: 'ヘックス', cloud: 'クラウド', teardrop: 'ティアドロップ'
    }
  },
  ko: {
    mascotLabel: 'Grok — 작성기 파트너',
    settingsLabel: 'Grok 마스코트',
    settingsTitle: 'Grok 마스코트',
    settingsDescription: '작성기 파트너의 색과 모양을 고릅니다.',
    characterGroup: '캐릭터',
    effectsGroup: '효과',
    automatic: '자동',
    colorLabel: '색',
    colorDescription: '자동으로 두면 작업 공간마다 색이 달라집니다.',
    shapeLabel: '모양',
    shapeDescription: '자동으로 두면 작업 공간마다 실루엣이 달라집니다.',
    statusRingLabel: '상태 링',
    statusRingDescription: '턴이 성공하거나 실패하면 마스코트 둘레에 링을 그립니다.',
    previewLabel: '미리보기',
    previewStates: {
      idle: '대기', focused: '포커스', dragging: '드래그', working: '작업 중',
      decision: '확인 대기', success: '성공', error: '오류', sleeping: '절전'
    },
    nextColorCommand: 'Grok: 색 전환',
    nextColorDescription: '설정을 열지 않고 색을 바꿉니다.',
    colorToast: 'Grok가 {color} 색이 되었습니다.',
    colors: {
      black: '검정', brown: '갈색', red: '빨강', orange: '주황', yellow: '노랑', green: '초록',
      cyan: '청록', blue: '파랑', violet: '보라', magenta: '자홍', gray: '회색'
    },
    shapes: {
      blob: '블롭', pebble: '조약돌', squircle: '스퀘어클', tablet: '알약',
      wedge: '쐐기', hex: '육각형', cloud: '구름', teardrop: '물방울'
    }
  },
  es: {
    mascotLabel: 'Grok, la compañera del Composer',
    settingsLabel: 'Mascota Grok',
    settingsTitle: 'Mascota Grok',
    settingsDescription: 'Elige el color y la forma de tu compañera del Composer.',
    characterGroup: 'Personaje',
    effectsGroup: 'Efectos',
    automatic: 'Automático',
    colorLabel: 'Color',
    colorDescription: 'En automático, cada espacio de trabajo tiene su propio color.',
    shapeLabel: 'Forma',
    shapeDescription: 'En automático, cada espacio de trabajo tiene su propia silueta.',
    statusRingLabel: 'Anillo de estado',
    statusRingDescription: 'Dibuja un anillo alrededor de la mascota al acertar o fallar.',
    previewLabel: 'Vista previa',
    previewStates: {
      idle: 'Inactivo', focused: 'Enfocado', dragging: 'Arrastrando', working: 'Trabajando',
      decision: 'Decisión', success: 'Correcto', error: 'Error', sleeping: 'Dormido'
    },
    nextColorCommand: 'Grok: siguiente color',
    nextColorDescription: 'Cambia el color sin abrir Ajustes.',
    colorToast: 'Grok ahora es {color}.',
    colors: {
      black: 'Negro', brown: 'Marrón', red: 'Rojo', orange: 'Naranja', yellow: 'Amarillo',
      green: 'Verde', cyan: 'Cian', blue: 'Azul', violet: 'Violeta', magenta: 'Magenta', gray: 'Gris'
    },
    shapes: {
      blob: 'Mancha', pebble: 'Guijarro', squircle: 'Cuadrado redondo', tablet: 'Cápsula',
      wedge: 'Cuña', hex: 'Hexágono', cloud: 'Nube', teardrop: 'Lágrima'
    }
  },
  fr: {
    mascotLabel: 'Grok, la compagne du Composer',
    settingsLabel: 'Mascotte Grok',
    settingsTitle: 'Mascotte Grok',
    settingsDescription: 'Choisissez la couleur et la forme de votre compagne du Composer.',
    characterGroup: 'Personnage',
    effectsGroup: 'Effets',
    automatic: 'Automatique',
    colorLabel: 'Couleur',
    colorDescription: 'En automatique, chaque espace de travail a sa propre couleur.',
    shapeLabel: 'Forme',
    shapeDescription: 'En automatique, chaque espace de travail a sa propre silhouette.',
    statusRingLabel: 'Anneau d’état',
    statusRingDescription: 'Trace un anneau autour de la mascotte en cas de succès ou d’échec.',
    previewLabel: 'Aperçu',
    previewStates: {
      idle: 'Inactif', focused: 'Actif', dragging: 'Glissement', working: 'En cours',
      decision: 'Décision', success: 'Réussite', error: 'Erreur', sleeping: 'Veille'
    },
    nextColorCommand: 'Grok : couleur suivante',
    nextColorDescription: 'Change de couleur sans ouvrir les réglages.',
    colorToast: 'Grok est maintenant {color}.',
    colors: {
      black: 'Noir', brown: 'Marron', red: 'Rouge', orange: 'Orange', yellow: 'Jaune', green: 'Vert',
      cyan: 'Cyan', blue: 'Bleu', violet: 'Violet', magenta: 'Magenta', gray: 'Gris'
    },
    shapes: {
      blob: 'Goutte', pebble: 'Galet', squircle: 'Carré arrondi', tablet: 'Gélule',
      wedge: 'Coin', hex: 'Hexagone', cloud: 'Nuage', teardrop: 'Larme'
    }
  },
  de: {
    mascotLabel: 'Grok, die Composer-Begleiterin',
    settingsLabel: 'Grok-Maskottchen',
    settingsTitle: 'Grok-Maskottchen',
    settingsDescription: 'Wähle Farbe und Form deiner Composer-Begleiterin.',
    characterGroup: 'Figur',
    effectsGroup: 'Effekte',
    automatic: 'Automatisch',
    colorLabel: 'Farbe',
    colorDescription: 'Bei „Automatisch“ bekommt jeder Arbeitsbereich eine eigene Farbe.',
    shapeLabel: 'Form',
    shapeDescription: 'Bei „Automatisch“ bekommt jeder Arbeitsbereich eine eigene Silhouette.',
    statusRingLabel: 'Statusring',
    statusRingDescription: 'Zeichnet einen Ring um das Maskottchen bei Erfolg oder Fehler.',
    previewLabel: 'Vorschau',
    previewStates: {
      idle: 'Inaktiv', focused: 'Fokussiert', dragging: 'Ziehen', working: 'Arbeitet',
      decision: 'Entscheidung', success: 'Erfolg', error: 'Fehler', sleeping: 'Ruhezustand'
    },
    nextColorCommand: 'Grok: nächste Farbe',
    nextColorDescription: 'Wechselt die Farbe, ohne die Einstellungen zu öffnen.',
    colorToast: 'Grok ist jetzt {color}.',
    colors: {
      black: 'Schwarz', brown: 'Braun', red: 'Rot', orange: 'Orange', yellow: 'Gelb', green: 'Grün',
      cyan: 'Cyan', blue: 'Blau', violet: 'Violett', magenta: 'Magenta', gray: 'Grau'
    },
    shapes: {
      blob: 'Klecks', pebble: 'Kiesel', squircle: 'Quadratkreis', tablet: 'Kapsel',
      wedge: 'Keil', hex: 'Hexagon', cloud: 'Wolke', teardrop: 'Träne'
    }
  }
}

export function stringsFor(locale: string): GrokStrings {
  const exact = CATALOG[locale as GrokLocale]
  if (exact !== undefined) return exact
  const base = locale.split('-')[0]
  const match = GROK_LOCALES.find((candidate) => candidate.split('-')[0] === base)
  return match !== undefined ? CATALOG[match] : CATALOG.en
}

export function translationsOf(key: GrokTextKey): Record<string, string> {
  return Object.fromEntries(GROK_LOCALES.map((locale) => [locale, CATALOG[locale][key]]))
}
