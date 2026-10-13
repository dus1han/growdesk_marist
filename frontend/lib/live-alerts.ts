/**
 * Ways a new WhatsApp BOT booking reaches staff when the GrowDesk page isn't in front of them:
 * a short chime (works on plain http, also with the browser minimised, while GrowDesk is open in
 * a tab) and a desktop notification (browsers only allow these on HTTPS).
 */

const SOUND_KEY = "growdesk.live.sound";

/** The chime is on unless this browser switched it off. */
export function soundEnabled(): boolean {
  try {
    return localStorage.getItem(SOUND_KEY) !== "0";
  } catch {
    return true;
  }
}

export function setSoundEnabled(on: boolean) {
  try {
    localStorage.setItem(SOUND_KEY, on ? "1" : "0");
  } catch {
    /* storage blocked: the default (on) applies */
  }
}

let audio: AudioContext | null = null;

/**
 * Browsers only let a page play sound after someone has clicked in it. Called on the first click,
 * so a chime can play later even when the tab is in the background or the window is minimised.
 */
export function unlockSound() {
  try {
    audio ??= new AudioContext();
    void audio.resume();
  } catch {
    /* no Web Audio: no chime */
  }
}

/** Two soft rising notes, about half a second. */
export function playChime() {
  if (!soundEnabled()) return;
  try {
    audio ??= new AudioContext();
    void audio.resume();
    const now = audio.currentTime;
    [880, 1320].forEach((freq, i) => {
      const osc = audio!.createOscillator();
      const gain = audio!.createGain();
      const start = now + i * 0.16;
      osc.type = "sine";
      osc.frequency.value = freq;
      gain.gain.setValueAtTime(0.0001, start);
      gain.gain.exponentialRampToValueAtTime(0.18, start + 0.02);
      gain.gain.exponentialRampToValueAtTime(0.0001, start + 0.3);
      osc.connect(gain).connect(audio!.destination);
      osc.start(start);
      osc.stop(start + 0.32);
    });
  } catch {
    /* no Web Audio: no chime */
  }
}

/** Desktop notifications need HTTPS (or localhost); on plain http the browser doesn't offer them. */
export function desktopAlertsSupported(): boolean {
  return typeof window !== "undefined" && window.isSecureContext && "Notification" in window;
}

export function desktopAlertPermission(): NotificationPermission | "unsupported" {
  return desktopAlertsSupported() ? Notification.permission : "unsupported";
}

export async function requestDesktopAlerts(): Promise<NotificationPermission | "unsupported"> {
  if (!desktopAlertsSupported()) return "unsupported";
  return Notification.requestPermission();
}

/**
 * Shown only while GrowDesk isn't the visible tab; clicking it brings GrowDesk forward.
 * Returns whether a notification was shown.
 */
export function showDesktopAlert(title: string, body: string, onClick: () => void): boolean {
  if (desktopAlertPermission() !== "granted" || document.visibilityState === "visible") return false;
  try {
    const n = new Notification(title, { body, icon: "/icon.svg", tag: `growdesk-${title}-${body}` });
    n.onclick = () => {
      window.focus();
      onClick();
      n.close();
    };
    return true;
  } catch {
    /* some browsers only allow notifications from a service worker */
    return false;
  }
}
