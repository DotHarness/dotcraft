import type { BrowserSelectionKind } from './browserFeedback'

const DEFAULT_SELECTION_ACCENT = '#4566cc'
export const ANNOTATION_BUBBLE_PATH =
  'M12.6504 0.824799C6.21496 0.824799 0.825466 5.77554 0.825195 12.0885C0.825245 14.2375 1.46183 16.2421 2.55176 17.943L2.02148 20.235L1.99316 20.3756C1.77603 21.655 2.78945 22.7791 4.02832 22.7691L4.0791 22.8209L4.53418 22.7047L7.12305 22.0426C8.77593 22.8778 10.6577 23.3531 12.6504 23.3531C19.086 23.3531 24.4754 18.4014 24.4756 12.0885C24.4753 5.77554 19.0858 0.824799 12.6504 0.824799Z'

export interface SelectionResult {
  text: string
  kind?: BrowserSelectionKind
  rect?: { x: number; y: number; width: number; height: number }
}

function bubbleCursor(accent: string): string {
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="26" height="25" viewBox="0 0 26 25"><path d="${ANNOTATION_BUBBLE_PATH}" fill="${accent}" stroke="#fff" stroke-width="1.65"/></svg>`
  return `url("data:image/svg+xml,${encodeURIComponent(svg)}") 4 23, crosshair`
}

export function selectionScript(kind: BrowserSelectionKind, accent = DEFAULT_SELECTION_ACCENT): string {
  return `(() => {
    window.__dotcraftCancelSelection?.();
    const kind = ${JSON.stringify(kind)};
    if (kind === 'text') {
      const selection = window.getSelection();
      const r = selection?.rangeCount ? selection.getRangeAt(0).getBoundingClientRect() : null;
      return { text: selection?.toString() || '', rect: r ? {x:r.x,y:r.y,width:r.width,height:r.height} : undefined };
    }
    const accent = ${JSON.stringify(accent)};
    const dragCursor = ${JSON.stringify(bubbleCursor(accent))};
    return new Promise(resolve => {
      let phase = 'idle';
      let start;
      let element;
      let last;
      const layer = document.createElement('div');
      layer.setAttribute('data-dotcraft-selection-layer', '');
      layer.style.cssText = 'position:fixed;inset:0;z-index:2147483646;cursor:crosshair;touch-action:pan-x pan-y;background:transparent';
      const box = document.createElement('div');
      box.style.cssText = 'position:fixed;pointer-events:none;box-sizing:border-box;z-index:2147483647;display:none;border:2px solid ' + accent + ';background:color-mix(in srgb, ' + accent + ' 3%, transparent);box-shadow:inset 0 0 0 1px rgba(255,255,255,.28)';
      document.documentElement.append(layer, box);
      const clean = () => {
        layer.remove(); box.remove();
        document.removeEventListener('pointerdown', down, true);
        document.removeEventListener('pointermove', move, true);
        document.removeEventListener('pointerup', up, true);
        document.removeEventListener('keydown', key, true);
        document.removeEventListener('scroll', rehover, true);
        setTimeout(() => document.removeEventListener('click', click, true), 0);
        delete window.__dotcraftCancelSelection;
      };
      const finish = value => { clean(); resolve(value); };
      const visibleRect = bounds => {
        const x = Math.max(0,bounds.x), y = Math.max(0,bounds.y);
        return {x,y,width:Math.max(0,Math.min(innerWidth,bounds.x+bounds.width)-x),height:Math.max(0,Math.min(innerHeight,bounds.y+bounds.height)-y)};
      };
      const rect = point => ({ x: Math.min(start.x,point.x), y: Math.min(start.y,point.y), width: Math.abs(start.x-point.x), height: Math.abs(start.y-point.y) });
      const under = point => {
        const target = document.elementsFromPoint(point.x, point.y).find(node => node instanceof Element && node !== layer && node !== box);
        return target && target !== document.documentElement && target !== document.body ? target : null;
      };
      const paint = (r, style, radius) => Object.assign(box.style, { display: 'block', left: r.x+'px', top: r.y+'px', width: r.width+'px', height: r.height+'px', borderStyle: style, borderRadius: radius });
      const hover = point => {
        last = point;
        if (kind !== 'element') return;
        const target = under(point);
        if (!target) { box.style.display = 'none'; return; }
        paint(visibleRect(target.getBoundingClientRect()), 'solid', getComputedStyle(target).borderRadius);
      };
      const rehover = () => { if (phase === 'idle' && last) hover(last); };
      const cancelDrag = () => {
        if (phase === 'idle') return;
        phase = 'idle'; start = undefined; element = undefined;
        layer.style.cursor = 'crosshair';
        box.style.display = 'none';
        if (last) hover(last);
      };
      const down = event => {
        event.preventDefault(); event.stopImmediatePropagation();
        start = {x:event.clientX,y:event.clientY};
        phase = 'pressed';
        element = undefined;
        const target = kind === 'element' ? under(start) : null;
        if (target) element = {text: (target.getAttribute('aria-label') || target.innerText || target.textContent || target.tagName).trim(), rect:visibleRect(target.getBoundingClientRect())};
      };
      const move = event => {
        const point = {x:event.clientX,y:event.clientY};
        if (phase === 'idle') return hover(point);
        event.preventDefault(); event.stopImmediatePropagation();
        last = point;
        if (phase === 'pressed') {
          if (Math.abs(point.x-start.x) <= 4 && Math.abs(point.y-start.y) <= 4) return;
          phase = 'dragging';
          layer.style.cursor = dragCursor;
        }
        paint(rect(point), 'dashed', '0');
      };
      const up = event => {
        if (phase === 'idle') return;
        event.preventDefault(); event.stopImmediatePropagation();
        if (phase === 'dragging') {
          const r = visibleRect(rect({x:event.clientX,y:event.clientY}));
          return finish(r.width > 0 && r.height > 0 ? {text:'',rect:r,kind:'region'} : null);
        }
        if (element) return finish(element);
        cancelDrag();
      };
      const click = event => { event.preventDefault(); event.stopImmediatePropagation(); };
      const key = event => {
        if (event.key !== 'Escape') return;
        event.preventDefault(); event.stopImmediatePropagation();
        if (phase === 'idle') return finish(null);
        cancelDrag();
      };
      window.__dotcraftCancelSelection = () => finish(null);
      document.addEventListener('pointerdown',down,true);
      document.addEventListener('pointermove',move,true);
      document.addEventListener('pointerup',up,true);
      document.addEventListener('keydown',key,true);
      document.addEventListener('click',click,true);
      document.addEventListener('scroll',rehover,true);
    });
  })()`
}
