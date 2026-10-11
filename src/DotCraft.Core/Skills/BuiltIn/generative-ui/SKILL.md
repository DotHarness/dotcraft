---
name: generative-ui
description: "Answer with a small interactive tool built from Desktop's own components: calculators, forms, quizzes, choices, comparisons, and cards the user can adjust or act on. Use only when interaction materially helps the answer; use visualize for free-form graphics, simulations, maps, or games, and plain Markdown for static facts and tables."
---

# Generative UI

- Use Generative UI only when the system context has a `# Generative UI` section. Otherwise, answer in Markdown and never emit a `dotcraft-ui` block.
- Use it when the user benefits from acting on the answer: adjusting inputs (calculators, estimators), choosing between options, answering a quiz, filling a short form, or acting on a comparison (copy a command, open a link, ask a follow-up).
- Use the `visualize` skill instead for dynamics, simulations, maps, custom graphics, games, and anything that needs free-form HTML, SVG, or canvas.
- Use plain Markdown for static facts, lists, and tables, and for every plain answer. Never add a block merely because the answer contains numbers or options.
- Use at most one surface per piece of content. Never render the same content as both a block and a visualization.
- Work silently: the final reply is your first user-facing message. Never announce this skill, widgets, components, JSX, programs, or code.
- In prose, describe only what the block helps the user see or decide. Keep it concise and never repeat what the block already shows.
- Read this file in full before authoring.

## Authoring contract

### Block

- Write one top-level fenced block with the info string `dotcraft-ui` and three backticks. Never indent it or put it inside a list, quote, or another fence. Never put three backticks in a row inside the program.
- Each block holds one JSX program that ends with `DotCraft.render(<App />)`. A program that never calls `render` fails.
- A message may contain several blocks; each renders in place between the surrounding Markdown. Usually one block is right. Place it where it belongs in the answer.
- Close each fence as soon as the program is complete.

### Program

- Built-in components are capitalized globals (`Card`, `Row`, `Slider`, ...). Use no `import` or `export`.
- Define your own components as named functions at the top level of the program, with distinct names.
- Props are JSON values (strings, finite numbers, booleans, null, arrays, plain objects) or functions. A function prop is an event handler. Elements are valid only as children, never as props. `undefined` props are omitted.
- Give every element rendered from an array a stable `key`.
- Network, storage, workers, and imports are unavailable. Timers (`setTimeout`, `setInterval`) are available; start them in `useEffect` and clear them in its cleanup.
- Keep the program deterministic: the first render must be the same every time. Never use `Math.random()`, `Date.now()`, or `new Date()` for initial values. Put verified data from the conversation directly in the program.
- Images must be base64 `data:image/png`, `jpeg`, `gif`, or `webp` URLs.

### Hooks

All hooks live on `DotCraft` and follow React semantics. Call them at the top of a component, in the same order on every render, never inside conditions, loops, or handlers.

| Hook | Meaning |
|------|---------|
| `useState(initial)` | State and setter; the setter accepts a value or an updater function. |
| `useEffect(effect, deps?)` | Runs after a committed render; may return a cleanup. |
| `useMemo(compute, deps?)` | Cached derived value. |
| `useRef(initial)` | Mutable `{ current }` that survives renders. |
| `useId()` | Stable unique string. |
| `useTheme()` | `'light'` or `'dark'`. |
| `useBreakpoint()` | `'compact'` below 480px of block width, `'regular'` below 900px, otherwise `'wide'`. |

Never set state unconditionally during render; a state update on every render fails the block.

### Saved state

- Every `useState` value is saved per block after the user interacts and restored when the conversation is reopened. Keep component names, tree shape, and hook order stable, and initial values deterministic, so saved values still apply.
- Only JSON values are saved; others are skipped. Keep the whole state of a block under 16 KiB or none of it is saved.
- `useRef`, `useMemo`, and uncontrolled fields (`defaultValue`, `defaultChecked`) are not saved. Use controlled fields (`value`/`checked` plus `onChange` backed by `useState`) for anything that should survive a reload.
- You never see saved state or what the user did in a block. Ask the user, or have the block send a self-contained message.

### Actions

Call actions only from event handlers, at most one per user interaction, with arguments within the sizes in the table.

