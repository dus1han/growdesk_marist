import { buildRequest, createSession, enabledFields, saveBlocker, type CaptureSession, type Platform } from '../types/capture';
import type { ConfigBundle } from '../types/growdesk';
import type { ContentMessage, SaveResponse, StatePushMessage, StateResponse } from '../types/messages';
import { clearSession, isTabCapturing, readSession, writeSession } from '../storage/captureSession';
import { guideUrl, isConfigured, readSettings } from '../storage/settings';
import { cachedBundle, GrowDeskError, loadBundle, sendLead } from '../api/growdesk';
import { updateRequired } from '../utils/version';
import { fieldKeyFromMenuId, rebuildMenus, setMenusVisible } from './contextMenus';
import { mergeHighlight, normalizeSelection } from '../utils/normalize';
import { platformFromUrl } from '../utils/platform';

const BUNDLE_KEY = 'growdesk-capture-config';
const SETTINGS_KEY = 'growdesk-capture-settings';

/** Pushes state (and an optional flash message) down to one tab's toolbar. */
async function pushState(tabId: number, session: CaptureSession | null, flash?: string, flashTone: StatePushMessage['flashTone'] = 'success'): Promise<void> {
  const message: StatePushMessage = {
    type: 'GD_STATE',
    session,
    bundle: await cachedBundle(),
    configured: isConfigured(await readSettings()),
    flash,
    flashTone,
  };
  try {
    await chrome.tabs.sendMessage(tabId, message);
  } catch {
    // The content script may not be injected yet (or the tab is gone). It re-requests state on
    // mount, so dropping this push is safe.
  }
}

/** Keeps the global menu visibility in step with the currently focused tab. */
async function syncMenusForActiveTab(): Promise<void> {
  try {
    const [tab] = await chrome.tabs.query({ active: true, lastFocusedWindow: true });
    const capturing = tab?.id != null && (await isTabCapturing(tab.id));
    await setMenusVisible(Boolean(capturing));
  } catch (error) {
    console.error('[GrowDesk Capture] Failed to sync context menus.', error);
    await setMenusVisible(false);
  }
}

async function restoreMenus(): Promise<void> {
  await rebuildMenus(await cachedBundle());
  await syncMenusForActiveTab();
}

chrome.runtime.onInstalled.addListener((details) => {
  void restoreMenus();
  // After any update (Chrome's own, chrome://extensions, or the toolbar's Reload) the toolbar in
  // open tabs is cut off from the extension. Refresh the WhatsApp / Instagram tabs that are
  // already open (never opens new ones) so they run the new version.
  if (details.reason === 'update') {
    void chrome.tabs.query({ url: ['https://web.whatsapp.com/*', 'https://www.instagram.com/*'] }).then((tabs) => {
      for (const tab of tabs) if (tab.id != null) void chrome.tabs.reload(tab.id);
    });
  }
  // First install: nothing works until GrowDesk is connected, so open the settings.
  if (details.reason === 'install') chrome.runtime.openOptionsPage();
});
chrome.runtime.onStartup.addListener(() => void restoreMenus());
chrome.tabs.onActivated.addListener(() => void syncMenusForActiveTab());
chrome.windows.onFocusChanged.addListener(() => void syncMenusForActiveTab());
chrome.tabs.onRemoved.addListener((tabId) => void clearSession(tabId));

// A new field setup (fetched on START or by the settings page) changes the right-click menu.
// Connecting in the settings, or a new field setup, is pushed to every open WhatsApp and
// Instagram tab at once, so a tab opened earlier doesn't keep saying "Not connected".
chrome.storage.onChanged.addListener((changes, area) => {
  if (area !== 'local') return;
  if (BUNDLE_KEY in changes) {
    void rebuildMenus((changes[BUNDLE_KEY].newValue as ConfigBundle | undefined) ?? null).then(syncMenusForActiveTab);
  }
  if (BUNDLE_KEY in changes || SETTINGS_KEY in changes) void pushToCaptureTabs();
});

