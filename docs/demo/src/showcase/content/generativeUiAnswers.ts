import { uiBlock, type ComparedExchange, type Localized } from './types'

const DINNER = `
function amountText(value, unit) {
  const big = unit === 'g' && value >= 1000
  const number = (big ? value / 1000 : value).toLocaleString(T.locale, { maximumFractionDigits: 2 })
  return (number + ' ' + T.units[big ? 'kg' : unit]).trim()
}

function DinnerPlanner() {
  const [guests, setGuests] = DotCraft.useState(6)
  const [vegetarians, setVegetarians] = DotCraft.useState(1)
  const [appetite, setAppetite] = DotCraft.useState('regular')
  const veg = Math.min(vegetarians, guests)
  const factor = { light: 0.8, regular: 1, hearty: 1.25 }[appetite]
  const rows = T.items.map(item => {
    const eaters = item.per === 'meat' ? guests - veg : item.per === 'veg' ? veg : guests
    const amount = Math.ceil(eaters * item.amount * factor / item.step - 1e-9) * item.step
    return { item: item.name, amount: amount > 0 ? amountText(amount, item.unit) : '—', cost: Math.round(amount * item.price * 100) / 100 }
  })
  const total = rows.reduce((sum, row) => sum + row.cost, 0)
  const list = rows.filter(row => row.cost > 0).map(row => '- ' + row.item + ': ' + row.amount).join('\\n')

  return (
    <Card title={T.title} subtitle={T.menu}>
      <Slider label={T.guests} min={2} max={12} value={guests} onChange={setGuests} valueText={guests + T.peopleUnit} />
      <Slider label={T.vegetarians} min={0} max={guests} value={veg} onChange={setVegetarians} valueText={veg + T.peopleUnit} />
      <SegmentedControl label={T.appetite} value={appetite} onChange={setAppetite} options={T.appetites} />
      <Table rowKey="item" rows={rows} columns={[
        { key: 'item', label: T.item },
        { key: 'amount', label: T.amount, align: 'end' },
        { key: 'cost', label: T.cost, align: 'end', format: 'currency', currency: T.currency }
      ]} />
      <Row wrap>
        <Col gap={2}>
          <Caption>{T.total}</Caption>
          <Text size="xl" weight="semibold" tabular>{T.symbol + total.toFixed(2)}</Text>
        </Col>
        <Spacer />
        <Button icon="copy" label={T.copy} onClick={() => DotCraft.copy(list)} />
        <Button variant="primary" label={T.plan} onClick={() => DotCraft.sendMessage(T.planText.replace('{guests}', String(guests)).replace('{veg}', String(veg)))} />
      </Row>
    </Card>
  )
}

DotCraft.render(<DinnerPlanner />)`

const PLANS = `
function Plans() {
  const [usage, setUsage] = DotCraft.useState(12)
  const [period, setPeriod] = DotCraft.useState('month')
  const months = period === 'year' ? 12 : 1
  const rows = T.plans.map(plan => {
    const extra = plan.included === null ? 0 : Math.max(0, usage - plan.included) * plan.extra
    return { plan: plan.name, data: plan.included === null ? T.unlimited : plan.included + ' GB', cost: (plan.price + extra) * months }
  })
  const best = rows.reduce((cheapest, row) => (row.cost < cheapest.cost ? row : cheapest))

  return (
    <Card title={T.title} subtitle={T.subtitle}>
      <Slider label={T.usage} min={1} max={40} value={usage} onChange={setUsage} valueText={usage + ' GB'} />
      <SegmentedControl label={T.period} value={period} onChange={setPeriod}
        options={[{ value: 'month', label: T.month }, { value: 'year', label: T.year }]} />
      <Table rowKey="plan" rows={rows} columns={[
        { key: 'plan', label: T.plan },
        { key: 'data', label: T.data },
        { key: 'cost', label: period === 'year' ? T.costYear : T.costMonth, align: 'end', format: 'currency', currency: T.currency }
      ]} />
      <Row wrap>
        <Badge tone="success" label={T.best.replace('{plan}', best.plan)} />
        <Spacer />
        <Button variant="ghost" icon="external-link" label={T.details} onClick={() => DotCraft.openUrl(T.url)} />
        <Button label={T.ask} onClick={() => DotCraft.sendMessage(T.askText.replace('{usage}', String(usage)).replace('{plan}', best.plan))} />
      </Row>
    </Card>
  )
}

DotCraft.render(<Plans />)`