| Action | Effect |
|--------|--------|
| `DotCraft.sendMessage(prompt)` | Sends `prompt` as the user's next message in this thread. Up to 16 KiB. |
| `DotCraft.prefillComposer(text)` | Puts `text` in the composer without sending. Up to 64 KiB. |
| `DotCraft.copy(text)` | Copies `text` to the clipboard. Up to 64 KiB. |
| `DotCraft.openUrl(url)` | Opens an `http` or `https` link. |

- Make every `sendMessage` prompt self-contained: include the selected values and the request, because you will not see the block's state. Write it as the user would, such as `Explain why merge sort is stable. I answered quicksort.`
- Label the triggering `Button` with what happens, such as `Explain`, `Copy command`, or `Open docs`.

### Limits

Source 64 KB, rendered tree 256 KB of JSON, 1,500 components, depth 32, first render within 5 seconds. Aggregate or trim data that would exceed them; tables take up to 200 rows and charts up to 500 points.

## Components

Use only the components and props below. Text props take at most 2,000 characters unless noted. `space` is a number from 0 to 64 (pixels).

### Layout

- `Card` — `title?`, `subtitle?`, `padding?` (`none`|`sm`|`md`|`lg`, default `md`), `gap?` space (default 12). One quiet surface.
- `Row` — `gap?` space (default 8), `align?` (`start`|`center`|`end`|`stretch`|`baseline`, default `center`), `justify?` (`start`|`center`|`end`|`between`|`around`), `wrap?`, `flex?` 0–12.
- `Col` — `gap?` (default 8), `align?`, `justify?`, `flex?`. Children stretch to its width, except buttons, badges, icons, and images.
- `Box` — `padding?` space, `gap?` (default 8), `surface?` (`none`|`subtle`|`muted`), `radius?` (`none`|`sm`|`md`), `flex?` 0–12, `minWidth?`/`maxWidth?` 0–1200. A column.
- `Spacer` — `size?` space. Without `size`, it fills the remaining room in a `Row` and pushes the following items to the end.
- `Divider` — `spacing?` space (default 4).

### Text

- `Title` — `value?` or text children, `level?` 1|2|3 (default 2).
- `Text` — `value?` (up to 8,000 characters) or text children, `tone?`, `size?` (`sm`|`md`|`lg`|`xl`|`2xl`), `weight?` (`regular`|`medium`|`semibold`), `align?` (`start`|`center`|`end`), `mono?`, `tabular?`, `truncate?`.
- `Caption` — `value?` or text children, `tone?`. Secondary meta text.
- `Markdown` — `content` (required, up to 32,000 characters). Takes no children. Conversation Markdown without Mermaid.
- `Badge` — `label?` (up to 80 characters) or text children, `tone?` (`neutral`|`success`|`warning`|`error`|`info`).

`tone` for `Text`, `Caption`, and `Icon` is `primary`|`secondary`|`dimmed`|`success`|`warning`|`error`|`info`.

### Media

- `Icon` — `name` (a Lucide icon name in kebab case, such as `circle-check`), `size?` integer 10–48 (default 16), `tone?`, `label?`. Without `label` the icon is decorative.
- `Image` — `src` (base64 `data:image/...` URL), `alt` (required), `width?`/`height?` 16–1200, `fit?` (`cover`|`contain`), `radius?` (`none`|`sm`|`md`, default `md`).

### Controls

Every field takes `name?`, `label?`, and `disabled?`. A field is controlled with `value` (or `checked`) plus `onChange`, or uncontrolled with `defaultValue` (or `defaultChecked`).

