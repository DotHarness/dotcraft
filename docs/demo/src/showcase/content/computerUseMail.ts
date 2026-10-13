import type { ComputerUseScript } from './computerUse'
import type { Localized } from './types'

const TARGET = 'Microsoft.OutlookForWindows_8wekyb3d8bbwe!Microsoft.OutlookforWindows'

function quote(body: string): string {
  return body.split('\n').map((line) => `> ${line}`).join('\n')
}

function script(text: {
  title: string
  prompt: string
  labels: Record<string, string>
  body: string
  tasks: string[]
  steps: { find: string; open: string; write: string; check: string; send: string }
  question: (quoted: string) => string
  yes: { label: string; message: string }
  no: { label: string; message: string; reply: string }
  reply: string
  declined: string
}): ComputerUseScript {
  return {
    title: text.title,
    prompt: text.prompt,
    app: { kind: 'mail', name: 'Outlook', target: TARGET, window: 'Outlook', approval: false },
    labels: text.labels,
    values: {},
    tasks: text.tasks,
    findStep: text.steps.find,
    actions: [
      { step: text.steps.open },
      { click: 'message.dana' },
      { set: { open: 'dana' } },
      { step: text.steps.write },
      { click: 'reply' },
      { set: { compose: '1' } },
      { click: 'body' },
      { type: 'body', text: text.body },
      { done: 0 },
      { step: text.steps.check },
      { move: 'send' },
      { pause: 600 }
    ],
    reply: text.reply,
    declined: text.declined,
    confirm: {
      question: text.question(quote(text.body)),
      yes: text.yes,
      no: text.no,
      actions: [
        { step: text.steps.send },
        { click: 'send' },
        { set: { sent: '1', compose: '' } },
        { done: 1 },
        { pause: 400 }
      ],
      reply: text.reply
    }
  }
}

export const mailExample: Localized<ComputerUseScript> = {
  en: script({
    title: 'Reply to Dana',
    prompt: 'In Outlook, reply to Dana’s “Design review” email and tell her Thursday at 3 PM works for me.',
    labels: {
      inbox: 'Inbox',
      danaFrom: 'Dana Kim',
      danaSubject: 'Design review',
      danaPreview: 'Could we go over the new onboarding screens this week? Thursday at 3 PM or Friday morning?',
      otherFrom: 'Build Bot',
      otherSubject: 'Nightly build passed',
      otherPreview: 'All 214 checks passed on main.',
      reply: 'Reply',
      to: 'To',
      subject: 'RE: Design review',
      send: 'Send',
      sent: 'Reply sent to Dana Kim',
      pick: 'Select a message to read it.'
    },
    body: 'Hi Dana,\nThursday at 3 PM works for me. See you then!',
    tasks: ['Write the reply to Dana', 'Send the reply'],
    steps: {
      find: 'Finding the Outlook window',
      open: 'Opening Dana’s email',
      write: 'Writing the reply',
      check: 'Checking the draft',
      send: 'Sending the reply'
    },
    question: (quoted) => `Your reply to Dana is ready in Outlook:\n\n${quoted}\n\nShould I send it?`,
    yes: { label: 'Yes, send it', message: 'Yes, send it.' },
    no: { label: 'Keep it as a draft', message: 'Not yet, keep it as a draft.', reply: 'Okay, nothing was sent. The reply stays open in Outlook as a draft.' },
    reply: 'Sent. Your reply to Dana is in Outlook’s Sent Items.',
    declined: ''
  }),
  zh: script({
    title: '回复 Dana',
    prompt: '在 Outlook 里回复 Dana 那封“设计评审”的邮件，告诉她周四下午 3 点我可以。',
    labels: {
      inbox: '收件箱',
      danaFrom: 'Dana Kim',
      danaSubject: '设计评审',
      danaPreview: '这周能一起过一下新的引导页吗？周四下午 3 点或者周五上午？',
      otherFrom: 'Build Bot',
      otherSubject: '夜间构建通过',
      otherPreview: 'main 分支 214 项检查全部通过。',
      reply: '答复',
      to: '收件人',
      subject: '答复: 设计评审',
      send: '发送',
      sent: '已回复 Dana Kim',
      pick: '选择一封邮件查看。'
    },
    body: 'Dana 你好，\n周四下午 3 点我可以，到时见！',
    tasks: ['写好给 Dana 的回复', '发送回复'],
    steps: {
      find: '查找 Outlook 窗口',
      open: '打开 Dana 的邮件',
      write: '撰写回复',
      check: '检查草稿',
      send: '发送回复'
    },
    question: (quoted) => `给 Dana 的回复已经在 Outlook 里写好：\n\n${quoted}\n\n要我现在发送吗？`,
    yes: { label: '发送吧', message: '发送吧。' },
    no: { label: '先存为草稿', message: '先别发，留作草稿。', reply: '好的，没有发送。回复会作为草稿留在 Outlook 里。' },
    reply: '已发送。给 Dana 的回复在 Outlook 的“已发送邮件”里。',
    declined: ''
  })
}
