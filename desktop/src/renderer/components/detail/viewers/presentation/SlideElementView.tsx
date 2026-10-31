import { memo, type CSSProperties } from 'react'
import { ChartColumn, ImageOff, Shapes } from 'lucide-react'
import type { SlideElement, SlideTable, TextBody } from '../../../../../shared/viewer/presentation'
import { useT } from '../../../../contexts/LocaleContext'
import { fillStyle, fontStack, paragraphStyle, runStyle, solidColor } from './slideStyle'

const ANCHOR: Record<TextBody['anchor'], CSSProperties['justifyContent']> = { top: 'flex-start', middle: 'center', bottom: 'flex-end' }

function TextBodyView({ body, fill }: { body: TextBody; fill?: boolean }): JSX.Element {
  const { insets } = body
  return (
    <div
      className="dc-slide__text"
      data-fill={fill || undefined}
      data-nowrap={!body.wrap || undefined}
      style={{ padding: `${insets.top}px ${insets.right}px ${insets.bottom}px ${insets.left}px`, justifyContent: ANCHOR[body.anchor] }}
    >
      {body.paragraphs.map((paragraph, index) => (
        <p key={index} className="dc-slide__paragraph" style={paragraphStyle(paragraph)}>
          {paragraph.bullet && (
            <span
              className="dc-slide__bullet"
              style={{
                width: (paragraph.indent ?? 0) < 0 ? -paragraph.indent! : undefined,
                color: paragraph.bullet.color,
                fontSize: paragraph.bullet.scale ? `${paragraph.bullet.scale * 100}%` : undefined,
                fontFamily: fontStack(paragraph.bullet.font)
              }}
            >
              {paragraph.bullet.text}
            </span>
          )}
          {paragraph.runs.map((run, runIndex) =>
            run.text === '\n' ? (
              <br key={runIndex} />
            ) : (
              <span key={runIndex} style={runStyle(run)} data-link={run.link} className={run.link ? 'dc-slide__link' : undefined}>
                {run.text}
              </span>
            )
          )}
          {paragraph.runs.length === 0 && '​'}
        </p>
      ))}
    </div>
  )
}

function ShapeFigure({ element, images }: { element: SlideElement; images: Record<string, string> }): JSX.Element | null {
  const { geometry, line, frame } = element
  if (!geometry) return null
  const stroke = line ? { stroke: line.color, strokeWidth: line.width, strokeDasharray: line.dash ? `${line.width * 4} ${line.width * 3}` : undefined } : { stroke: 'none' }
  const flip = frame.flipH || frame.flipV ? `scale(${frame.flipH ? -1 : 1}, ${frame.flipV ? -1 : 1})` : undefined
  if (geometry.kind === 'rect' || geometry.kind === 'roundRect' || geometry.kind === 'ellipse') {
    return (
      <div
        className="dc-slide__box"
        style={{
          ...fillStyle(element.fill, images),
          borderRadius: geometry.kind === 'ellipse' ? '50%' : geometry.kind === 'roundRect' ? geometry.radius : undefined,
          border: line ? `${line.width}px ${line.dash ? 'dashed' : 'solid'} ${line.color}` : undefined
        }}
      />
    )
  }
  const width = Math.max(frame.w, 1)
  const height = Math.max(frame.h, 1)
  const fill = solidColor(element.fill) ?? 'none'
  return (
    <svg className="dc-slide__figure" width={width} height={height} viewBox={`0 0 ${width} ${height}`} style={{ transform: flip }} aria-hidden>
      {geometry.kind === 'line' && <line x1={0} y1={0} x2={frame.w} y2={frame.h} {...stroke} strokeLinecap="round" />}
      {geometry.kind === 'polygon' && (
        <polygon points={geometry.points.map(([x, y]) => `${x * width},${y * height}`).join(' ')} fill={fill} {...stroke} strokeLinejoin="round" />
      )}
      {geometry.kind === 'path' && <path d={geometry.d} fill={fill} {...stroke} strokeLinejoin="round" />}
    </svg>
  )
}