- `Button` — `label?` or text children, `variant?` (`primary`|`secondary`|`ghost`|`danger`, default `secondary`), `size?` (`sm`|`md`), `icon?` Lucide name, `submit?`, `disabled?`, `fullWidth?`, `onClick()`. With `submit`, it submits the enclosing `Form` and still calls `onClick`.
- `Input` — `value?`/`defaultValue?` string or number, `placeholder?`, `type?` (`text`|`number`|`email`|`search`|`url`|`tel`), `hint?`, `error?`, `required?`, `onChange(value)`, `onSubmit(value)` on Enter. With `type="number"`, `value` is a number when the text parses, otherwise the raw string (such as `''`).
- `Textarea` — `value?`/`defaultValue?` string, `placeholder?`, `rows?` 2–16 (default 3), `hint?`, `error?`, `onChange(text)`.
- `Select` — `options`, `value?`/`defaultValue?`, `placeholder?`, `onChange(value)`. With `placeholder` the initial value is `''`; without it, the first option.
- `Checkbox` — `checked?`/`defaultChecked?`, `label?` or text children, `onChange(checked)`.
- `RadioGroup` — `options`, `value?`/`defaultValue?`, `direction?` (`row`|`column`, default `column`), `onChange(value)`. Use `''` for no selection.
- `SegmentedControl` — `options` (2–6), `value?`/`defaultValue?`, `onChange(value)`. Defaults to the first option.
- `Slider` — `min` and `max` (required, `max` greater than `min`), `step?` (positive), `value?`/`defaultValue?`, `valueText?` (up to 80 characters, shown at the trailing edge; defaults to the raw number), `onChange(number)`.
- `Form` — `gap?` (default 12), `onSubmit(values)`. `values` maps each named field inside the form to its current value (string, number, or boolean). Validation is the program's job.

`options` is an array of 1–100 `{ value, label, disabled? }` objects with string `value` and `label` up to 200 characters. Only `Input` and `Textarea` show `hint` and `error`; show a validation message for other fields with a `Caption tone="error"` below them.

### Data

- `Table` — `columns` (1–12 `{ key, label, align?, format?, currency? }`), `rows` (up to 200 objects mapping column keys to string, number, boolean, or null), `caption?`, `rowKey?` (the column whose value identifies a row). Numbers align right; booleans render as a check or a dash.
- `List` — `items` (up to 200 `{ title, description?, icon?, trailing? }`), `ordered?`, `divided?`.
- `Chart` — `type` (`line`|`bar`|`area`), `data` (1–500 objects of string, number, or null), `xKey`, `series` (1–5 `{ key, label?, color? }`), `height?` 120–480 (default 200), `stacked?`, `format?`, `currency?`, `showLegend?` (default: shown with more than one series).
- `PieChart` — `data` (1–12 `{ label, value, color? }` with `value` ≥ 0), `height?` 120–480, `donut?`, `format?`, `currency?`, `showLegend?` (default shown, with values).

`format` is `number`|`compact`|`percent`|`currency`. `percent` expects fractions (`0.25` shows 25%). `currency` is a three-letter ISO 4217 code used with `format: "currency"` (default `USD`); on `Table` it is set per column. Chart `color` is `series-1`–`series-4` or `other`; series take colors in order by default.

## Layout and style

- Use one `Card` per answer, titled concisely. Never nest cards. Inside the card, rely on the default gaps; change `gap` only to group or separate content.
- Use `Box surface` sparingly, for at most one highlighted result or a grouped region. Never add surfaces for decoration.
- Put related controls in a `Col` (the card already is one). Use a `Row` for inline actions or side-by-side figures; give columns in a row `flex={1}` to share the width, and `flex={0}` to keep one at its natural width. A row of buttons or badges wraps by itself; add `wrap` when other items may not fit.
- Read `useBreakpoint()` and stack side-by-side content when it is `'compact'`.
- Show key figures with `Text size="xl"` or `"2xl"`, `weight="semibold"`, and `tabular`, labeled with a `Caption`. Use `tabular` on every changing number.
- Use tones and badge tones only for meaning (success, warning, error, info), never for decoration. Keep text in the default tone otherwise.
- Use one primary `Button` per group. Prefer `secondary` or `ghost` for supporting actions.
- Show only the controls the task needs. Never invent filters, resets, or metrics the user did not ask for.

## Accessibility

- Give every field a `label`, including sliders, radio groups, and segmented controls.
- Give an `Icon` a `label` when it carries meaning on its own; leave decorative icons unlabeled. Give every `Image` a meaningful `alt`.
- Never encode meaning only in color: pair tones with text, such as a `Badge` label of `Correct`.
- Label buttons with verbs that say what happens: `Check answers`, `Copy command`, `Draft report`.

## Reply shape

