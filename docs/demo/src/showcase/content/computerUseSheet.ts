import type { ComputerUseScript } from './computerUse'
import type { Lang, Localized } from './types'

type Row = [date: string, item: string, amount: string]

function rows(list: Row[]): Record<string, string> {
  const values: Record<string, string> = {}
  list.forEach(([date, item, amount], index) => {
    values[`A${index + 2}`] = date
    values[`B${index + 2}`] = item
    values[`C${index + 2}`] = amount
  })
  return values
}

const DATA: Record<Lang, { header: Row; unsorted: Row[]; sorted: Row[]; total: string }> = {
  en: {
    header: ['Date', 'Item', 'Amount'],
    unsorted: [
      ['Mar 18', 'Team lunch', '86.40'],
      ['Mar 3', 'Train tickets', '212.00'],
      ['Mar 27', 'Software license', '499.00'],
      ['Mar 9', 'Office chairs', '418.50'],
      ['Mar 14', 'Taxi', '38.60']
    ],
    sorted: [
      ['Mar 3', 'Train tickets', '212.00'],
      ['Mar 9', 'Office chairs', '418.50'],
      ['Mar 14', 'Taxi', '38.60'],
      ['Mar 18', 'Team lunch', '86.40'],
      ['Mar 27', 'Software license', '499.00']
    ],
    total: '1,254.50'
  },
  zh: {
    header: ['日期', '项目', '金额'],
    unsorted: [
      ['3月18日', '团队午餐', '586.00'],
      ['3月3日', '高铁票', '1,106.00'],
      ['3月27日', '软件授权', '3,480.00'],
      ['3月9日', '办公椅', '2,390.00'],
      ['3月14日', '出租车', '68.50']
    ],
    sorted: [
      ['3月3日', '高铁票', '1,106.00'],
      ['3月9日', '办公椅', '2,390.00'],
      ['3月14日', '出租车', '68.50'],
      ['3月18日', '团队午餐', '586.00'],
      ['3月27日', '软件授权', '3,480.00']
    ],
    total: '7,630.50'
  }
}

function script(lang: Lang, text: {
  title: string
  prompt: string
  window: string
  sort: string
  bold: string
  totalLabel: string
  tasks: string[]
  steps: { find: string; read: string; sort: string; total: string; bold: string; check: string }
  reply: string
  declined: string
}): ComputerUseScript {
  const data = DATA[lang]
  const [a1, b1, c1] = data.header
  return {
    title: text.title,
    prompt: text.prompt,
    app: {
      kind: 'sheet',
      name: 'Excel',
      target: 'C:\\Program Files\\Microsoft Office\\root\\Office16\\EXCEL.EXE',
      window: text.window,
      approval: true
    },
    labels: { sort: text.sort, bold: text.bold },
    values: { A1: a1, B1: b1, C1: c1, ...rows(data.unsorted) },
    tasks: text.tasks,
    findStep: text.steps.find,
    actions: [
      { step: text.steps.read },
      { move: 'A2' },
      { move: 'C6' },
      { step: text.steps.sort },
      { click: 'A1' },
      { click: 'sort' },
      { set: rows(data.sorted) },
      { done: 0 },
      { step: text.steps.total },
      { click: 'B7' },
      { type: 'B7', text: text.totalLabel },
      { click: 'C7' },
      { type: 'C7', text: '=SUM(C2:C6)' },
      { set: { C7: data.total, 'C7.formula': '=SUM(C2:C6)' } },
      { done: 1 },
      { step: text.steps.bold },
      { click: 'row1' },
      { click: 'bold' },
      { set: { bold: '1' } },
      { done: 2 },
      { step: text.steps.check },
      { move: 'C7' },
      { pause: 500 }
    ],
    reply: text.reply,
    declined: text.declined
  }
}

export const sheetExample: Localized<ComputerUseScript> = {
  en: script('en', {
    title: 'Tidy the expenses sheet',
    prompt: 'In the expenses sheet open in Excel, sort the rows by date, add a total under Amount, and make the header row bold.',
    window: 'march-expenses.xlsx - Excel',
    sort: 'Sort A→Z',
    bold: 'Bold',
    totalLabel: 'Total',
    tasks: ['Sort the rows by date', 'Add the total under Amount', 'Make the header row bold'],
    steps: {
      find: 'Finding the Excel window',
      read: 'Reading the sheet',
      sort: 'Sorting rows 2–6 by date',
      total: 'Adding the total',
      bold: 'Making the header row bold',
      check: 'Checking the result'
    },
    reply: [
      'Done. In **march-expenses.xlsx**:',
      '',
      '- Rows 2–6 are sorted by date, from Mar 3 to Mar 27.',
      `- C7 adds up the Amount column: **${DATA.en.total}**.`,
      '- The header row is bold.',
      '',
      'I didn’t save the file. Press Ctrl+S in Excel once it looks right.'
    ].join('\n'),
    declined: 'You declined access to Excel, so I left the sheet as it was. If you tell me where **march-expenses.xlsx** is saved, I can tidy the file directly instead.'
  }),
  zh: script('zh', {
    title: '整理报销表',
    prompt: '把 Excel 里打开的报销表按日期排序，在金额下面加一行合计，再把表头加粗。',
    window: '3月报销.xlsx - Excel',
    sort: '升序排序',
    bold: '加粗',
    totalLabel: '合计',
    tasks: ['按日期排序', '在金额下面加合计', '把表头加粗'],
    steps: {
      find: '查找 Excel 窗口',
      read: '读取表格内容',
      sort: '按日期排序第 2–6 行',
      total: '添加合计',
      bold: '加粗表头',
      check: '检查结果'
    },
    reply: [
      '整理好了。**3月报销.xlsx** 现在是这样：',
      '',
      '- 第 2–6 行已按日期排序，从 3月3日 到 3月27日。',
      `- C7 是金额列的合计：**${DATA.zh.total}**。`,
      '- 表头已加粗。',
      '',
      '文件还没保存，确认无误后在 Excel 里按 Ctrl+S 即可。'
    ].join('\n'),
    declined: '你拒绝了 Excel 的使用权限，所以表格保持原样。告诉我 **3月报销.xlsx** 保存在哪里，我可以直接整理这个文件。'
  })
}
