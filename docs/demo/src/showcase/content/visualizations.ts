import { visualizationReference, type Exchange, type Lang, type Localized } from './types'
import { energyFragment, moonFragment, timerFragment } from './visualizationFragments'

interface VisualizationExample {
  file: string
  exchange: Localized<Exchange>
  fragment: Localized<string>
}

const energyTitle = { en: 'Electricity use by month', zh: '每月用电量' }
const moonTitle = { en: 'Phases of the Moon', zh: '月相变化' }
const timerTitle = { en: 'Focus timer designs', zh: '专注计时器设计稿' }

export const visualizationExamples: Record<string, VisualizationExample> = {
  energy: {
    file: 'electricity-use.html',
    exchange: {
      en: {
        prompt: 'How did our electricity use this year compare with last year?',
        answer: [
          'You used **4,000 kWh** this year, 8% less than the 4,350 kWh you used last year. Most of the savings came in the winter months.',
          visualizationReference('electricity-use.html', energyTitle.en),
          'Hover or tap a point for the exact month, hide a year from the legend, or switch to cost.'
        ].join('\n')
      },
      zh: {
        prompt: '今年的用电量和去年比怎么样？',
        answer: [
          '今年一共用电 **4,000 度**，比去年的 4,350 度少了 8%，省下的电主要集中在冬季几个月。',
          visualizationReference('electricity-use.html', energyTitle.zh),
          '悬停或点按数据点可以查看具体月份，点图例可以隐藏某一年，也可以切换成电费。'
        ].join('\n')
      }
    },
    fragment: {
      en: energyFragment({
        title: energyTitle.en, usage: 'Usage', cost: 'Cost', thisYear: 'This year', lastYear: 'Last year',
        months: ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'], unit: 'kWh', symbol: '$', rate: 0.28,
        totalThis: 'This year', totalLast: 'Last year', change: 'Change', chartLabel: 'Monthly electricity use for this year and last year'
      }),
      zh: energyFragment({
        title: energyTitle.zh, usage: '用电量', cost: '电费', thisYear: '今年', lastYear: '去年',
        months: ['1月', '2月', '3月', '4月', '5月', '6月', '7月', '8月', '9月', '10月', '11月', '12月'], unit: '度', symbol: '¥', rate: 0.56,
        totalThis: '今年合计', totalLast: '去年合计', change: '变化', chartLabel: '今年和去年每月的用电量'
      })
    }
  },
  moon: {
    file: 'moon-phases.html',
    exchange: {
      en: {
        prompt: 'Why does the Moon have phases?',
        answer: [
          'The Sun always lights half of the Moon. As the Moon circles Earth about every 29.5 days, we see more or less of that lit half, so it seems to grow from a thin crescent to full and shrink back again.',
          visualizationReference('moon-phases.html', moonTitle.en),
          'Drag the slider or press Play to move the Moon around its orbit.'
        ].join('\n')
      },
      zh: {
        prompt: '月亮为什么会有阴晴圆缺？',
        answer: [
          '太阳始终照亮月球的一半。月球大约每 29.5 天绕地球转一圈，从地球上看到的亮面时多时少，于是月亮从细细的月牙变成满月，再慢慢缩回去。',
          visualizationReference('moon-phases.html', moonTitle.zh),
          '拖动滑块或点播放，让月球沿轨道走一圈。'
        ].join('\n')
      }
    },
    fragment: {
      en: moonFragment({
        title: moonTitle.en, topView: 'Seen from above the North Pole', skyView: 'Seen from Earth', sun: 'Sunlight', earth: 'Earth',
        day: 'Day {n}', play: 'Play', pause: 'Pause', lit: '{percent}% lit',
        phases: ['New moon', 'Waxing crescent', 'First quarter', 'Waxing gibbous', 'Full moon', 'Waning gibbous', 'Last quarter', 'Waning crescent'],
        topLabel: 'The Moon orbiting Earth with sunlight coming from the left', skyLabel: 'How the Moon looks from Earth on the selected day'
      }),
      zh: moonFragment({
        title: moonTitle.zh, topView: '从北极上方俯视', skyView: '从地球上看', sun: '阳光', earth: '地球',
        day: '第 {n} 天', play: '播放', pause: '暂停', lit: '被照亮 {percent}%',
        phases: ['新月', '娥眉月', '上弦月', '盈凸月', '满月', '亏凸月', '下弦月', '残月'],
        topLabel: '月球绕地球运行，阳光从左侧照来', skyLabel: '所选日期从地球上看到的月亮'
      })
    }
  },
  timer: {
    file: 'focus-timer.html',
    exchange: {
      en: {
        prompt: 'Mock up a focus timer widget for my app. Show me a few directions.',
        answer: [
          'Here are three directions: a minimal card, a progress ring, and a compact bar. Each one works, so start it to watch it count down.',
          visualizationReference('focus-timer.html', timerTitle.en),
          'Use the arrows under the preview to switch designs, and open the design controls to fine-tune the one you like.'
        ].join('\n')
      },
      zh: {
        prompt: '给我的 App 设计一个专注计时器小组件，多给几个方向。',
        answer: [
          '这里有三个方向：极简卡片、进度环和紧凑条。每个都能用，点开始就会倒计时。',
          visualizationReference('focus-timer.html', timerTitle.zh),
          '用预览下方的箭头切换方案，打开设计控件可以微调你喜欢的那一版。'
        ].join('\n')
      }
    },
    fragment: {
      en: timerFragment({
        designs: timerTitle.en, minimal: 'Minimal', ring: 'Ring', compact: 'Compact', focus: 'Focus', deepWork: 'Deep work',
        session: 'Session 2 of 4 · break at 11:30', start: 'Start', pause: 'Pause', resume: 'Resume', reset: 'Reset', accent: 'Accent',
        digits: 'Digit size', radius: 'Corner radius', thickness: 'Ring thickness', details: 'Session details', layout: 'Layout',
        inline: 'Inline', stacked: 'Stacked'
      }),
      zh: timerFragment({
        designs: timerTitle.zh, minimal: '极简', ring: '进度环', compact: '紧凑', focus: '专注', deepWork: '深度工作',
        session: '第 2 / 4 轮 · 11:30 休息', start: '开始', pause: '暂停', resume: '继续', reset: '重置', accent: '强调色',
        digits: '数字大小', radius: '圆角', thickness: '环宽', details: '轮次信息', layout: '布局',
        inline: '横排', stacked: '竖排'
      })
    }
  }
}

export function visualizationFragment(path: string, lang: Lang): string | null {
  const example = Object.values(visualizationExamples).find((candidate) => path.endsWith(`/${candidate.file}`))
  return example ? example.fragment[lang] : null
}
