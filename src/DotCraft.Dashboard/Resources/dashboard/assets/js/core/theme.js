import { onAction } from './actions.js';
import { icon } from './html.js';

const STORAGE_KEY = 'dotcraft.dashboard.theme';
const prefersLight = window.matchMedia('(prefers-color-scheme: light)');
let chosen = readChoice();

function readChoice() {
  try {
    const value = localStorage.getItem(STORAGE_KEY);
    return value === 'light' || value === 'dark' ? value : null;
  } catch {
    return null;
  }
}

function currentTheme() {
  return chosen ?? (prefersLight.matches ? 'light' : 'dark');
}

function applyTheme() {
  const theme = currentTheme();
  document.documentElement.dataset.theme = theme;
  const toggle = document.getElementById('themeToggle');
  toggle.innerHTML = icon(theme === 'dark' ? 'moon' : 'sun', 15);
  toggle.setAttribute('aria-label', theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme');
}

export function initTheme() {
  applyTheme();
  prefersLight.addEventListener('change', applyTheme);
}

onAction('click', {
  'toggle-theme': () => {
    chosen = currentTheme() === 'dark' ? 'light' : 'dark';
    try {
      localStorage.setItem(STORAGE_KEY, chosen);
    } catch {
      // Storage can be unavailable; the choice still applies for this page.
    }
    applyTheme();
  }
});
