import type { ApiEnvelope, ApiFieldError } from "@/types/api";

/** Dispatched on any 401 so the auth layer can end the session in one place. */
export const UNAUTHORIZED_EVENT = "growdesk:unauthorized";

/** Dispatched when the API refuses a call until the user changes their password (backend PasswordChangeGate). */
export const PASSWORD_CHANGE_EVENT = "growdesk:password-change-required";

/** Dispatched when the API refuses a call because the subscription is unpaid (backend SubscriptionGate). */
export const SUBSCRIPTION_BLOCKED_EVENT = "growdesk:subscription-blocked";

const FRIENDLY_FALLBACK = "Something went wrong. Please try again.";

export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly fieldErrors: ApiFieldError[] = [],
    /** Extra detail some errors carry, e.g. the existing customer on a duplicate (409). */
    public readonly data: unknown = null,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

type Method = "GET" | "POST" | "PUT" | "PATCH" | "DELETE";

interface RequestOptions {
  body?: unknown;
  signal?: AbortSignal;
  /** Set for calls where a 401 is an expected answer (e.g. login), not an expired session. */
  skipUnauthorizedEvent?: boolean;
}

/**
 * The only function in the app that calls fetch. Requests go to /api/* on the Next.js server,
 * which forwards them to the backend, so the HTTP-only session cookie is sent automatically.
 */
async function request<T>(method: Method, path: string, options: RequestOptions = {}): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`/api${path}`, {
      method,
      credentials: "same-origin",
      headers: options.body === undefined ? undefined : { "Content-Type": "application/json" },
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      signal: options.signal,
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") throw error;
    throw new ApiError("Can't reach the server. Check your connection and try again.", 0);
  }

  let envelope: ApiEnvelope<T> | null = null;
  try {
    envelope = (await response.json()) as ApiEnvelope<T>;
  } catch {
    // Non-JSON body (e.g. a proxy error page). Fall through to the friendly message.
  }

  if (response.status === 401 && !options.skipUnauthorizedEvent && typeof window !== "undefined") {
    window.dispatchEvent(new Event(UNAUTHORIZED_EVENT));
  }

  if (
    response.status === 403 &&
    envelope?.errors?.some((e) => e.field === "password_change_required") &&
    typeof window !== "undefined"
  ) {
    window.dispatchEvent(new Event(PASSWORD_CHANGE_EVENT));
  }

  if (response.status === 402 && typeof window !== "undefined") {
    window.dispatchEvent(new Event(SUBSCRIPTION_BLOCKED_EVENT));
  }

  if (!response.ok || !envelope?.success) {
    // 5xx messages are already generic from the backend; anything unexpected becomes the fallback.
    const message = envelope?.message ?? FRIENDLY_FALLBACK;
    throw new ApiError(message, response.status, envelope?.errors ?? [], envelope?.data ?? null);
  }

  return envelope.data as T;
}

export const api = {
  get: <T>(path: string, options?: RequestOptions) => request<T>("GET", path, options),
  post: <T>(path: string, body?: unknown, options?: RequestOptions) => request<T>("POST", path, { ...options, body }),
  put: <T>(path: string, body?: unknown, options?: RequestOptions) => request<T>("PUT", path, { ...options, body }),
  patch: <T>(path: string, body?: unknown, options?: RequestOptions) => request<T>("PATCH", path, { ...options, body }),
  delete: <T>(path: string, options?: RequestOptions) => request<T>("DELETE", path, options),
};

/**
 * Downloads a file from the API (e.g. an Excel export) and saves it with the server's file name.
 * Errors come back as the usual JSON envelope and are thrown as ApiError, like other calls.
 */
export async function downloadFile(path: string, fallbackName: string) {
  let response: Response;
  try {
    response = await fetch(`/api${path}`, { credentials: "same-origin" });
  } catch {
    throw new ApiError("Can't reach the server. Check your connection and try again.", 0);
  }

  if (!response.ok) {
    let envelope: ApiEnvelope<unknown> | null = null;
    try {
      envelope = (await response.json()) as ApiEnvelope<unknown>;
    } catch {
      // not JSON
    }
    if (response.status === 401 && typeof window !== "undefined") window.dispatchEvent(new Event(UNAUTHORIZED_EVENT));
    throw new ApiError(envelope?.message ?? FRIENDLY_FALLBACK, response.status, envelope?.errors ?? []);
  }

  const disposition = response.headers.get("content-disposition") ?? "";
  const name = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)?.[1] ?? fallbackName;
  const url = URL.createObjectURL(await response.blob());
  const a = document.createElement("a");
  a.href = url;
  a.download = decodeURIComponent(name);
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