const TRIP = `
function TripBudget() {
  const [days, setDays] = DotCraft.useState(5)
  const [people, setPeople] = DotCraft.useState(2)
  const [stay, setStay] = DotCraft.useState('mid')
  const [flight, setFlight] = DotCraft.useState(T.flight)
  const compact = DotCraft.useBreakpoint() === 'compact'
  const Layout = compact ? Col : Row
  const fare = typeof flight === 'number' && Number.isFinite(flight) ? flight : 0
  const parts = [
    { label: T.parts.stay, value: (days - 1) * Math.ceil(people / 2) * T.stays[stay] },
    { label: T.parts.food, value: days * people * T.daily.food },
    { label: T.parts.transport, value: days * people * T.daily.transport },
    { label: T.parts.activities, value: days * people * T.daily.activities },
    { label: T.parts.flights, value: people * fare }
  ]
  const total = parts.reduce((sum, part) => sum + part.value, 0)
  const money = value => T.symbol + Math.round(value).toLocaleString(T.locale)
  const summary = parts.map(part => part.label + ': ' + money(part.value)).concat(T.total + ': ' + money(total)).join('\\n')

  return (
    <Card title={T.title}>
      <Layout gap={16} align="stretch">
        <Col flex={1}>
          <Slider label={T.days} min={2} max={14} value={days} onChange={setDays} valueText={days + T.dayUnit} />
          <Slider label={T.people} min={1} max={6} value={people} onChange={setPeople} valueText={people + T.peopleUnit} />
          <SegmentedControl label={T.stay} value={stay} onChange={setStay} options={T.stayOptions} />
          <Input label={T.flightLabel} type="number" value={flight} onChange={setFlight} hint={T.flightHint} />
        </Col>
        <Col flex={1} gap={4}>
          <Caption>{T.total}</Caption>
          <Text size="2xl" weight="semibold" tabular>{money(total)}</Text>
          <Caption>{T.each.replace('{amount}', money(total / people))}</Caption>
          <PieChart donut height={160} format="currency" currency={T.currency} data={parts} />
        </Col>
      </Layout>
      <Row wrap>
        <Button icon="copy" label={T.copy} onClick={() => DotCraft.copy(summary)} />
        <Button variant="ghost" icon="pencil" label={T.edit}
          onClick={() => DotCraft.prefillComposer(T.editText.replace('{days}', String(days)).replace('{people}', String(people)))} />
      </Row>
    </Card>
  )
}

DotCraft.render(<TripBudget />)`

