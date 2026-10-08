import type { Platform } from '../types/capture';

export const TOOLBAR_HEIGHT = 48;

const STYLE_ID = 'crm-capture-offset-style';
export const OFFSET_CLASS = 'crm-capture-offset';

const PLATFORM_CLASS: Record<Platform, string> = {
  WhatsApp: 'crm-platform-whatsapp',
  Instagram: 'crm-platform-instagram',
};

const ALL_PLATFORM_CLASSES = Object.values(PLATFORM_CLASS);

/**
 * Pushes the host page down by the toolbar height instead of overlaying it.
 *
 * A plain `margin-top` on <html> is not enough. WhatsApp's `#app` is
 * `position: absolute` with no positioned ancestor, so it resolves against the
 * initial containing block — the viewport — and slides straight back under the
 * toolbar at `top: 0`. Making <html> `position: relative` turns it into that
 * containing block, and clamping the app's `100vh` height stops it overflowing
 * off the bottom of the screen by the same 48px.
 *
 * Instagram scrolls in normal document flow, so it only needs the offset; a
 * height clamp there would break page scrolling. Rules are therefore scoped
 * per platform. Instagram's Messages pages are the exception: see
 * watchInstagramLayout().
 */
export function applyPageOffset(platform: Platform): void {
  const root = document.documentElement;

  if (!document.getElementById(STYLE_ID)) {
    const style = document.createElement('style');
    style.id = STYLE_ID;
    style.textContent = `
      html.${OFFSET_CLASS} {
        margin-top: ${TOOLBAR_HEIGHT}px !important;
        position: relative !important;
      }

      /* WhatsApp: fixed-height app shell that must fit the remaining space. */
      html.${OFFSET_CLASS}.${PLATFORM_CLASS.WhatsApp} {
        height: calc(100% - ${TOOLBAR_HEIGHT}px) !important;
      }
      html.${OFFSET_CLASS}.${PLATFORM_CLASS.WhatsApp} > body {
        height: 100% !important;
        min-height: 0 !important;
      }
      html.${OFFSET_CLASS}.${PLATFORM_CLASS.WhatsApp} > body > * {
        max-height: 100% !important;
      }

      /*
       * Instagram Messages: a fixed, full-window app that a margin can't move. A transform on
       * <body> makes it the containing block for position: fixed, so the app sits below the
       * toolbar; the page itself doesn't scroll there.
       */
      html.${OFFSET_CLASS}.${INSTAGRAM_APP_CLASS} {
        height: calc(100% - ${TOOLBAR_HEIGHT}px) !important;
      }
      html.${OFFSET_CLASS}.${INSTAGRAM_APP_CLASS} > body {
        height: 100% !important;
        min-height: 0 !important;
        overflow: hidden !important;
        transform: translateZ(0) !important;
      }
    `;
    (document.head ?? root).appendChild(style);
  }

  root.classList.add(OFFSET_CLASS);
  root.classList.remove(...ALL_PLATFORM_CLASSES);
  root.classList.add(PLATFORM_CLASS[platform]);
}

export function removePageOffset(): void {
  const root = document.documentElement;
  root.classList.remove(OFFSET_CLASS, INSTAGRAM_APP_CLASS, ...ALL_PLATFORM_CLASSES);
  document.getElementById(STYLE_ID)?.remove();
  releaseViewportHeights();
}

const INSTAGRAM_APP_CLASS = 'crm-instagram-app';
const CLAMP_ATTR = 'data-crm-capture-clamped';
/** How far into the page to look for full-window panels (they sit near the top of the tree). */
const MAX_DEPTH = 12;
const MAX_NODES = 1500;

/** Instagram's Messages pages, which are laid out as an app rather than a scrolling page. */
function isInstagramApp(): boolean {
  return location.pathname.startsWith('/direct');
}

/**
 * Keeps Instagram's Messages pages below the toolbar. Instagram navigates without reloading, so
 * the page type is checked as you move around. Only layout is touched; page content is never read.
 */
export function watchInstagramLayout(): () => void {
  const tick = () => {
    const root = document.documentElement;
    if (!root.classList.contains(OFFSET_CLASS) || !isInstagramApp()) {
      if (root.classList.contains(INSTAGRAM_APP_CLASS)) {
        root.classList.remove(INSTAGRAM_APP_CLASS);
        releaseViewportHeights();
      }
      return;
    }
    root.classList.add(INSTAGRAM_APP_CLASS);
    fitViewportHeights();
  };
  tick();
  const timer = window.setInterval(tick, 700);
  window.addEventListener('resize', tick);
  return () => {
    window.clearInterval(timer);
    window.removeEventListener('resize', tick);
  };
}

/**
 * Panels sized to the full window (100vh) would still run 48px past the bottom, hiding the message
 * box. Shrinks exactly those by the toolbar's height.
 */
function fitViewportHeights(): void {
  const full = window.innerHeight;
  const queue: [Element, number][] = [[document.body, 0]];
  let visited = 0;
  while (queue.length > 0 && visited < MAX_NODES) {
    const [el, depth] = queue.shift()!;
    visited++;
    if (el instanceof HTMLElement && el !== document.body && !el.hasAttribute(CLAMP_ATTR)) {
      const height = parseFloat(getComputedStyle(el).height);
      if (Math.abs(height - full) <= 1) {
        el.style.setProperty('max-height', `calc(100vh - ${TOOLBAR_HEIGHT}px)`, 'important');
        el.setAttribute(CLAMP_ATTR, '');
      }
    }
    if (depth < MAX_DEPTH) for (const child of el.children) queue.push([child, depth + 1]);
  }
}

function releaseViewportHeights(): void {
  document.querySelectorAll<HTMLElement>(`[${CLAMP_ATTR}]`).forEach((el) => {
    el.style.removeProperty('max-height');
    el.removeAttribute(CLAMP_ATTR);
  });
}
