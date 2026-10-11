import { uiBlock, type Exchange, type Localized } from './types'

const BILL_SPLITTER = `
function BillSplitter() {
  const [bill, setBill] = DotCraft.useState(T.bill)
  const [tip, setTip] = DotCraft.useState(T.tip)
  const [people, setPeople] = DotCraft.useState(T.people)
  const compact = DotCraft.useBreakpoint() === 'compact'
  const Summary = compact ? Col : Row
  const tipAmount = bill * Number(tip) / 100
  const total = bill + tipAmount
  const each = total / people
  const money = value => T.symbol + value.toFixed(2)

  return (
    <Card title={T.title} subtitle={T.subtitle}>
      <Slider label={T.billLabel} min={0} max={T.billMax} step={1} value={bill} onChange={setBill} valueText={money(bill)} />
      <SegmentedControl label={T.tipLabel} value={tip} onChange={setTip}
        options={T.tips.map(value => ({ value, label: value + '%' }))} />
      <Slider label={T.peopleLabel} min={1} max={12} value={people} onChange={setPeople} valueText={people + T.peopleUnit} />
      <Divider />
      <Summary gap={16} align={compact ? 'stretch' : 'center'}>
        <Col gap={2} flex={compact ? undefined : 1}>
          <Caption>{T.eachLabel}</Caption>
          <Text size="2xl" weight="semibold" tabular>{money(each)}</Text>
          <Caption>{T.totalText.replace('{total}', money(total)).replace('{tip}', money(tipAmount))}</Caption>
        </Col>
        <Box minWidth={200}>
          <PieChart donut height={120} format="currency" currency={T.currency} showLegend
            data={[{ label: T.billLabel, value: bill }, { label: T.tipLabel, value: Math.round(tipAmount * 100) / 100 }]} />
        </Box>
      </Summary>
      <Row>
        <Button icon="copy" label={T.copy} onClick={() => DotCraft.copy(T.copyText.replace('{each}', money(each)).replace('{people}', String(people)))} />
      </Row>
    </Card>
  )
}

DotCraft.render(<BillSplitter />)`

const LOAN = `
function Loan() {
  const [amount, setAmount] = DotCraft.useState(T.amount)
  const [rate, setRate] = DotCraft.useState(T.rate)
  const [term, setTerm] = DotCraft.useState('30')
  const compact = DotCraft.useBreakpoint() === 'compact'
  const months = Number(term) * 12
  const monthlyRate = rate / 100 / 12
  const payment = monthlyRate === 0 ? amount / months : amount * monthlyRate / (1 - Math.pow(1 + monthlyRate, -months))
  const balances = DotCraft.useMemo(() => {
    const points = []
    let balance = amount
    for (let year = 1; year <= Number(term); year++) {
      for (let month = 0; month < 12; month++) balance -= payment - balance * monthlyRate
      points.push({ year: String(year), balance: Math.max(0, Math.round(balance)) })
    }
    return points
  }, [amount, rate, term])
  const money = value => T.symbol + Math.round(value).toLocaleString(T.locale)
  const summary = T.summary.replace('{amount}', money(amount)).replace('{rate}', String(rate)).replace('{term}', term).replace('{payment}', money(payment))

  return (
    <Card title={T.title}>
      <Slider label={T.amountLabel} min={T.amountMin} max={T.amountMax} step={T.amountStep} value={amount} onChange={setAmount} valueText={money(amount)} />
      <Slider label={T.rateLabel} min={0} max={10} step={0.05} value={rate} onChange={setRate} valueText={rate.toFixed(2) + '%'} />
      <SegmentedControl label={T.termLabel} value={term} onChange={setTerm}
        options={[{ value: '15', label: T.years.replace('{n}', '15') }, { value: '30', label: T.years.replace('{n}', '30') }]} />
      <Row gap={compact ? 12 : 24} wrap>
        <Col gap={2}>
          <Caption>{T.perMonth}</Caption>
          <Text size="xl" weight="semibold" tabular>{money(payment)}</Text>
        </Col>
        <Col gap={2}>
          <Caption>{T.interest}</Caption>
          <Text size="xl" weight="semibold" tabular>{money(payment * months - amount)}</Text>
        </Col>
      </Row>
      <Chart type="area" data={balances} xKey="year" series={[{ key: 'balance', label: T.balance }]}
        format="currency" currency={T.currency} height={180} />
      <Row wrap>
        <Button icon="copy" label={T.copy} onClick={() => DotCraft.copy(summary)} />
        <Button variant="ghost" label={T.ask} onClick={() => DotCraft.sendMessage(T.askText.replace('{summary}', summary))} />
      </Row>
    </Card>
  )
}

DotCraft.render(<Loan />)`

