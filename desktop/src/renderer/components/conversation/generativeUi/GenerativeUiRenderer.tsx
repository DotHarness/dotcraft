import { Fragment, lazy, Suspense, useMemo, type ComponentType, type ReactNode } from 'react'
import { Skeleton } from '../../ui/Skeleton'
import type { GenerativeUiComponentName, GenerativeUiElementNode, GenerativeUiNode } from './generativeUiProtocol'
import { isGenerativeUiComponent, readGenerativeUiProps, type GenerativeUiProps } from './generativeUiSchema'
import { GenerativeUiNodeError } from './GenerativeUiNodeError'
import { GenerativeUiBox, GenerativeUiCard, GenerativeUiCol, GenerativeUiDivider, GenerativeUiForm, GenerativeUiRow, GenerativeUiSpacer } from './components/GenerativeUiLayout'
import {
  GenerativeUiBadge,
  GenerativeUiCaption,
  GenerativeUiIcon,
  GenerativeUiImage,
  GenerativeUiList,
  GenerativeUiMarkdown,
  GenerativeUiTable,
  GenerativeUiText,
  GenerativeUiTitle
} from './components/GenerativeUiContent'
import {
  GenerativeUiButton,
  GenerativeUiCheckbox,
  GenerativeUiInput,
  GenerativeUiRadioGroup,
  GenerativeUiSegmentedControl,
  GenerativeUiSelect,
  GenerativeUiSlider,
  GenerativeUiTextarea
} from './components/GenerativeUiControls'

const LazyChart = lazy(() => import('./components/GenerativeUiCharts'))
const LazyPieChart = lazy(() => import('./components/GenerativeUiCharts').then(module => ({ default: module.GenerativeUiPieChart })))

function GenerativeUiChart({ props }: { props: GenerativeUiProps<'Chart'> }): JSX.Element {
  return <Suspense fallback={<Skeleton height={props.height ?? 200} radius={8} />}><LazyChart props={props} /></Suspense>
}

function GenerativeUiPieChart({ props }: { props: GenerativeUiProps<'PieChart'> }): JSX.Element {
  return <Suspense fallback={<Skeleton height={props.height ?? 200} radius={8} />}><LazyPieChart props={props} /></Suspense>
}

type ComponentMap = { [Name in GenerativeUiComponentName]: ComponentType<{ props: GenerativeUiProps<Name>; children?: ReactNode }> }

const COMPONENTS: ComponentMap = {
  Card: GenerativeUiCard,
  Row: GenerativeUiRow,
  Col: GenerativeUiCol,
  Box: GenerativeUiBox,
  Spacer: GenerativeUiSpacer,
  Divider: GenerativeUiDivider,
  Title: GenerativeUiTitle,
  Text: GenerativeUiText,
  Caption: GenerativeUiCaption,
  Markdown: GenerativeUiMarkdown,
  Badge: GenerativeUiBadge,
  Icon: GenerativeUiIcon,
  Image: GenerativeUiImage,
  Button: GenerativeUiButton,
  Input: GenerativeUiInput,
  Textarea: GenerativeUiTextarea,
  Select: GenerativeUiSelect,
  Checkbox: GenerativeUiCheckbox,
  RadioGroup: GenerativeUiRadioGroup,
  SegmentedControl: GenerativeUiSegmentedControl,
  Slider: GenerativeUiSlider,
  Form: GenerativeUiForm,
  Table: GenerativeUiTable,
  List: GenerativeUiList,
  Chart: GenerativeUiChart,
  PieChart: GenerativeUiPieChart
}

export function GenerativeUiNodes({ nodes }: { nodes: readonly GenerativeUiNode[] }): JSX.Element {
  return (
    <>
      {nodes.map((node, index) => typeof node === 'string'
        ? <Fragment key={`text-${index}`}>{node}</Fragment>
        : <GenerativeUiElement key={node.key === undefined ? `${node.type}-${index}` : `key-${node.key}`} node={node} />)}
    </>
  )
}

function GenerativeUiElement({ node }: { node: GenerativeUiElementNode }): JSX.Element {
  const result = useMemo(() => isGenerativeUiComponent(node.type) ? readGenerativeUiProps(node.type, node.props) : null, [node.props, node.type])
  if (!isGenerativeUiComponent(node.type) || !result) {
    return <GenerativeUiNodeError component={node.type} detail="Unknown component." />
  }
  if (!result.ok) return <GenerativeUiNodeError component={node.type} detail={result.message} />
  const Component = COMPONENTS[node.type] as ComponentType<{ props: Record<string, unknown>; children?: ReactNode }>
  return (
    <Component props={result.props}>
      {node.children.length > 0 ? <GenerativeUiNodes nodes={node.children} /> : undefined}
    </Component>
  )
}
