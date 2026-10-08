import { useCallback, useEffect, useRef, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';

import { displayValue, enabledFields, fieldKind, saveBlocker, type CaptureSession, type FieldValue, type Platform } from '../types/capture';
import type { ConfigBundle } from '../types/growdesk';
import type { ContentMessage, SaveResponse, StateResponse } from '../types/messages';
import { isStatePush } from '../types/messages';
import { detectPlatform } from '../utils/platform';
import { applyPageOffset, OFFSET_CLASS, removePageOffset, TOOLBAR_HEIGHT, watchInstagramLayout } from './pageOffset';
import type { ConfigField } from '../types/growdesk';
import { mergeHighlight, normalizeSelection } from '../utils/normalize';
import { BoxPicker } from './toolbar/BoxPicker';
import { SavedCard, type SavedLead } from './toolbar/SavedCard';
import { Toolbar, type StatusMessage } from './toolbar/Toolbar';
import toolbarCss from './toolbar/toolbar.css?inline';

const HOST_ID = 'growdesk-capture-toolbar-host';
const FLASH_MS = 2200;
/** How often the field setup is re-read while the tab is visible, so admin changes show up. */
const REFRESH_MS = 60_000;

const UNREACHABLE = 'GrowDesk Capture stopped responding. Reload the page.';

/** Promise wrapper around sendMessage that never rejects into React. */
async function request<T>(message: ContentMessage, onFailure: (error: string) => T): Promise<T> {
  try {
    const response = (await chrome.runtime.sendMessage(message)) as T | undefined;
    return response ?? onFailure('No response from the extension.');
  } catch (error) {
    console.error('[GrowDesk Capture] Background unreachable.', error);
    return onFailure(UNREACHABLE);
  }
}

const send = (message: ContentMessage): Promise<StateResponse> =>
  request(message, (error) => ({ ok: false, session: null, bundle: null, configured: true, error }));

const sendSave = (): Promise<SaveResponse> => request({ type: 'GD_SAVE' }, (error) => ({ ok: false, error }));

function App({ platform }: { platform: Platform }) {
  const [session, setSession] = useState<CaptureSession | null>(null);
  const [bundle, setBundle] = useState<ConfigBundle | null>(null);
  const [configured, setConfigured] = useState(true);
  const [installType, setInstallType] = useState<string | undefined>();
  const [status, setStatus] = useState<StatusMessage | null>(null);
  const [errorField, setErrorField] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState<SavedLead | null>(null);
  /** "Draw a box around it" in progress, for the field whose chip it was chosen from. */
  const [drawing, setDrawing] = useState<string | null>(null);
  const timer = useRef<number | null>(null);

  /** Confirmations fade; errors and warnings stay until the next action, so they can't be missed. */
  const show = useCallback((text: string, tone: StatusMessage['tone'] = 'success') => {
    if (timer.current !== null) window.clearTimeout(timer.current);
    timer.current = null;
    setStatus({ text, tone });
    if (tone === 'success') {
      timer.current = window.setTimeout(() => {
        setStatus(null);
        timer.current = null;
      }, FLASH_MS);
    }
  }, []);

  const apply = useCallback((r: StateResponse) => {
    setSession(r.session);
    if (r.bundle) setBundle(r.bundle);
    setConfigured(r.configured);
    if (r.installType) setInstallType(r.installType);
  }, []);

  // Re-read the field setup: on load, when the tab comes back into view, and every minute while
  // it is visible. A change made in GrowDesk (e.g. a newly required field) shows up by itself.
  useEffect(() => {
    let cancelled = false;
    const refresh = () => {
      if (document.visibilityState !== 'visible') return;
      void send({ type: 'GD_REFRESH', platform }).then((r) => !cancelled && apply(r));
    };
    void send({ type: 'GD_GET_STATE', platform }).then((r) => {
      if (cancelled) return;
      apply(r);
      refresh();
    });
    const interval = window.setInterval(refresh, REFRESH_MS);
    document.addEventListener('visibilitychange', refresh);
    window.addEventListener('focus', refresh);
    return () => {
      cancelled = true;
      window.clearInterval(interval);
      document.removeEventListener('visibilitychange', refresh);
      window.removeEventListener('focus', refresh);
    };
  }, [platform, apply]);

  // State pushed by the service worker after a right-click capture or a new field setup.
  useEffect(() => {
    const listener = (message: unknown) => {
      if (!isStatePush(message)) return;
      setSession(message.session);
      if (message.bundle) setBundle(message.bundle);
      if (message.configured !== undefined) setConfigured(message.configured);
      if (message.flash) show(message.flash, message.flashTone ?? 'success');
    };
    chrome.runtime.onMessage.addListener(listener);
    return () => chrome.runtime.onMessage.removeListener(listener);
  }, [show]);

  useEffect(() => () => {
    if (timer.current !== null) window.clearTimeout(timer.current);
  }, []);

  const handleStart = useCallback(async () => {
    if (!configured) {
      show('Connect GrowDesk Capture first: click Connect.', 'error');
      return;
    }
    setBusy(true);
    setErrorField(null);
    show('Connecting to GrowDesk…', 'pending');
    const r = await send({ type: 'GD_START', platform });
    setBusy(false);
    apply(r);
    if (!r.ok) {
      show(r.error ?? 'Could not start capturing.', 'error');
      return;
    }
    show('Capturing: highlight text, right-click, then choose GrowDesk Capture.', 'success');
  }, [platform, apply, show, configured]);

  const handleStop = useCallback(async () => {
    const blocker = saveBlocker(session, bundle);
    if (blocker) {
      show(blocker, 'error');
      return;
    }
    setBusy(true);
    show('Saving to GrowDesk…', 'pending');
    const r = await sendSave();
    setBusy(false);

    if (!r.ok) {
      // The session is kept, so STOP can simply be pressed again once it's fixed.
      setErrorField(r.field ?? null);
      const fresh = await send({ type: 'GD_GET_STATE', platform });
      apply(fresh);
      show(r.error ?? 'Could not save to GrowDesk. Press STOP to try again.', 'error');
      return;
    }

    setSaved(savedLead(session!, bundle!, r));
    setSession(null);
    setErrorField(null);
    const text = `✓ ${r.message ?? 'Saved to GrowDesk.'}`;
    if (r.warnings?.length) show(`${text} ${r.warnings.join(' ')}`, 'warning');
    else show(text, 'success');
  }, [session, bundle, platform, apply, show]);

  const handleDiscard = useCallback(async () => {
    const r = await send({ type: 'GD_DISCARD' });
    apply(r);
    setErrorField(null);
    show('Capture discarded.', 'success');
  }, [apply, show]);

  const handleSetValue = useCallback(
    async (key: string, value: FieldValue | null) => {
      if (errorField === key) setErrorField(null);
      const r = await send({ type: 'GD_SET_VALUE', key, value });
      if (r.ok) apply(r);
      else show(r.error ?? 'Could not keep that value.', 'error');
    },
    [apply, errorField, show],
  );

  const drawingField = drawing ? (enabledFields(bundle).find((f) => f.key === drawing && fieldKind(f) === 'highlight') ?? null) : null;

  const captureDrawn = async (field: ConfigField, text: string) => {
    setDrawing(null);
    const value = normalizeSelection(field, text);
    const next = mergeHighlight(field, session?.values[field.key], value);
    const r = await send({ type: 'GD_SET_VALUE', key: field.key, value: next });
    if (!r.ok) {
      show(r.error ?? 'Could not keep that value.', 'error');
      return;
    }
    apply(r);
    if (errorField === field.key) setErrorField(null);
    show(`${field.label}: ${value}`, 'success');
  };

  return (
    <>
      {drawingField && (
        <div className="gd">
          <BoxPicker
            field={drawingField}
            skip={document.getElementById(HOST_ID)}
            onCapture={(f, t) => void captureDrawn(f, t)}
            onCancel={(message) => {
              setDrawing(null);
              if (message) show(message, 'error');
            }}
          />
        </div>
      )}
      <Toolbar
        session={session}
        bundle={bundle}
        configured={configured}
        platform={platform}
        status={status}
        busy={busy}
        errorField={errorField}
        onStart={() => void handleStart()}
        onStop={() => void handleStop()}
        onDiscard={() => void handleDiscard()}
        onSetValue={(k, v) => void handleSetValue(k, v)}
        onOpenSettings={() => void send({ type: 'GD_OPEN_SETTINGS' })}
        onOpenGuide={() => void send({ type: 'GD_OPEN_GUIDE' })}
        onDraw={(key) => setDrawing(key)}
        installType={installType}
        onReloadExtension={() => void send({ type: 'GD_RELOAD_EXTENSION' })}
        onOpenExtensions={() => void send({ type: 'GD_OPEN_EXTENSIONS' })}
      />
      {saved && (
        <div className="gd">
          <SavedCard key={`${saved.customerId}-${saved.action}`} lead={saved} onClose={() => setSaved(null)} />
        </div>
      )}
    </>
  );
}

/** What the "Saved in GrowDesk" card shows, from the capture that was just sent. */
function savedLead(session: CaptureSession, bundle: ConfigBundle, r: SaveResponse): SavedLead {
  const field = (key: string) => enabledFields(bundle).find((f) => f.key === key);
  const shown = (key: string) => {
    const f = field(key);
    return f ? displayValue(f, session.values[key], bundle) || undefined : undefined;
  };
  const instagram = shown('instagram');
  return {
    customerId: r.customerId ?? 0,
    name: r.customerName ?? shown('name') ?? 'Customer',
    action: r.action ?? 'created',
    contact: shown('whatsapp') ?? (instagram ? `@${instagram.replace(/^@/, '')}` : undefined),
    treatments: shown('treatments'),
    stage: shown('stage'),
    warnings: r.warnings ?? [],
    url: `${bundle.server}/customers/${r.customerId ?? ''}`,
  };
}

/** Builds the shadow host element that carries the toolbar. */
function createHost(): HTMLDivElement {
  const host = document.createElement('div');
  host.id = HOST_ID;
  host.setAttribute('data-growdesk-capture', 'toolbar');
  // Inline styles keep the host itself immune to page stylesheets.
  host.style.cssText = [
    'position:fixed',
    'top:0',
    'left:0',
    'right:0',
    'width:100%',
    'height:' + TOOLBAR_HEIGHT + 'px',
    'margin:0',
    'padding:0',
    'border:0',
    'z-index:2147483647',
    'pointer-events:auto',
    'color-scheme:light dark',
  ].join(';');
  return host;
}

let observer: MutationObserver | null = null;

/**
 * WhatsApp and Instagram rewrite large parts of the DOM as you navigate. This watches only the
 * direct children of <html> - it never reads page content - and re-attaches the single toolbar
 * host if the site detaches it.
 */
function keepMounted(host: HTMLElement, platform: Platform): void {
  observer?.disconnect();
  let scheduled = false;

  observer = new MutationObserver(() => {
    if (scheduled) return;
    scheduled = true;
    requestAnimationFrame(() => {
      scheduled = false;
      document.querySelectorAll('#' + HOST_ID).forEach((node) => {
        if (node !== host) node.remove();
      });
      if (!host.isConnected) document.documentElement.appendChild(host);
      if (!document.documentElement.classList.contains(OFFSET_CLASS)) applyPageOffset(platform);
    });
  });

  observer.observe(document.documentElement, { childList: true });
}

function mount(): void {
  if (document.getElementById(HOST_ID)) return;

  const platform = detectPlatform();
  if (!platform) {
    console.warn('[GrowDesk Capture] Unsupported page - toolbar not injected.');
    return;
  }

  const host = createHost();
  const shadow = host.attachShadow({ mode: 'open' });

  const style = document.createElement('style');
  style.textContent = toolbarCss;
  shadow.appendChild(style);

  const mountPoint = document.createElement('div');
  shadow.appendChild(mountPoint);

  document.documentElement.appendChild(host);
  applyPageOffset(platform);

  let root: Root;
  try {
    root = createRoot(mountPoint);
    root.render(<App platform={platform} />);
  } catch (error) {
    console.error('[GrowDesk Capture] Failed to render toolbar.', error);
    host.remove();
    removePageOffset();
    return;
  }

  keepMounted(host, platform);
  const stopLayoutWatch = platform === 'Instagram' ? watchInstagramLayout() : () => undefined;

  window.addEventListener(
    'pagehide',
    () => {
      observer?.disconnect();
      observer = null;
      stopLayoutWatch();
      try {
        root.unmount();
      } catch {
        // The page is going away regardless.
      }
      host.remove();
      removePageOffset();
    },
    { once: true },
  );
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', mount, { once: true });
} else {
  mount();
}