function PictureView({ element, images }: { element: SlideElement; images: Record<string, string> }): JSX.Element {
  const t = useT()
  const source = element.image ? images[element.image] : undefined
  const { frame, crop, geometry, line } = element
  if (!source) {
    return (
      <div className="dc-slide__placeholder">
        <ImageOff size={24} aria-hidden />
        <span>{t('viewer.slides.picture')}</span>
      </div>
    )
  }
  const width = crop ? frame.w / (1 - crop.left - crop.right) : frame.w
  const height = crop ? frame.h / (1 - crop.top - crop.bottom) : frame.h
  return (
    <div
      className="dc-slide__picture"
      style={{
        borderRadius: geometry?.kind === 'ellipse' ? '50%' : geometry?.kind === 'roundRect' ? geometry.radius : undefined,
        outline: line ? `${line.width}px solid ${line.color}` : undefined,
        outlineOffset: line ? -line.width / 2 : undefined,
        transform: frame.flipH || frame.flipV ? `scale(${frame.flipH ? -1 : 1}, ${frame.flipV ? -1 : 1})` : undefined
      }}
    >
      <img
        src={source}
        alt=""
        draggable={false}
        style={{ width, height, left: crop ? -crop.left * width : 0, top: crop ? -crop.top * height : 0 }}
      />
    </div>
  )
}

function TableView({ table }: { table: SlideTable }): JSX.Element {
  return (
    <table className="dc-slide__table" style={{ width: table.columns.reduce((sum, width) => sum + width, 0) }}>
      <colgroup>
        {table.columns.map((width, index) => (
          <col key={index} style={{ width }} />
        ))}
      </colgroup>
      <tbody>
        {table.rows.map((row, rowIndex) => (
          <tr key={rowIndex} style={{ height: row.height }}>
            {row.cells.map((cell, cellIndex) =>
              cell ? (
                <td
                  key={cellIndex}
                  colSpan={cell.colSpan}
                  rowSpan={cell.rowSpan}
                  style={{
                    background: cell.fill,
                    border: cell.border ? `1px solid ${cell.border}` : undefined,
                    verticalAlign: cell.text.anchor === 'middle' ? 'middle' : cell.text.anchor === 'bottom' ? 'bottom' : 'top'
                  }}
                >
                  <TextBodyView body={cell.text} />
                </td>
              ) : null
            )}
          </tr>
        ))}
      </tbody>
    </table>
  )
}

function Placeholder({ element }: { element: SlideElement }): JSX.Element {
  const t = useT()
  const chart = element.kind === 'chart'
  return (
    <div className="dc-slide__placeholder">
      {chart ? <ChartColumn size={28} aria-hidden /> : <Shapes size={28} aria-hidden />}
      <span>{chart ? element.chartTitle ?? t('viewer.slides.chart') : t('viewer.slides.object')}</span>
    </div>
  )
}

export const SlideElementView = memo(function SlideElementView({ element, images }: { element: SlideElement; images: Record<string, string> }): JSX.Element {
  const { frame } = element
  return (
    <div
      className="dc-slide__element"
      data-element-id={element.inherited ? undefined : element.id}
      style={{
        left: frame.x,
        top: frame.y,
        width: frame.w,
        height: frame.h,
        transform: frame.rotation ? `rotate(${frame.rotation}deg)` : undefined
      }}
    >
      {element.kind === 'picture' ? (
        <PictureView element={element} images={images} />
      ) : element.kind === 'table' && element.table ? (
        <TableView table={element.table} />
      ) : element.kind === 'chart' || element.kind === 'unsupported' ? (
        <Placeholder element={element} />
      ) : (
        <ShapeFigure element={element} images={images} />
      )}
      {element.text && element.kind !== 'table' && <TextBodyView body={element.text} fill />}
    </div>
  )
})
