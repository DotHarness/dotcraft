import { z } from 'zod'
import type { GenerativeUiComponentName } from './generativeUiProtocol'

const text = (max = 2000) => z.string().max(max)
const callback = z.object({ $callback: z.string().min(1).max(512) })
const space = z.number().min(0).max(64)
const align = z.enum(['start', 'center', 'end', 'stretch', 'baseline'])
const justify = z.enum(['start', 'center', 'end', 'between', 'around'])
const textTone = z.enum(['primary', 'secondary', 'dimmed', 'success', 'warning', 'error', 'info'])
const option = z.object({ value: text(200), label: text(200), disabled: z.boolean().optional() })
const options = z.array(option).min(1).max(100)
const cell = z.union([z.string().max(2000), z.number(), z.boolean(), z.null()])
const chartColor = z.enum(['series-1', 'series-2', 'series-3', 'series-4', 'other'])
const valueFormat = z.enum(['number', 'compact', 'percent', 'currency'])
const currency = z.string().regex(/^[A-Z]{3}$/)
const iconName = z.string().regex(/^[a-z0-9]+(?:-[a-z0-9]+)*$/).max(64)

const field = {
  name: text(100).optional(),
  label: text(200).optional(),
  disabled: z.boolean().optional()
}

export const GENERATIVE_UI_SCHEMAS = {
  Card: z.object({
    title: text(200).optional(),
    subtitle: text(400).optional(),
    padding: z.enum(['none', 'sm', 'md', 'lg']).optional(),
    gap: space.optional()
  }),
  Row: z.object({ gap: space.optional(), align: align.optional(), justify: justify.optional(), wrap: z.boolean().optional(), flex: z.number().min(0).max(12).optional() }),
  Col: z.object({ gap: space.optional(), align: align.optional(), justify: justify.optional(), flex: z.number().min(0).max(12).optional() }),
  Box: z.object({
    padding: space.optional(),
    gap: space.optional(),
    surface: z.enum(['none', 'subtle', 'muted']).optional(),
    radius: z.enum(['none', 'sm', 'md']).optional(),
    flex: z.number().min(0).max(12).optional(),
    minWidth: z.number().min(0).max(1200).optional(),
    maxWidth: z.number().min(0).max(1200).optional()
  }),
  Spacer: z.object({ size: space.optional() }),
  Divider: z.object({ spacing: space.optional() }),
  Title: z.object({ value: text(400).optional(), level: z.union([z.literal(1), z.literal(2), z.literal(3)]).optional() }),
  Text: z.object({
    value: text(8000).optional(),
    tone: textTone.optional(),
    size: z.enum(['sm', 'md', 'lg', 'xl', '2xl']).optional(),
    weight: z.enum(['regular', 'medium', 'semibold']).optional(),
    align: z.enum(['start', 'center', 'end']).optional(),
    mono: z.boolean().optional(),
    tabular: z.boolean().optional(),
    truncate: z.boolean().optional()
  }),
  Caption: z.object({ value: text(2000).optional(), tone: textTone.optional() }),
  Markdown: z.object({ content: text(32_000) }),
  Badge: z.object({ label: text(80).optional(), tone: z.enum(['neutral', 'success', 'warning', 'error', 'info']).optional() }),
  Icon: z.object({ name: iconName, size: z.number().int().min(10).max(48).optional(), tone: textTone.optional(), label: text(200).optional() }),
  Image: z.object({
    src: z.string().max(1_000_000).regex(/^data:image\/(?:png|jpeg|gif|webp);base64,[A-Za-z0-9+/=\s]+$/i, 'Images must be base64 data:image URLs.'),
    alt: text(400),
    width: z.number().min(16).max(1200).optional(),
    height: z.number().min(16).max(1200).optional(),
    fit: z.enum(['cover', 'contain']).optional(),
    radius: z.enum(['none', 'sm', 'md']).optional()
  }),
  Button: z.object({
    label: text(200).optional(),
    variant: z.enum(['primary', 'secondary', 'ghost', 'danger']).optional(),
    size: z.enum(['sm', 'md']).optional(),
    icon: iconName.optional(),
    submit: z.boolean().optional(),
    disabled: z.boolean().optional(),
    fullWidth: z.boolean().optional(),
    onClick: callback.optional()
  }),
  Input: z.object({
    ...field,
    value: z.union([z.string().max(8000), z.number()]).optional(),
    defaultValue: z.union([z.string().max(8000), z.number()]).optional(),
    placeholder: text(200).optional(),
    type: z.enum(['text', 'number', 'email', 'search', 'url', 'tel']).optional(),
    hint: text(400).optional(),
    error: text(400).optional(),
    required: z.boolean().optional(),
    onChange: callback.optional(),
    onSubmit: callback.optional()
  }),
  Textarea: z.object({
    ...field,
    value: z.string().max(32_000).optional(),
    defaultValue: z.string().max(32_000).optional(),
    placeholder: text(200).optional(),
    rows: z.number().int().min(2).max(16).optional(),
    hint: text(400).optional(),
    error: text(400).optional(),
    onChange: callback.optional()
  }),
  Select: z.object({
    ...field,
    value: text(200).optional(),
    defaultValue: text(200).optional(),
    options,
    placeholder: text(200).optional(),
    onChange: callback.optional()
  }),
  Checkbox: z.object({ ...field, checked: z.boolean().optional(), defaultChecked: z.boolean().optional(), onChange: callback.optional() }),
  RadioGroup: z.object({
    ...field,
    value: text(200).optional(),
    defaultValue: text(200).optional(),
    options,
    direction: z.enum(['row', 'column']).optional(),
    onChange: callback.optional()
  }),
  SegmentedControl: z.object({
    ...field,
    value: text(200).optional(),
    defaultValue: text(200).optional(),
    options: z.array(option).min(2).max(6),
    onChange: callback.optional()
  }),
  Slider: z.object({
    ...field,
    value: z.number().optional(),
    defaultValue: z.number().optional(),
    min: z.number(),
    max: z.number(),
    step: z.number().positive().optional(),
    valueText: text(80).optional(),
    onChange: callback.optional()
  }).refine(props => props.max > props.min, 'Slider max must be greater than min.'),
  Form: z.object({ gap: space.optional(), onSubmit: callback.optional() }),
  Table: z.object({
    columns: z.array(z.object({
      key: text(100),
      label: text(200),
      align: z.enum(['start', 'center', 'end']).optional(),
      format: valueFormat.optional(),
      currency: currency.optional()
    })).min(1).max(12),
    rows: z.array(z.record(z.string(), cell)).max(200),
    caption: text(400).optional(),
    rowKey: text(100).optional()
  }),
  List: z.object({
    items: z.array(z.object({
      title: text(400),
      description: text(1000).optional(),
      icon: iconName.optional(),
      trailing: text(200).optional()
    })).max(200),
    ordered: z.boolean().optional(),
    divided: z.boolean().optional()
  }),
  Chart: z.object({
    type: z.enum(['line', 'bar', 'area']),
    data: z.array(z.record(z.string(), z.union([z.string().max(200), z.number(), z.null()]))).min(1).max(500),
    xKey: text(100),
    series: z.array(z.object({ key: text(100), label: text(200).optional(), color: chartColor.optional() })).min(1).max(5),
    height: z.number().min(120).max(480).optional(),
    stacked: z.boolean().optional(),
    format: valueFormat.optional(),
    currency: currency.optional(),
    showLegend: z.boolean().optional()
  }),
  PieChart: z.object({
    data: z.array(z.object({ label: text(200), value: z.number().min(0), color: chartColor.optional() })).min(1).max(12),
    height: z.number().min(120).max(480).optional(),
    donut: z.boolean().optional(),
    format: valueFormat.optional(),
    currency: currency.optional(),
    showLegend: z.boolean().optional()
  })
} satisfies Record<GenerativeUiComponentName, z.ZodType>

export type GenerativeUiProps<Name extends GenerativeUiComponentName> = z.infer<(typeof GENERATIVE_UI_SCHEMAS)[Name]>
export type GenerativeUiCallback = z.infer<typeof callback>

export type GenerativeUiPropsResult =
  | { ok: true; props: Record<string, unknown> }
  | { ok: false; message: string }

export function isGenerativeUiComponent(type: string): type is GenerativeUiComponentName {
  return Object.prototype.hasOwnProperty.call(GENERATIVE_UI_SCHEMAS, type)
}

function withoutNullProps(props: unknown): unknown {
  if (!props || typeof props !== 'object' || Array.isArray(props)) return props
  return Object.fromEntries(Object.entries(props).filter(([, value]) => value !== null))
}

export function readGenerativeUiProps(type: GenerativeUiComponentName, props: unknown): GenerativeUiPropsResult {
  const result = GENERATIVE_UI_SCHEMAS[type].safeParse(withoutNullProps(props))
  if (result.success) return { ok: true, props: result.data as Record<string, unknown> }
  const issue = result.error.issues[0]
  const path = issue?.path.join('.')
  return { ok: false, message: path ? `${path}: ${issue.message}` : issue?.message ?? 'Invalid props.' }
}
