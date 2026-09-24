export interface ComputerUsePillStrings {
  usingComputer: string
  escToCancel: string
}

export const COMPUTER_USE_PILL_WIDTH = 380
export const COMPUTER_USE_PILL_HEIGHT = 44

function escapeHtml(value: string): string {
  return value.replace(/[&<>"']/g, (char) => `&#${char.charCodeAt(0)};`)
}

export function computerUsePillHtml(strings: ComputerUsePillStrings): string {
  return `<!doctype html><html><head><meta charset="utf-8"><style>
html,body{margin:0;height:100%;background:transparent;overflow:hidden;font:500 13px/1 "Segoe UI Variable Text","Segoe UI",system-ui,sans-serif;}
.pill{box-sizing:border-box;height:100%;margin:0 auto;display:flex;align-items:center;justify-content:center;gap:10px;padding:0 18px;border-radius:22px;
color:#eeeeec;border:1px solid transparent;width:max-content;max-width:100%;
background:linear-gradient(rgba(20,21,21,.94),rgba(20,21,21,.94)) padding-box,linear-gradient(48deg,#2458f7,#5f82f7 46%,#8fa5ff) border-box;}
.dot{width:8px;height:8px;border-radius:50%;background:#5f82f7;box-shadow:0 0 8px rgba(95,130,247,.7);animation:pulse 1.6s ease-in-out infinite;}
.sep{opacity:.5}.hint{opacity:.72}
@keyframes pulse{0%,100%{opacity:1}50%{opacity:.35}}
@media (prefers-reduced-motion:reduce){.dot{animation:none}}
</style></head><body><div class="pill"><span class="dot"></span><span>${escapeHtml(strings.usingComputer)}</span><span class="sep">·</span><span class="hint">${escapeHtml(strings.escToCancel)}</span></div></body></html>`
}

export function computerUseGlowHtml(): string {
  return `<!doctype html><html><head><meta charset="utf-8"><style>
html,body{margin:0;height:100%;background:transparent;overflow:hidden;}
.glow{position:fixed;inset:0;box-shadow:inset 0 0 0 2px rgba(95,130,247,.9),inset 0 0 28px 4px rgba(36,88,247,.45),inset 0 0 72px 12px rgba(143,165,255,.2);animation:breathe 2.8s ease-in-out infinite;}
@keyframes breathe{0%,100%{opacity:.6}50%{opacity:1}}
@media (prefers-reduced-motion:reduce){.glow{animation:none}}
</style></head><body><div class="glow"></div></body></html>`
}