export const comparedExamples: Record<string, Localized<ComparedExchange>> = {
  dinner: {
    en: {
      prompt: 'I’m cooking dinner for six friends on Saturday, and one of them is vegetarian. What should I make, and how much do I need to buy?',
      plain: [
        'A chicken traybake keeps the evening easy, with a lasagne for your vegetarian friend:',
        '',
        '- **Main:** lemon-herb chicken thighs with roast potatoes, plus a mushroom lasagne portion',
        '- **Sides:** a big green salad and fresh baguettes',
        '- **Dessert:** ice cream with berries',
        '',
        '| Item | Amount |',
        '|---|---|',
        '| Chicken thighs | 1.25 kg |',
        '| Vegetarian lasagne | 1 portion |',
        '| Potatoes | 1.5 kg |',
        '| Salad greens | 360 g |',
        '| Baguettes | 2 |',
        '| Ice cream | 1 L |',
        '',
        'Expect to spend about **$44**. Put the chicken and potatoes in the oven 50 minutes before you want to eat.'
      ].join('\n'),
      interactive: [
        'Make a lemon-herb chicken traybake with roast potatoes, a mushroom lasagne portion for your vegetarian friend, a green salad, baguettes, and ice cream. For six people the shopping comes to about **$44**.',
        '',
        uiBlock({
          title: 'Saturday dinner', menu: 'Chicken traybake · veggie lasagne · salad · ice cream', guests: 'Guests', vegetarians: 'Vegetarians',
          peopleUnit: '', appetite: 'Appetite',
          appetites: [{ value: 'light', label: 'Light' }, { value: 'regular', label: 'Regular' }, { value: 'hearty', label: 'Hearty' }],
          item: 'Item', amount: 'Amount', cost: 'Cost', total: 'Estimated cost', currency: 'USD', symbol: '$', locale: 'en-US',
          copy: 'Copy shopping list', plan: 'Plan the timing',
          planText: 'I’m cooking a chicken traybake dinner for {guests} people, {veg} of them vegetarian. Give me a cooking timeline that has everything ready at 7 pm.',
          units: { g: 'g', kg: 'kg', each: '', L: 'L' },
          items: [
            { name: 'Chicken thighs', per: 'meat', amount: 250, unit: 'g', step: 50, price: 0.011 },
            { name: 'Vegetarian lasagne', per: 'veg', amount: 1, unit: 'each', step: 1, price: 5.5 },
            { name: 'Potatoes', per: 'all', amount: 250, unit: 'g', step: 250, price: 0.003 },
            { name: 'Salad greens', per: 'all', amount: 60, unit: 'g', step: 20, price: 0.02 },
            { name: 'Baguettes', per: 'all', amount: 1 / 3, unit: 'each', step: 1, price: 3 },
            { name: 'Ice cream', per: 'all', amount: 0.15, unit: 'L', step: 0.5, price: 7 }
          ]
        }, DINNER),
        '',
        'Change the guest count or appetite and the list rescales.'
      ].join('\n')
    },
    zh: {
      prompt: '周六请六个朋友来家里吃饭，其中一位吃素。做什么菜好，要买多少？',
      plain: [
        '做几道家常菜就够热闹，素食的朋友也有得吃：',
        '',
        '- **荤菜**：可乐鸡翅',
        '- **素菜**：香菇烧豆腐、番茄炒蛋、蒜蓉西兰花',
        '- **主食和水果**：米饭、西瓜',
        '',
        '| 食材 | 用量 |',
        '|---|---|',
        '| 鸡翅中 | 1 千克 |',
        '| 嫩豆腐 | 3 盒 |',
        '| 鸡蛋 | 9 个 |',
        '| 番茄 | 750 克 |',
        '| 西兰花 | 750 克 |',
        '| 大米 | 600 克 |',
        '| 西瓜 | 1 个 |',
        '',
        '预计花费约 **¥110**。鸡翅最费时间，开饭前 40 分钟先做。'
      ].join('\n'),
      interactive: [
        '做可乐鸡翅、香菇烧豆腐、番茄炒蛋和蒜蓉西兰花，配米饭和西瓜，素食的朋友有三道菜可选。六个人的食材约 **¥110**。',
        '',
        uiBlock({
          title: '周六家宴', menu: '可乐鸡翅 · 香菇烧豆腐 · 番茄炒蛋 · 蒜蓉西兰花', guests: '人数', vegetarians: '吃素的人数',
          peopleUnit: ' 人', appetite: '饭量',
          appetites: [{ value: 'light', label: '小' }, { value: 'regular', label: '正常' }, { value: 'hearty', label: '大' }],
          item: '食材', amount: '用量', cost: '花费', total: '预计花费', currency: 'CNY', symbol: '¥', locale: 'zh-CN',
          copy: '复制购物清单', plan: '安排做菜顺序',
          planText: '我要给 {guests} 个人做可乐鸡翅、香菇烧豆腐、番茄炒蛋和蒜蓉西兰花，其中 {veg} 人吃素。帮我排一个晚上 7 点准时开饭的做菜顺序。',
          units: { g: '克', kg: '千克', each: '个', box: '盒' },
          items: [
            { name: '鸡翅中', per: 'meat', amount: 200, unit: 'g', step: 50, price: 0.042 },
            { name: '嫩豆腐', per: 'all', amount: 0.5, unit: 'box', step: 1, price: 4 },
            { name: '鸡蛋', per: 'all', amount: 1.5, unit: 'each', step: 1, price: 0.8 },
            { name: '番茄', per: 'all', amount: 120, unit: 'g', step: 50, price: 0.008 },
            { name: '西兰花', per: 'all', amount: 120, unit: 'g', step: 50, price: 0.012 },
            { name: '大米', per: 'all', amount: 100, unit: 'g', step: 100, price: 0.006 },
            { name: '西瓜', per: 'all', amount: 1 / 6, unit: 'each', step: 1, price: 30 }
          ]
        }, DINNER),
        '',
        '改一下人数或饭量，清单会跟着重新算。'
      ].join('\n')
    }
  },
  plans: {
    en: {
      prompt: 'Which phone plan should I pick? I use about 12 GB of data a month and barely make calls.',
      plain: [
        'Go with **Standard**. At 12 GB a month it costs $25, while Basic would charge $28 in extra data on top of its $15 price.',
        '',
        '| Plan | Data included | Monthly cost at 12 GB |',
        '|---|---|---|',
        '| Basic | 5 GB, then $4 per GB | $43 |',
        '| Standard | 15 GB, then $4 per GB | $25 |',
        '| Unlimited | Unlimited | $40 |',
        '',
        'Unlimited only pays off above about 19 GB a month.'
      ].join('\n'),
      interactive: [
        'Go with **Standard**: at 12 GB a month it costs **$25**, against $43 for Basic with extra data and $40 for Unlimited.',
        '',
        uiBlock({
          title: 'Phone plans', subtitle: 'Calls and texts included on every plan', usage: 'Data a month', period: 'Show cost per',
          month: 'Month', year: 'Year', plan: 'Plan', data: 'Data', costMonth: 'Cost a month', costYear: 'Cost a year',
          unlimited: 'Unlimited', currency: 'USD', best: 'Best for you: {plan}', details: 'Plan details', url: 'https://example.com/phone-plans',
          ask: 'Ask about switching', askText: 'I use about {usage} GB of data a month. How do I switch to the {plan} plan without losing my number?',
          plans: [
            { name: 'Basic', price: 15, included: 5, extra: 4 },
            { name: 'Standard', price: 25, included: 15, extra: 4 },
            { name: 'Unlimited', price: 40, included: null, extra: 0 }
          ]
        }, PLANS),
        '',
        'Slide to your own usage to see when another plan wins.'
      ].join('\n')
    },
    zh: {
      prompt: '我每月大概用 12 GB 流量，几乎不打电话，选哪个手机套餐划算？',
      plain: [
        '选**标准套餐**。每月 12 GB 时只要 ¥59，基础套餐要多付 ¥35 超出流量费，合计 ¥64。',
        '',
        '| 套餐 | 包含流量 | 每月 12 GB 的花费 |',
        '|---|---|---|',
        '| 基础 | 5 GB，超出每 GB ¥5 | ¥64 |',
        '| 标准 | 20 GB，超出每 GB ¥5 | ¥59 |',
        '| 畅享 | 不限量 | ¥99 |',
        '',
        '每月用量超过约 28 GB 时，畅享套餐才更划算。'
      ].join('\n'),
      interactive: [
        '选**标准套餐**：每月 12 GB 时花费 **¥59**，基础套餐加上超出流量要 ¥64，畅享套餐要 ¥99。',
        '',
        uiBlock({
          title: '手机套餐', subtitle: '每个套餐都含通话和短信', usage: '每月流量', period: '费用按',
          month: '月', year: '年', plan: '套餐', data: '流量', costMonth: '每月费用', costYear: '每年费用',
          unlimited: '不限量', currency: 'CNY', best: '最适合你：{plan}', details: '套餐详情', url: 'https://example.com/phone-plans',
          ask: '问问怎么转套餐', askText: '我每月大概用 {usage} GB 流量，怎样在不换号的情况下转到{plan}？',
          plans: [
            { name: '基础套餐', price: 29, included: 5, extra: 5 },
            { name: '标准套餐', price: 59, included: 20, extra: 5 },
            { name: '畅享套餐', price: 99, included: null, extra: 0 }
          ]
        }, PLANS),
        '',
        '把滑块拖到你自己的用量，看看哪个套餐最省。'
      ].join('\n')
    }
  },
  trip: {
    en: {
      prompt: 'Help me budget a five-day trip to Lisbon for two people.',
      plain: [
        'Plan on about **$1,720** for two, or $860 each, with a mid-range hotel:',
        '',
        '| Category | Cost |',
        '|---|---|',
        '| Hotel, 4 nights | $480 |',
        '| Food | $400 |',
        '| Local transport | $80 |',
        '| Sights and activities | $200 |',
        '| Flights | $560 |',
        '',
        'A budget guesthouse saves about $200; a nicer hotel adds about $320.'
      ].join('\n'),
      interactive: [
        'Plan on about **$1,720** for two, or **$860 each**, with a mid-range hotel and $280 return flights.',
        '',
        uiBlock({
          title: 'Lisbon trip budget', days: 'Days', dayUnit: ' days', people: 'Travelers', peopleUnit: ' people', stay: 'Hotel',
          stayOptions: [{ value: 'budget', label: 'Budget' }, { value: 'mid', label: 'Mid-range' }, { value: 'comfort', label: 'Comfort' }],
          stays: { budget: 70, mid: 120, comfort: 200 }, daily: { food: 40, transport: 8, activities: 20 }, flight: 280,
          flightLabel: 'Flight per person', flightHint: 'Return fare', total: 'Total', each: '{amount} per person',
          parts: { stay: 'Hotel', food: 'Food', transport: 'Transport', activities: 'Activities', flights: 'Flights' },
          currency: 'USD', symbol: '$', locale: 'en-US', copy: 'Copy budget', edit: 'Change the plan',
          editText: 'Rework the Lisbon budget for {people} people over {days} days, but '
        }, TRIP),
        '',
        'Adjust the days, travelers, or hotel and the totals follow.'
      ].join('\n')
    },
    zh: {
      prompt: '帮我做个两个人去成都玩 5 天的预算。',
      plain: [
        '住舒适型酒店的话，两人合计约 **¥6,700**，每人 ¥3,350：',
        '',
        '| 项目 | 花费 |',
        '|---|---|',
        '| 住宿，4 晚 | ¥1,600 |',
        '| 餐饮 | ¥1,500 |',
        '| 市内交通 | ¥400 |',
        '| 门票和活动 | ¥800 |',
        '| 往返机票 | ¥2,400 |',
        '',
        '换成经济型酒店能省约 ¥800，住高档酒店要多花约 ¥1,600。'
      ].join('\n'),
      interactive: [
        '住舒适型酒店、机票每人往返 ¥1,200 的话，两人合计约 **¥6,700**，**每人 ¥3,350**。',
        '',
        uiBlock({
          title: '成都旅行预算', days: '天数', dayUnit: ' 天', people: '人数', peopleUnit: ' 人', stay: '酒店',
          stayOptions: [{ value: 'budget', label: '经济' }, { value: 'mid', label: '舒适' }, { value: 'comfort', label: '高档' }],
          stays: { budget: 200, mid: 400, comfort: 800 }, daily: { food: 150, transport: 40, activities: 80 }, flight: 1200,
          flightLabel: '每人机票', flightHint: '往返票价', total: '合计', each: '每人 {amount}',
          parts: { stay: '住宿', food: '餐饮', transport: '交通', activities: '门票活动', flights: '机票' },
          currency: 'CNY', symbol: '¥', locale: 'zh-CN', copy: '复制预算', edit: '改一改计划',
          editText: '按 {people} 个人、{days} 天重新做成都的预算，但是'
        }, TRIP),
        '',
        '调整天数、人数或酒店档次，总价会跟着变。'
      ].join('\n')
    }
  }
}
