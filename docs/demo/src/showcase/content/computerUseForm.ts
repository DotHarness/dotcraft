import type { ComputerUseScript } from './computerUse'
import type { Localized } from './types'

const TARGET = 'C:\\Program Files\\Contoso\\Expenses\\ContosoExpenses.exe'

function script(text: {
  title: string
  prompt: string
  labels: Record<string, string>
  values: Record<string, string>
  entry: { date: string; merchant: string; amount: string; category: string }
  tasks: string[]
  steps: { find: string; open: string; fill: string; save: string }
  reply: string
  declined: string
}): ComputerUseScript {
  return {
    title: text.title,
    prompt: text.prompt,
    app: { kind: 'form', name: 'Contoso Expenses', target: TARGET, window: 'Contoso Expenses', approval: true },
    labels: text.labels,
    values: text.values,
    tasks: text.tasks,
    findStep: text.steps.find,
    actions: [
      { step: text.steps.open },
      { click: 'new' },
      { set: { form: '1' } },
      { step: text.steps.fill },
      { click: 'date' },
      { type: 'date', text: text.entry.date },
      { click: 'merchant' },
      { type: 'merchant', text: text.entry.merchant },
      { click: 'amount' },
      { type: 'amount', text: text.entry.amount },
      { click: 'category' },
      { set: { 'category.open': '1' } },
      { click: 'option.travel' },
      { set: { category: text.entry.category, 'category.open': '' } },
      { done: 0 },
      { step: text.steps.save },
      { click: 'save' },
      { set: { saved: '1' } },
      { done: 1 },
      { pause: 400 }
    ],
    reply: text.reply,
    declined: text.declined
  }
}

export const formExample: Localized<ComputerUseScript> = {
  en: script({
    title: 'Log a taxi expense',
    prompt: 'Add yesterday’s taxi ride to Contoso Expenses: Mar 14, Metro Cab, amount 38.60, category Travel. Save it as a draft.',
    labels: {
      nav: 'My expenses',
      reports: 'Reports',
      new: 'New expense',
      heading: 'New expense',
      date: 'Date',
      merchant: 'Merchant',
      amount: 'Amount ($)',
      category: 'Category',
      choose: 'Choose…',
      travel: 'Travel',
      meals: 'Meals',
      office: 'Office',
      save: 'Save draft',
      submit: 'Submit',
      saved: 'Draft saved',
      empty: 'Select New expense to add one.'
    },
    values: { 'row1.title': 'Office chairs', 'row1.meta': 'Mar 9 · $418.50 · Submitted', 'row2.title': 'Train tickets', 'row2.meta': 'Mar 3 · $212.00 · Approved' },
    entry: { date: 'Mar 14, 2026', merchant: 'Metro Cab', amount: '38.60', category: 'Travel' },
    tasks: ['Fill in the date, merchant, amount, and category', 'Save the expense as a draft'],
    steps: {
      find: 'Finding the Contoso Expenses window',
      open: 'Opening a new expense',
      fill: 'Filling in the details',
      save: 'Saving it as a draft'
    },
    reply: [
      'Added to Contoso Expenses and saved as a draft:',
      '',
      '- **Date:** Mar 14, 2026',
      '- **Merchant:** Metro Cab',
      '- **Amount:** 38.60',
      '- **Category:** Travel',
      '',
      'It isn’t submitted yet. Submit it from Contoso Expenses when you’re ready.'
    ].join('\n'),
    declined: 'You declined access to Contoso Expenses, so I didn’t add anything. The details are Mar 14, Metro Cab, 38.60, Travel, ready for you to enter yourself.'
  }),
  zh: script({
    title: '登记一笔打车费',
    prompt: '在 Contoso Expenses 里登记昨天的打车费：3月14日，Metro Cab，68.50 元，类别选差旅，存成草稿。',
    labels: {
      nav: '我的报销',
      reports: '报表',
      new: '新建报销',
      heading: '新建报销',
      date: '日期',
      merchant: '商户',
      amount: '金额（元）',
      category: '类别',
      choose: '请选择…',
      travel: '差旅',
      meals: '餐饮',
      office: '办公',
      save: '存为草稿',
      submit: '提交',
      saved: '草稿已保存',
      empty: '点击“新建报销”添加一笔。'
    },
    values: { 'row1.title': '办公椅', 'row1.meta': '3月9日 · 2,390.00 元 · 已提交', 'row2.title': '高铁票', 'row2.meta': '3月3日 · 1,106.00 元 · 已通过' },
    entry: { date: '2026-03-14', merchant: 'Metro Cab', amount: '68.50', category: '差旅' },
    tasks: ['填写日期、商户、金额和类别', '把这笔报销存为草稿'],
    steps: {
      find: '查找 Contoso Expenses 窗口',
      open: '新建一笔报销',
      fill: '填写报销信息',
      save: '存为草稿'
    },
    reply: [
      '已在 Contoso Expenses 里登记，并存为草稿：',
      '',
      '- **日期：** 2026-03-14',
      '- **商户：** Metro Cab',
      '- **金额：** 68.50 元',
      '- **类别：** 差旅',
      '',
      '这笔还没提交，准备好后在 Contoso Expenses 里提交即可。'
    ].join('\n'),
    declined: '你拒绝了 Contoso Expenses 的使用权限，所以我没有登记。信息是 3月14日、Metro Cab、68.50 元、差旅，你可以自己填进去。'
  })
}