async function pushToCaptureTabs(): Promise<void> {
  const tabs = await chrome.tabs.query({ url: ['https://web.whatsapp.com/*', 'https://www.instagram.com/*'] }).catch(() => []);
  for (const tab of tabs) {
    if (tab.id != null) await pushState(tab.id, await readSession(tab.id));
  }
}

// The toolbar icon has no popup: a click opens the settings.
chrome.action.onClicked.addListener(() => chrome.runtime.openOptionsPage());

/** A right-click on "GrowDesk Capture → Set as …". */
chrome.contextMenus.onClicked.addListener((info, tab) => {
  const key = fieldKeyFromMenuId(info.menuItemId);
  const tabId = tab?.id;
  if (!key || tabId == null) return;

  void (async () => {
    const session = await readSession(tabId);
    if (!session?.active) {
      await syncMenusForActiveTab();
      await pushState(tabId, session, 'Capture is not running. Press START first.', 'error');
      return;
    }

    const field = enabledFields(await cachedBundle()).find((f) => f.key === key);
    if (!field) {
      await pushState(tabId, session, 'That field is no longer used. Press START again to reload the fields.', 'error');
      return;
    }

    const value = normalizeSelection(field, info.selectionText ?? '');
    if (!value) {
      await pushState(tabId, session, 'No text selected. Highlight it first, or use Draw on the bar.', 'error');
      return;
    }

    const updated: CaptureSession = { ...session, values: { ...session.values, [key]: mergeHighlight(field, session.values[key], value) } };
    if (!(await writeSession(tabId, updated))) {
      await pushState(tabId, session, 'Could not keep that value. Try again.', 'error');
      return;
    }
    await pushState(tabId, updated, `${field.label}: ${value}`, 'success');
  })();
});

/** Request/response channel used by the toolbar. */
chrome.runtime.onMessage.addListener((message: ContentMessage, sender, sendResponse) => {
  const tabId = sender.tab?.id;
  const reply = async (response: Omit<StateResponse, 'configured'> & { configured?: boolean }) =>
    sendResponse({
      configured: isConfigured(await readSettings()),
      installType: await chrome.management.getSelf().then((s) => s.installType).catch(() => undefined),
      ...response,
    } satisfies StateResponse);

  if (message.type === 'GD_OPEN_SETTINGS') {
    chrome.runtime.openOptionsPage();
    sendResponse({ ok: true });
    return false;
  }
  if (message.type === 'GD_RELOAD_EXTENSION') {
    // After the new files were unzipped over the old ones: reload from disk, then refresh the
    // already-open WhatsApp / Instagram tabs so they get the new toolbar (see onInstalled).
    chrome.runtime.reload();
    sendResponse({ ok: true });
    return false;
  }
  if (message.type === 'GD_OPEN_EXTENSIONS') {
    void chrome.tabs.create({ url: `chrome://extensions/?id=${chrome.runtime.id}` });
    sendResponse({ ok: true });
    return false;
  }
  if (message.type === 'GD_OPEN_GUIDE') {
    // The guide lives in GrowDesk, so it always matches the current version; not connected yet
    // means there's nowhere to open it from, so show the settings instead.
    void readSettings().then((s) => {
      const url = guideUrl(s);
      if (url) void chrome.tabs.create({ url });
      else chrome.runtime.openOptionsPage();
    });
    sendResponse({ ok: true });
    return false;
  }
  if (tabId == null) {
    sendResponse({ ok: false, session: null, bundle: null, configured: false, error: 'No tab context.' } satisfies StateResponse);
    return false;
  }

  void (async () => {
    try {
      switch (message.type) {
        case 'GD_GET_STATE': {
          const session = await readSession(tabId);
          if (session?.active) {
            // A tab can be navigated between the two supported sites mid-capture.
            const platform = resolvePlatform(message.platform, sender.url);
            if (platform && session.source !== platform) {
              session.source = platform;
              await writeSession(tabId, session);
            }
          }
          await syncMenusForActiveTab();
          await reply({ ok: true, session, bundle: await cachedBundle() });
          return;
        }

        case 'GD_REFRESH': {
          const session = await readSession(tabId);
          let bundle = await cachedBundle();
          const settings = await readSettings();
          if (isConfigured(settings)) {
            try {
              bundle = await loadBundle();
            } catch {
              // Offline or not reachable: keep the setup we have; START and STOP report errors.
            }
          }
          await reply({ ok: true, session, bundle });
          return;
        }

        case 'GD_START': {
          const platform = resolvePlatform(message.platform, sender.url);
          if (!platform) {
            await reply({ ok: false, session: null, bundle: null, error: 'Unsupported page.' });
            return;
          }
          // Fresh field setup every capture, so admin changes apply straight away.
          let bundle: ConfigBundle;
          try {
            bundle = await loadBundle();
          } catch (error) {
            await reply({ ok: false, session: null, bundle: await cachedBundle(), error: errorText(error) });
            return;
          }
          // An out-of-date toolbar may not match GrowDesk's rules: capturing waits for the update.
          if (updateRequired(bundle, chrome.runtime.getManifest().version)) {
            await reply({ ok: false, session: null, bundle, error: `Update GrowDesk Capture to ${bundle.latest!.version} first: click Update.` });
            return;
          }
          const session = createSession(platform);
          if (!(await writeSession(tabId, session))) {
            await reply({ ok: false, session: null, bundle, error: 'Browser storage is unavailable.' });
            return;
          }
          await rebuildMenus(bundle);
          await setMenusVisible(true);
          await reply({ ok: true, session, bundle });
          return;
        }

        case 'GD_SET_VALUE': {
          const session = await readSession(tabId);
          if (!session?.active) {
            await reply({ ok: false, session, bundle: await cachedBundle(), error: 'Capture is not running.' });
            return;
          }
          const values = { ...session.values };
          if (message.value === null) delete values[message.key];
          else values[message.key] = message.value;
          const updated = { ...session, values };
          await writeSession(tabId, updated);
          await reply({ ok: true, session: updated, bundle: await cachedBundle() });
          return;
        }

        case 'GD_SAVE': {
          sendResponse(await saveLead(tabId));
          return;
        }

        case 'GD_DISCARD': {
          await clearSession(tabId);
          await syncMenusForActiveTab();
          await reply({ ok: true, session: null, bundle: await cachedBundle() });
          return;
        }
      }
    } catch (error) {
      console.error('[GrowDesk Capture] Service worker message failed.', error);
      await reply({ ok: false, session: null, bundle: null, error: errorText(error) });
    }
  })();

  // Keep the message channel open for the async response above.
  return true;
});

