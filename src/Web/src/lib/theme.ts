/**
 * Light or dark: the visitor's choice (localStorage), else the system setting. Layout.astro inlines the same logic in
 * the head so the theme is set before the first paint; storage can throw (private windows, blocked site data).
 */
const KEY = 'theme';

export function storedTheme(): string | null {
  try {
    return localStorage.getItem(KEY);
  } catch {
    return null;
  }
}

export function storeTheme(theme: 'light' | 'dark') {
  try {
    localStorage.setItem(KEY, theme);
  } catch {
    // The choice then lasts for this page only.
  }
}

export function applyTheme() {
  const theme = storedTheme() ?? (matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
  document.documentElement.classList.toggle('dark', theme === 'dark');
}