const UNIT_CONVERTER = `
const OPTIONS = Object.entries(T.units).map(([value, unit]) => ({ value, label: unit.label }))

function Converter() {
  const [amount, setAmount] = DotCraft.useState(5)
  const [from, setFrom] = DotCraft.useState('km')
  const [to, setTo] = DotCraft.useState('mi')
  const valid = typeof amount === 'number' && Number.isFinite(amount)
  const result = valid ? amount * T.units[from].factor / T.units[to].factor : null
  const text = result === null ? '—' : result.toLocaleString(T.locale, { maximumFractionDigits: 4 }) + ' ' + T.units[to].name
  const stacked = DotCraft.useBreakpoint() === 'compact'
  const Line = stacked ? Col : Row

  return (
    <Card title={T.title}>
      <Line gap={8} align={stacked ? 'stretch' : 'end'}>
        <Box flex={1}><Input label={T.amount} type="number" value={amount} onChange={setAmount} error={valid ? undefined : T.invalid} /></Box>
        <Box flex={1}><Select label={T.from} value={from} options={OPTIONS} onChange={setFrom} /></Box>
        <Button variant="ghost" icon="arrow-left-right" label={T.swap} onClick={() => { setFrom(to); setTo(from) }} />
        <Box flex={1}><Select label={T.to} value={to} options={OPTIONS} onChange={setTo} /></Box>
      </Line>
      <Box surface="muted" radius="md" padding={12} gap={2}>
        <Caption>{T.result}</Caption>
        <Row>
          <Text size="xl" weight="semibold" tabular>{text}</Text>
          <Spacer />
          <Button size="sm" variant="ghost" icon="copy" label={T.copy} disabled={result === null} onClick={() => DotCraft.copy(text)} />
        </Row>
      </Box>
    </Card>
  )
}

DotCraft.render(<Converter />)`

const QUIZ = `
function labelOf(question, value) {
  return question.options.find(option => option.value === value)?.label ?? T.nothing
}

function Quiz() {
  const [answers, setAnswers] = DotCraft.useState({})
  const [checked, setChecked] = DotCraft.useState(false)
  const score = T.questions.filter(q => answers[q.id] === q.answer).length

  return (
    <Card title={T.title}>
      {T.questions.map(q => (
        <Col key={q.id} gap={6}>
          <RadioGroup label={q.prompt} options={q.options} value={answers[q.id] ?? ''} disabled={checked}
            onChange={value => setAnswers(previous => ({ ...previous, [q.id]: value }))} />
          {checked && (
            <Row wrap>
              <Badge tone={answers[q.id] === q.answer ? 'success' : 'error'}
                label={answers[q.id] === q.answer ? T.correct : T.answer.replace('{answer}', labelOf(q, q.answer))} />
              <Button size="sm" variant="ghost" label={T.explain} onClick={() => DotCraft.sendMessage(
                T.explainText.replace('{question}', q.prompt).replace('{answer}', labelOf(q, q.answer)).replace('{mine}', labelOf(q, answers[q.id])))} />
            </Row>
          )}
        </Col>
      ))}
      <Row>
        {checked
          ? <Text tabular>{T.score.replace('{score}', String(score)).replace('{total}', String(T.questions.length))}</Text>
          : <Button variant="primary" label={T.check} disabled={Object.keys(answers).length < T.questions.length} onClick={() => setChecked(true)} />}
        <Spacer />
        {checked && <Button label={T.retry} onClick={() => { setAnswers({}); setChecked(false) }} />}
      </Row>
    </Card>
  )
}

DotCraft.render(<Quiz />)`

