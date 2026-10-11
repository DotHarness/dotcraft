export interface EnergyStrings {
  title: string
  usage: string
  cost: string
  thisYear: string
  lastYear: string
  months: string[]
  unit: string
  symbol: string
  rate: number
  totalThis: string
  totalLast: string
  change: string
  chartLabel: string
}

export function energyFragment(t: EnergyStrings): string {
  return String.raw`<div id="energy" class="viz-root">
<style>
#energy{display:flex;flex-direction:column;gap:12px}
#energy .head{display:flex;flex-wrap:wrap;align-items:center;justify-content:space-between;gap:8px}
#energy h3{margin:0}
#energy .legend{display:flex;flex-wrap:wrap;gap:12px}
#energy .legend button{min-height:0;padding:0;gap:6px;border:0;background:transparent;color:var(--foreground)}
#energy .legend button[aria-pressed=false]{color:var(--muted-foreground)}
#energy .legend button[aria-pressed=false] .swatch{opacity:.35}
#energy .swatch{width:10px;height:10px;border-radius:2px}
#energy svg text{fill:var(--muted-foreground);font-size:11px}
#energy .grid{stroke:var(--border);stroke-width:1}
#energy .dot{stroke:var(--background);stroke-width:2}
#energy .stats{grid-template-columns:repeat(auto-fit,minmax(min(140px,100%),1fr))}
</style>
<div class="head">
  <h3>${t.title}</h3>
  <div class="nav nav-pills" role="tablist" aria-label="${t.title}">
    <button class="nav-link active" id="energy-usage" role="tab" aria-controls="energy-plot" aria-selected="true" type="button">${t.usage}</button>
    <button class="nav-link" id="energy-cost" role="tab" aria-controls="energy-plot" aria-selected="false" type="button">${t.cost}</button>
  </div>
</div>
<div class="legend" role="group" aria-label="${t.chartLabel}">
  <button type="button" aria-pressed="true" data-series="current"><span class="swatch" style="background:var(--viz-series-1)"></span>${t.thisYear}</button>
  <button type="button" aria-pressed="true" data-series="previous"><span class="swatch" style="background:var(--viz-series-2)"></span>${t.lastYear}</button>
</div>
<div id="energy-plot" role="tabpanel" aria-labelledby="energy-usage"></div>
<div class="viz-grid stats">
  <div class="card viz-stat"><span class="text-muted">${t.totalThis}</span><span class="viz-stat-value tabular-nums" data-stat="current"></span></div>
  <div class="card viz-stat"><span class="text-muted">${t.totalLast}</span><span class="viz-stat-value tabular-nums" data-stat="previous"></span></div>
  <div class="card viz-stat"><span class="text-muted">${t.change}</span><span class="viz-stat-value tabular-nums" data-stat="change"></span></div>
</div>
</div>
<script>
{
  const T = ${JSON.stringify(t)};
  const series = {
    current: { label: T.thisYear, color: 'var(--viz-series-1)', values: [455, 410, 370, 310, 270, 245, 265, 255, 280, 330, 380, 430] },
    previous: { label: T.lastYear, color: 'var(--viz-series-2)', values: [520, 470, 410, 330, 280, 250, 270, 265, 290, 350, 420, 495] }
  };
  const visible = new Set(['current', 'previous']);
  let mode = 'usage';
  const ns = 'http://www.w3.org/2000/svg';
  const make = (tag, attrs, parent) => { const node = document.createElementNS(ns, tag); for (const [key, value] of Object.entries(attrs)) node.setAttribute(key, value); parent.appendChild(node); return node; };
  const value = kwh => mode === 'usage' ? kwh : kwh * T.rate;
  const format = number => mode === 'usage' ? Math.round(number).toLocaleString() + ' ' + T.unit : T.symbol + number.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  const host = document.getElementById('energy-plot');
  const sum = values => values.reduce((total, item) => total + item, 0);
  const draw = () => {
    const width = Math.max(260, host.clientWidth);
    const height = 220;
    host.replaceChildren();
    const svg = make('svg', { viewBox: '0 0 ' + width + ' ' + height, width, height, role: 'img', 'aria-label': T.chartLabel }, host);
    const left = 52, right = width - 10, top = 10, bottom = height - 28;
    const max = mode === 'usage' ? 600 : 600 * T.rate;
    const x = index => left + (right - left) * index / 11;
    const y = number => bottom - (bottom - top) * number / max;
    for (let step = 0; step <= 4; step++) {
      const tick = max * step / 4;
      make('line', { class: 'grid', x1: left, x2: right, y1: y(tick), y2: y(tick) }, svg);
      make('text', { x: left - 8, y: y(tick) + 4, 'text-anchor': 'end' }, svg).textContent = mode === 'usage' ? Math.round(tick) : T.symbol + Math.round(tick);
    }
    const every = width < 480 ? 2 : 1;
    T.months.forEach((month, index) => { if (index % every === 0) make('text', { x: x(index), y: bottom + 18, 'text-anchor': 'middle' }, svg).textContent = month; });
    for (const [key, item] of Object.entries(series)) {
      if (!visible.has(key)) continue;
      make('path', { d: item.values.map((kwh, index) => (index ? 'L' : 'M') + x(index) + ' ' + y(value(kwh))).join(' '), fill: 'none', stroke: item.color, 'stroke-width': 2 }, svg);
      item.values.forEach((kwh, index) => make('circle', {
        class: 'dot', cx: x(index), cy: y(value(kwh)), r: 4.5, fill: item.color, tabindex: 0,
        'data-tooltip': item.label + ' · ' + T.months[index] + ' · ' + format(value(kwh)),
        'data-tooltip-placement': index > 9 ? 'left' : 'top'
      }, svg));
    }
    const current = sum(series.current.values), previous = sum(series.previous.values);
    document.querySelector('[data-stat="current"]').textContent = format(value(current));
    document.querySelector('[data-stat="previous"]').textContent = format(value(previous));
    document.querySelector('[data-stat="change"]').textContent = ((current - previous) / previous * 100).toFixed(1) + '%';
  };
  document.querySelectorAll('#energy .legend button').forEach(button => button.addEventListener('click', () => {
    const key = button.dataset.series;
    if (visible.has(key) && visible.size > 1) visible.delete(key); else visible.add(key);
    button.setAttribute('aria-pressed', String(visible.has(key)));
    draw();
  }));
  document.querySelectorAll('#energy [role="tab"]').forEach(tab => tab.addEventListener('click', () => { mode = tab.id === 'energy-cost' ? 'cost' : 'usage'; draw(); }));
  new ResizeObserver(draw).observe(host);
  draw();
}
</script>`
}