Put any necessary concise explanation outside the block and place the block where it belongs. Never repeat the block's data in prose; add at most one short conclusion when the user needs an explanation.

````markdown
Adjust the amount, rate, and term to see the monthly payment and how the balance falls.

```dotcraft-ui
function Mortgage() {
  const [amount, setAmount] = DotCraft.useState(400000)
  const [rate, setRate] = DotCraft.useState(6.5)
  const [term, setTerm] = DotCraft.useState('30')
  const compact = DotCraft.useBreakpoint() === 'compact'
  const months = Number(term) * 12
  const monthlyRate = rate / 100 / 12
  const payment = monthlyRate === 0
    ? amount / months
    : amount * monthlyRate / (1 - Math.pow(1 + monthlyRate, -months))
  const balances = DotCraft.useMemo(() => {
    const points = []
    let balance = amount
    for (let year = 1; year <= Number(term); year++) {
      for (let month = 0; month < 12; month++) balance -= payment - balance * monthlyRate
      points.push({ year: String(year), balance: Math.max(0, Math.round(balance)) })
    }
    return points
  }, [amount, rate, term])
  const money = value => '$' + Math.round(value).toLocaleString('en-US')
  return (
    <Card title="Monthly payment">
      <Slider label="Loan amount" min={50000} max={1500000} step={10000} value={amount} onChange={setAmount} valueText={money(amount)} />
      <Slider label="Interest rate" min={0} max={12} step={0.125} value={rate} onChange={setRate} valueText={`${rate}%`} />
      <SegmentedControl label="Term" value={term} onChange={setTerm}
        options={[{ value: '15', label: '15 years' }, { value: '30', label: '30 years' }]} />
      <Row gap={compact ? 12 : 24} wrap>
        <Col gap={2}>
          <Caption>Per month</Caption>
          <Text size="xl" weight="semibold" tabular>{money(payment)}</Text>
        </Col>
        <Col gap={2}>
          <Caption>Total interest</Caption>
          <Text size="xl" weight="semibold" tabular>{money(payment * months - amount)}</Text>
        </Col>
      </Row>
      <Chart type="area" data={balances} xKey="year" series={[{ key: 'balance', label: 'Balance left' }]}
        format="currency" currency="USD" height={180} />
    </Card>
  )
}
DotCraft.render(<Mortgage />)
```
````

## More examples

### Quiz with follow-up questions

```dotcraft-ui
const QUESTIONS = [
  { id: 'search', prompt: 'Binary search on a sorted array of n items takes…', answer: 'log',
    options: [{ value: 'n', label: 'O(n)' }, { value: 'log', label: 'O(log n)' }, { value: 'nlog', label: 'O(n log n)' }] },
  { id: 'stable', prompt: 'Which of these sorts is stable?', answer: 'merge',
    options: [{ value: 'quick', label: 'Quicksort' }, { value: 'heap', label: 'Heapsort' }, { value: 'merge', label: 'Merge sort' }] }
]

function labelOf(question, value) {
  return question.options.find(option => option.value === value)?.label ?? 'nothing'
}

function Quiz() {
  const [answers, setAnswers] = DotCraft.useState({})
  const [checked, setChecked] = DotCraft.useState(false)
  const score = QUESTIONS.filter(q => answers[q.id] === q.answer).length
  return (
    <Card title="Check your understanding">
      {QUESTIONS.map(q => (
        <Col key={q.id} gap={6}>
          <RadioGroup label={q.prompt} options={q.options} value={answers[q.id] ?? ''} disabled={checked}
            onChange={value => setAnswers(previous => ({ ...previous, [q.id]: value }))} />
          {checked && (
            <Row wrap>
              <Badge tone={answers[q.id] === q.answer ? 'success' : 'error'}
                label={answers[q.id] === q.answer ? 'Correct' : `Answer: ${labelOf(q, q.answer)}`} />
              <Button size="sm" variant="ghost" label="Explain" onClick={() => DotCraft.sendMessage(
                `Explain why the answer to "${q.prompt}" is ${labelOf(q, q.answer)}. I answered ${labelOf(q, answers[q.id])}.`)} />
            </Row>
          )}
        </Col>
      ))}
      <Row>
        {checked
          ? <Text tabular>{`${score} of ${QUESTIONS.length} correct`}</Text>
          : <Button variant="primary" label="Check answers" disabled={Object.keys(answers).length < QUESTIONS.length}
              onClick={() => setChecked(true)} />}
        <Spacer />
        {checked && <Button label="Try again" onClick={() => { setAnswers({}); setChecked(false) }} />}
      </Row>
    </Card>
  )
}
DotCraft.render(<Quiz />)
```