/**
 * Sends the tab's capture to GrowDesk and, only on success, ends the session. A failed send
 * keeps everything so the user can fix it and press STOP again: a lead is never silently lost.
 */
async function saveLead(tabId: number): Promise<SaveResponse> {
  const session = await readSession(tabId);
  // Re-read the setup first: a field the admin has just made required is enforced before sending.
  const bundle = await loadBundle().catch(() => cachedBundle());
  const blocker = saveBlocker(session, bundle);
  if (blocker || !session || !bundle) return { ok: false, error: blocker ?? 'Nothing to save.' };

  try {
    const result = await sendLead(buildRequest(session, bundle));
    await clearSession(tabId);
    await syncMenusForActiveTab();
    return {
      ok: true,
      action: result.action,
      customerId: result.customerId,
      customerName: result.customerName,
      message: result.message ?? (result.action === 'created' ? `${result.customerName} was added.` : `${result.customerName} was updated.`),
      warnings: result.warnings,
    };
  } catch (error) {
    // A list changed in GrowDesk since START: refresh it so the fix is one click away.
    if (error instanceof GrowDeskError && error.status === 400) void loadBundle().catch(() => undefined);
    return { ok: false, error: errorText(error), field: error instanceof GrowDeskError ? error.field : null };
  }
}

function errorText(error: unknown): string {
  if (error instanceof GrowDeskError) return error.message;
  console.error('[GrowDesk Capture] Unexpected error.', error);
  return 'Something went wrong. Please try again.';
}

function resolvePlatform(claimed: Platform | undefined, senderUrl?: string): Platform | null {
  // Trust the sender URL Chrome reports over anything the page sent.
  return platformFromUrl(senderUrl) ?? claimed ?? null;
}