export interface MoonStrings {
  title: string
  topView: string
  skyView: string
  sun: string
  earth: string
  day: string
  play: string
  pause: string
  lit: string
  phases: string[]
  topLabel: string
  skyLabel: string
}

export function moonFragment(t: MoonStrings): string {
  return String.raw`<div id="moon" class="viz-root">
<style>
#moon{display:flex;flex-direction:column;gap:12px}
#moon h3{margin:0}
#moon .views{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(220px,100%),1fr));gap:12px}
#moon figure{margin:0;display:flex;flex-direction:column;align-items:center;gap:6px;padding:12px;border:1px solid var(--border);border-radius:10px}
#moon figcaption{color:var(--muted-foreground);font-size:12px}
#moon svg{width:100%;max-width:240px;height:auto}
#moon .orbit{fill:none;stroke:var(--border);stroke-dasharray:3 4}
#moon .ray{stroke:var(--yellow);stroke-width:1.5;opacity:.7}
#moon .lit{fill:#f4f1e6}
#moon .dark{fill:#3b3f47}
#moon .earth-day{fill:var(--blue)}
#moon .earth-night{fill:color-mix(in srgb,var(--blue) 35%,#111)}
#moon .label{fill:var(--muted-foreground);font-size:10px}
#moon .controls{display:flex;flex-wrap:wrap;align-items:center;gap:12px}
#moon .controls label{display:flex;flex:1;min-width:200px;flex-direction:column;gap:4px}
#moon output{font-weight:600}
</style>
<h3>${t.title}</h3>
<div class="views">
  <figure><svg viewBox="0 0 240 200" role="img" aria-label="${t.topLabel}" data-view="top"></svg><figcaption>${t.topView}</figcaption></figure>
  <figure><svg viewBox="0 0 200 200" role="img" aria-label="${t.skyLabel}" data-view="sky"></svg><figcaption>${t.skyView}</figcaption></figure>
</div>
<div class="controls">
  <button type="button" class="btn btn-primary" data-play>${t.play}</button>
  <label><span class="form-label tabular-nums" data-day></span><input class="form-range" type="range" min="0" max="29.5" step="0.5" value="4"></label>
</div>
<output aria-live="polite"></output>
</div>
<script>
{
  const T = ${JSON.stringify(t)};
  const PERIOD = 29.53;
  const root = document.getElementById('moon');
  const top = root.querySelector('[data-view="top"]');
  const sky = root.querySelector('[data-view="sky"]');
  const range = root.querySelector('input');
  const play = root.querySelector('[data-play]');
  const output = root.querySelector('output');
  const dayText = root.querySelector('[data-day]');
  let timer = 0;

  const phaseName = day => T.phases[Math.round(day / PERIOD * 8) % 8];
  const disk = (cx, cy, r, angle) => {
    const k = Math.cos(angle);
    const waxing = Math.sin(angle) >= 0;
    const rx = Math.abs(k) * r;
    const first = waxing ? 1 : 0;
    const second = waxing ? (k > 0 ? 0 : 1) : (k > 0 ? 1 : 0);
    return '<circle class="dark" cx="' + cx + '" cy="' + cy + '" r="' + r + '"/>' +
      '<path class="lit" d="M' + cx + ' ' + (cy - r) + ' A' + r + ' ' + r + ' 0 0 ' + first + ' ' + cx + ' ' + (cy + r) +
      ' A' + rx.toFixed(2) + ' ' + r + ' 0 0 ' + second + ' ' + cx + ' ' + (cy - r) + 'Z"/>';
  };
  const render = () => {
    const day = Number(range.value);
    const angle = 2 * Math.PI * day / PERIOD;
    const mx = 140 - 70 * Math.cos(angle), my = 100 + 70 * Math.sin(angle);
    const rays = [40, 70, 100, 130, 160].map(y => '<line class="ray" x1="6" y1="' + y + '" x2="38" y2="' + y + '"/>').join('');
    top.innerHTML = rays + '<text class="label" x="6" y="22">' + T.sun + ' →</text>' +
      '<circle class="orbit" cx="140" cy="100" r="70"/>' +
      '<circle class="earth-night" cx="140" cy="100" r="16"/><path class="earth-day" d="M140 84 A16 16 0 0 0 140 116Z"/>' +
      '<text class="label" x="140" y="132" text-anchor="middle">' + T.earth + '</text>' +
      '<circle class="dark" cx="' + mx.toFixed(1) + '" cy="' + my.toFixed(1) + '" r="8"/>' +
      '<path class="lit" d="M' + mx.toFixed(1) + ' ' + (my - 8).toFixed(1) + ' A8 8 0 0 0 ' + mx.toFixed(1) + ' ' + (my + 8).toFixed(1) + 'Z"/>';
    sky.innerHTML = disk(100, 100, 72, angle);
    const lit = Math.round((1 - Math.cos(angle)) / 2 * 100);
    dayText.textContent = T.day.replace('{n}', day.toFixed(1));
    output.textContent = phaseName(day) + ' · ' + T.lit.replace('{percent}', String(lit));
  };
  const stop = () => { clearInterval(timer); timer = 0; play.textContent = T.play; };
  play.addEventListener('click', () => {
    if (timer) { stop(); return; }
    play.textContent = T.pause;
    timer = setInterval(() => {
      const next = Number(range.value) + 0.5;
      range.value = String(next > 29.5 ? 0 : next);
      range.dispatchEvent(new Event('input', { bubbles: true }));
    }, 160);
  });
  range.addEventListener('input', render);
  render();
}
</script>`
}