### Comparison with copy and open actions

```dotcraft-ui
const LIBRARIES = [
  { id: 'zod', name: 'Zod', api: 'Chained methods', license: 'MIT', install: 'npm install zod', docs: 'https://zod.dev' },
  { id: 'valibot', name: 'Valibot', api: 'Composable functions', license: 'MIT', install: 'npm install valibot', docs: 'https://valibot.dev' },
  { id: 'yup', name: 'Yup', api: 'Chained methods', license: 'MIT', install: 'npm install yup', docs: 'https://github.com/jquense/yup' }
]

function Libraries() {
  const [selected, setSelected] = DotCraft.useState('zod')
  const library = LIBRARIES.find(item => item.id === selected)
  return (
    <Card title="Schema validation libraries">
      <Table rowKey="name"
        columns={[{ key: 'name', label: 'Library' }, { key: 'api', label: 'API style' }, { key: 'license', label: 'License' }]}
        rows={LIBRARIES.map(({ name, api, license }) => ({ name, api, license }))} />
      <SegmentedControl label="Library" value={selected} onChange={setSelected}
        options={LIBRARIES.map(item => ({ value: item.id, label: item.name }))} />
      <Row wrap>
        <Text mono>{library.install}</Text>
        <Spacer />
        <Button icon="copy" label="Copy command" onClick={() => DotCraft.copy(library.install)} />
        <Button icon="external-link" label="Open docs" onClick={() => DotCraft.openUrl(library.docs)} />
      </Row>
    </Card>
  )
}
DotCraft.render(<Libraries />)
```

### Form with validation

```dotcraft-ui
const AREAS = [{ value: 'editor', label: 'Editor' }, { value: 'sync', label: 'Sync' }, { value: 'billing', label: 'Billing' }]

function BugReport() {
  const [draft, setDraft] = DotCraft.useState({ title: '', area: '', steps: '', regression: false })
  const [errors, setErrors] = DotCraft.useState({})
  const set = key => value => setDraft(previous => ({ ...previous, [key]: value }))
  const submit = values => {
    const next = {}
    if (!values.title.trim()) next.title = 'Add a short summary.'
    if (!values.area) next.area = 'Choose an area.'
    if (values.steps.trim().length < 10) next.steps = 'Describe the steps to reproduce.'
    setErrors(next)
    if (Object.keys(next).length > 0) return
    DotCraft.prefillComposer([
      `Write a bug report for: ${values.title.trim()}`,
      `Area: ${values.area}`,
      `Regression: ${values.regression ? 'yes' : 'no'}`,
      `Steps to reproduce:\n${values.steps.trim()}`
    ].join('\n'))
  }
  return (
    <Card title="Bug report">
      <Form onSubmit={submit}>
        <Input name="title" label="Summary" value={draft.title} onChange={set('title')} error={errors.title} required />
        <Col gap={4}>
          <Select name="area" label="Area" placeholder="Choose an area" options={AREAS} value={draft.area} onChange={set('area')} />
          {errors.area && <Caption tone="error">{errors.area}</Caption>}
        </Col>
        <Textarea name="steps" label="Steps to reproduce" rows={4} value={draft.steps} onChange={set('steps')}
          error={errors.steps} hint="One step per line." />
        <Checkbox name="regression" label="This used to work" checked={draft.regression} onChange={set('regression')} />
        <Row justify="end">
          <Button submit variant="primary" label="Draft report" />
        </Row>
      </Form>
    </Card>
  )
}
DotCraft.render(<BugReport />)
```

## Before replying

- Check every component name, prop name, and enum value against the component reference above.
- Check that hooks run unconditionally and in a fixed order, components are top-level named functions, mapped elements have keys, and no handler calls more than one action.
- Check that the prose does not repeat what the block shows and the block's fence is closed.