export const toolExamples: Record<string, Localized<Exchange>> = {
  'bill-splitter': {
    en: {
      prompt: 'Dinner for the three of us came to 126 dollars. How do we split it with an 18% tip?',
      answer: [
        'With an 18% tip the total is **$148.68**, so each of you pays **$49.56**.',
        '',
        uiBlock({
          title: 'Split the bill', subtitle: 'Friday dinner', bill: 126, billMax: 400, tip: '18', tips: ['10', '15', '18', '22'],
          people: 3, peopleUnit: ' people', billLabel: 'Bill', tipLabel: 'Tip', peopleLabel: 'People', eachLabel: 'Each person pays',
          totalText: '{total} total, including {tip} tip', currency: 'USD', symbol: '$', copy: 'Copy amount',
          copyText: 'Each of us pays {each} ({people} people, tip included).'
        }, BILL_SPLITTER),
        '',
        'Change the tip or the head count and the split updates.'
      ].join('\n')
    },
    zh: {
      prompt: '三个人吃饭花了 480 元，再加 10% 服务费，每人该付多少？',
      answer: [
        '加上 10% 服务费一共 **¥528.00**，每人付 **¥176.00**。',
        '',
        uiBlock({
          title: '分摊账单', subtitle: '周五晚饭', bill: 480, billMax: 1500, tip: '10', tips: ['0', '5', '10', '15'],
          people: 3, peopleUnit: ' 人', billLabel: '账单', tipLabel: '服务费', peopleLabel: '人数', eachLabel: '每人应付',
          totalText: '合计 {total}，含服务费 {tip}', currency: 'CNY', symbol: '¥', copy: '复制金额',
          copyText: '每人付 {each}（{people} 人，含服务费）。'
        }, BILL_SPLITTER),
        '',
        '改一下服务费比例或人数，分摊金额会跟着变。'
      ].join('\n')
    }
  },
  mortgage: {
    en: {
      prompt: 'What would the monthly payment be on a 400,000-dollar mortgage at 6.5% over 30 years?',
      answer: [
        'At $400,000 and 6.5% over 30 years, the payment is about **$2,528 a month**, with roughly $510,000 of interest over the loan.',
        '',
        uiBlock({
          title: 'Monthly payment', amount: 400000, amountMin: 50000, amountMax: 1500000, amountStep: 10000, rate: 6.5,
          amountLabel: 'Loan amount', rateLabel: 'Interest rate', termLabel: 'Term', years: '{n} years', perMonth: 'Per month',
          interest: 'Total interest', balance: 'Balance left', currency: 'USD', symbol: '$', locale: 'en-US', copy: 'Copy summary',
          summary: '{amount} at {rate}% over {term} years: {payment} a month.', ask: 'What if I pay extra?',
          askText: '{summary} How much sooner would I finish, and how much interest would I save, if I paid an extra $200 a month?'
        }, LOAN),
        '',
        'Move the sliders to see how the rate and term change the payment.'
      ].join('\n')
    },
    zh: {
      prompt: '贷款 100 万，年利率 3.1%，分 30 年还，每月要还多少？',
      answer: [
        '按等额本息，100 万元、年利率 3.1%、30 年，每月约还 **¥4,270**，整个贷款期的利息约 ¥53.7 万。',
        '',
        uiBlock({
          title: '月供计算', amount: 1000000, amountMin: 100000, amountMax: 5000000, amountStep: 50000, rate: 3.1,
          amountLabel: '贷款金额', rateLabel: '年利率', termLabel: '期限', years: '{n} 年', perMonth: '每月还款',
          interest: '利息总额', balance: '剩余本金', currency: 'CNY', symbol: '¥', locale: 'zh-CN', copy: '复制结果',
          summary: '贷款 {amount}，年利率 {rate}%，{term} 年：每月还 {payment}。', ask: '提前还款会怎样？',
          askText: '{summary}如果每月多还 2,000 元，能提前多久还清，省多少利息？'
        }, LOAN),
        '',
        '拖动滑块，看看利率和期限怎样影响月供。'
      ].join('\n')
    }
  },
  'unit-converter': {
    en: {
      prompt: 'How many miles is 5 kilometers?',
      answer: [
        '5 kilometers is about **3.107 miles**.',
        '',
        uiBlock({
          title: 'Unit converter', amount: 'Amount', from: 'From', to: 'To', swap: 'Swap', result: 'Result', invalid: 'Enter a number',
          copy: 'Copy', locale: 'en-US',
          units: {
            m: { label: 'Meters', name: 'meters', factor: 1 },
            km: { label: 'Kilometers', name: 'kilometers', factor: 1000 },
            ft: { label: 'Feet', name: 'feet', factor: 0.3048 },
            mi: { label: 'Miles', name: 'miles', factor: 1609.344 },
            in: { label: 'Inches', name: 'inches', factor: 0.0254 }
          }
        }, UNIT_CONVERTER),
        '',
        'Type another amount or pick different units.'
      ].join('\n')
    },
    zh: {
      prompt: '5 公里是多少英里？',
      answer: [
        '5 公里约等于 **3.107 英里**。',
        '',
        uiBlock({
          title: '单位换算', amount: '数值', from: '从', to: '换算为', swap: '互换', result: '结果', invalid: '请输入数字',
          copy: '复制', locale: 'zh-CN',
          units: {
            m: { label: '米', name: '米', factor: 1 },
            km: { label: '公里', name: '公里', factor: 1000 },
            ft: { label: '英尺', name: '英尺', factor: 0.3048 },
            mi: { label: '英里', name: '英里', factor: 1609.344 },
            in: { label: '英寸', name: '英寸', factor: 0.0254 }
          }
        }, UNIT_CONVERTER),
        '',
        '换个数值或单位，结果会立即更新。'
      ].join('\n')
    }
  },
  quiz: {
    en: {
      prompt: 'Quiz me on the solar system. Three quick questions.',
      answer: [
        'Here are three questions. Pick an answer for each, then check them.',
        '',
        uiBlock({
          title: 'Solar system quiz', nothing: 'nothing', correct: 'Correct', answer: 'Answer: {answer}', explain: 'Explain',
          explainText: 'Explain why the answer to "{question}" is {answer}. I answered {mine}.',
          score: '{score} of {total} correct', check: 'Check answers', retry: 'Try again',
          questions: [
            { id: 'year', prompt: 'Which planet has the shortest year?', answer: 'mercury',
              options: [{ value: 'venus', label: 'Venus' }, { value: 'mercury', label: 'Mercury' }, { value: 'mars', label: 'Mars' }] },
            { id: 'moon', prompt: 'What is the largest moon in the solar system?', answer: 'ganymede',
              options: [{ value: 'titan', label: 'Titan' }, { value: 'europa', label: 'Europa' }, { value: 'ganymede', label: 'Ganymede' }] },
            { id: 'tilt', prompt: 'Which planet spins on its side?', answer: 'uranus',
              options: [{ value: 'neptune', label: 'Neptune' }, { value: 'uranus', label: 'Uranus' }, { value: 'saturn', label: 'Saturn' }] }
          ]
        }, QUIZ),
        '',
        'Choose **Explain** on any answer to go deeper.'
      ].join('\n')
    },
    zh: {
      prompt: '出三道关于太阳系的小题考考我。',
      answer: [
        '一共三道题，每题选一个答案，选完再核对。',
        '',
        uiBlock({
          title: '太阳系小测验', nothing: '未作答', correct: '答对了', answer: '答案：{answer}', explain: '讲解',
          explainText: '讲讲为什么“{question}”的答案是{answer}。我选的是{mine}。',
          score: '答对 {score} / {total} 题', check: '核对答案', retry: '再做一次',
          questions: [
            { id: 'year', prompt: '哪颗行星的一年最短？', answer: 'mercury',
              options: [{ value: 'venus', label: '金星' }, { value: 'mercury', label: '水星' }, { value: 'mars', label: '火星' }] },
            { id: 'moon', prompt: '太阳系里最大的卫星是哪一颗？', answer: 'ganymede',
              options: [{ value: 'titan', label: '土卫六' }, { value: 'europa', label: '木卫二' }, { value: 'ganymede', label: '木卫三' }] },
            { id: 'tilt', prompt: '哪颗行星是“躺着”自转的？', answer: 'uranus',
              options: [{ value: 'neptune', label: '海王星' }, { value: 'uranus', label: '天王星' }, { value: 'saturn', label: '土星' }] }
          ]
        }, QUIZ),
        '',
        '在任意一题上选**讲解**，可以接着往下问。'
      ].join('\n')
    }
  }
}