export interface TimerStrings {
  designs: string
  minimal: string
  ring: string
  compact: string
  focus: string
  deepWork: string
  session: string
  start: string
  pause: string
  resume: string
  reset: string
  accent: string
  digits: string
  radius: string
  thickness: string
  details: string
  layout: string
  inline: string
  stacked: string
}

export function timerFragment(t: TimerStrings): string {
  return String.raw`<div id="timer" class="viz-root" aria-label="${t.designs}">
<style>
#timer{--tm-ink:light-dark(#1d2026,#ecedf0);--tm-sub:light-dark(#646a73,#9aa1ab);--tm-surface:light-dark(#ffffff,#1d1f23);--tm-line:light-dark(#e3e5e9,#30333a);--tm-track:light-dark(#eceef2,#2a2d33);--tm-accent:#2f7d6d}
#timer .card button{all:unset;box-sizing:border-box;cursor:var(--cursor-interaction)}
#timer .card button:focus-visible{outline:2px solid var(--tm-accent);outline-offset:2px}
#timer .stage{display:flex;min-height:220px;align-items:center;justify-content:center}
#timer .card{border:1px solid var(--tm-line);background:var(--tm-surface);box-shadow:0 10px 28px rgb(0 0 0 / 14%);color:var(--tm-ink);font-family:system-ui,-apple-system,"Segoe UI",sans-serif}
#timer .card .go{padding:7px 16px;border-radius:999px;background:var(--tm-accent);color:#fff;font-size:13px;font-weight:600}
#timer .card .ghost{padding:7px 12px;border-radius:999px;color:var(--tm-sub);font-size:13px}
#tm-minimal{width:min(280px,100%);padding:20px;border-radius:var(--tm-radius,16px);text-align:center;box-sizing:border-box}
#tm-minimal .label{color:var(--tm-sub);font-size:12px;letter-spacing:.06em;text-transform:uppercase}
#tm-minimal .time{margin:6px 0 14px;font-size:var(--tm-digits,44px);font-weight:500;font-variant-numeric:tabular-nums;line-height:1.1}
#tm-minimal .actions{display:flex;justify-content:center;gap:8px}
#tm-ring{display:flex;width:min(340px,100%);align-items:center;gap:18px;padding:18px;border-radius:16px;box-sizing:border-box}
#tm-ring svg{flex:none}
#tm-ring .track{stroke:var(--tm-track)}
#tm-ring .arc{stroke:var(--tm-accent);transition:stroke-dashoffset .3s linear}
#tm-ring .center{fill:var(--tm-ink);font-size:15px;font-weight:600;font-variant-numeric:tabular-nums}
#tm-ring .meta{display:flex;min-width:0;flex-direction:column;align-items:flex-start;gap:8px}
#tm-ring h4{margin:0;font-size:15px;font-weight:600}
#tm-ring p{margin:0;color:var(--tm-sub);font-size:13px}
#tm-compact{display:flex;width:min(420px,100%);align-items:center;gap:12px;padding:8px 8px 8px 16px;border-radius:999px;box-sizing:border-box}
#tm-compact[data-layout=stacked]{flex-direction:column;align-items:stretch;padding:14px;border-radius:14px}
#tm-compact .time{font-weight:600;font-variant-numeric:tabular-nums}
#tm-compact .bar{height:6px;flex:1;overflow:hidden;border-radius:999px;background:var(--tm-track)}
#tm-compact[data-layout=stacked] .bar{flex:none}
#tm-compact .fill{width:0;height:100%;background:var(--tm-accent)}
@media (max-width:360px){#timer .stage{min-height:276px}#tm-ring{flex-direction:column;text-align:center}#tm-ring .meta{align-items:center}}
@media (prefers-reduced-motion:reduce){#tm-ring .arc{transition:none}}
</style>
<div class="viz-carousel viz-dotted-background" aria-label="${t.designs}">
  <section data-variant="${t.minimal}" aria-label="${t.minimal}">
    <div class="stage"><div id="tm-minimal" class="card" aria-label="${t.minimal}">
      <div class="label">${t.focus}</div>
      <div class="time" data-time>25:00</div>
      <div class="actions"><button type="button" class="go" data-toggle>${t.start}</button><button type="button" class="ghost" data-reset>${t.reset}</button></div>
    </div></div>
  </section>
  <section data-variant="${t.ring}" aria-label="${t.ring}" hidden>
    <div class="stage"><div id="tm-ring" class="card" aria-label="${t.ring}">
      <svg width="96" height="96" viewBox="0 0 96 96" aria-hidden="true"><circle class="track" cx="48" cy="48" r="40" fill="none" stroke-width="8"></circle><circle class="arc" cx="48" cy="48" r="40" fill="none" stroke-width="8" stroke-linecap="round" transform="rotate(-90 48 48)" pathLength="100" stroke-dasharray="100" stroke-dashoffset="0"></circle><text class="center" x="48" y="53" text-anchor="middle" data-time>25:00</text></svg>
      <div class="meta"><h4>${t.deepWork}</h4><p data-session>${t.session}</p><button type="button" class="go" data-toggle>${t.start}</button></div>
    </div></div>
  </section>
  <section data-variant="${t.compact}" aria-label="${t.compact}" hidden>
    <div class="stage"><div id="tm-compact" class="card" aria-label="${t.compact}">
      <span class="time" data-time>25:00</span><div class="bar"><div class="fill" data-fill></div></div><button type="button" class="go" data-toggle>${t.start}</button>
    </div></div>
  </section>
</div>
</div>
<script>
{
  const T = ${JSON.stringify(t)};
  const LENGTH = 25 * 60;
  for (const card of document.querySelectorAll('#timer .card')) {
    let remaining = LENGTH;
    let handle = 0;
    const toggle = card.querySelector('[data-toggle]');
    const draw = () => {
      const text = String(Math.floor(remaining / 60)).padStart(2, '0') + ':' + String(remaining % 60).padStart(2, '0');
      card.querySelectorAll('[data-time]').forEach(node => { node.textContent = text; });
      const done = (LENGTH - remaining) / LENGTH;
      card.querySelector('.arc')?.setAttribute('stroke-dashoffset', String(100 * done));
      const fill = card.querySelector('[data-fill]');
      if (fill) fill.style.width = (done * 100).toFixed(1) + '%';
      toggle.textContent = handle ? T.pause : remaining < LENGTH ? T.resume : T.start;
    };
    toggle.addEventListener('click', () => {
      if (handle) { clearInterval(handle); handle = 0; }
      else handle = setInterval(() => { remaining = Math.max(0, remaining - 1); draw(); }, 1000);
      draw();
    });
    card.querySelector('[data-reset]')?.addEventListener('click', () => { clearInterval(handle); handle = 0; remaining = LENGTH; draw(); });
    draw();
  }
  const root = document.getElementById('timer');
  const minimalCard = document.getElementById('tm-minimal');
  const ringCard = document.getElementById('tm-ring');
  const compactCard = document.getElementById('tm-compact');
  const theme = { accent: '#2f7d6d' };
  const minimal = { digits: 44, radius: 16 };
  const ring = { thickness: 8, session: true };
  const compact = { layout: T.inline };
  const paint = () => {
    root.style.setProperty('--tm-accent', theme.accent);
    minimalCard.style.setProperty('--tm-digits', minimal.digits + 'px');
    minimalCard.style.setProperty('--tm-radius', minimal.radius + 'px');
    ringCard.querySelectorAll('circle').forEach(circle => circle.setAttribute('stroke-width', String(ring.thickness)));
    ringCard.querySelector('[data-session]').hidden = !ring.session;
    compactCard.dataset.layout = compact.layout === T.stacked ? 'stacked' : 'inline';
  };
  paint();
  if (globalThis.Tweak) {
    new Tweak({ container: root, onChange: paint })
      .addColorPicker(theme, 'accent', { label: T.accent, reference: '--tm-accent' });
    new Tweak({ container: minimalCard, onChange: paint })
      .addSlider(minimal, 'digits', { label: T.digits, min: 32, max: 64, unit: 'px', reference: '--tm-digits' })
      .addSlider(minimal, 'radius', { label: T.radius, min: 0, max: 28, unit: 'px', reference: '--tm-radius' });
    new Tweak({ container: ringCard, onChange: paint })
      .addSlider(ring, 'thickness', { label: T.thickness, min: 4, max: 14, unit: 'px', reference: 'ring.strokeWidth' })
      .addToggle(ring, 'session', { label: T.details, reference: 'ring.session' });
    new Tweak({ container: compactCard, onChange: paint })
      .addSelect(compact, 'layout', { label: T.layout, options: [T.inline, T.stacked], reference: 'compact.layout' });
  }
}
</script>`
}
