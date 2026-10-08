import type { CaptureOption, CaptureRequest, ConfigBundle, ConfigField } from './growdesk';

/** Platforms the toolbar runs on. */
export type Platform = 'WhatsApp' | 'Instagram';

/** A captured value: text, a chosen option id, several ids, or yes/no. */
export type FieldValue = string | number | number[] | boolean;

/**
 * A single in-progress lead capture, scoped to one browser tab. Held in
 * chrome.storage.session so it survives service-worker sleep and page reloads,
 * but never outlives the browser session. Values are keyed by field key.
 */
export interface CaptureSession {
  active: boolean;
  source: Platform;
  /** ISO timestamp of when START was pressed. */
  startedAt?: string;
  values: Record<string, FieldValue>;
}

export function createSession(source: Platform): CaptureSession {
  return { active: true, source, startedAt: new Date().toISOString(), values: {} };
}

/**
 * How a field is filled in:
 * - highlight: select text on the page, right-click, "Set as …"
 * - single / multi: choose from a list in the toolbar
 * - date / boolean: small pickers in the toolbar
 */
export type FieldKind = 'highlight' | 'single' | 'multi' | 'date' | 'boolean';

export function fieldKind(field: ConfigField): FieldKind {
  switch (field.type) {
    case 'dropdown':
      return 'single';
    case 'multiselect':
      return 'multi';
    case 'date':
      return 'date';
    case 'boolean':
      return 'boolean';
    default:
      return 'highlight';
  }
}

/**
 * Fields the toolbar never asks for: the lead source is the site the toolbar is on, which
 * GrowDesk sets from the request's `source`.
 */
const AUTOMATIC_FIELDS = new Set(['lead_source']);

/** The fields the toolbar shows, in the admin's order. */
export function enabledFields(bundle: ConfigBundle | null | undefined): ConfigField[] {
  return (bundle?.fields ?? [])
    .filter((f) => f.enabled && !AUTOMATIC_FIELDS.has(f.key))
    .sort((a, b) => a.order - b.order);
}

/** Choices for a list field: the CRM's treatments, stages and sources, or the field's own options. */
export function optionsFor(field: ConfigField, bundle: ConfigBundle): CaptureOption[] {
  const fromLookup = (list: { id: number; name: string }[]) => list.map((l) => ({ id: l.id, label: l.name }));
  switch (field.key) {
    case 'treatments':
      return fromLookup(bundle.treatments);
    case 'stage':
      return fromLookup(bundle.stages);
    case 'lead_source':
      return fromLookup(bundle.sources);
    default:
      return field.options ?? [];
  }
}

export function hasValue(value: FieldValue | undefined | null): boolean {
  if (value === undefined || value === null) return false;
  if (typeof value === 'string') return value.trim() !== '';
  if (Array.isArray(value)) return value.length > 0;
  return true;
}

/** Short text for a value, as the toolbar shows it. */
export function displayValue(field: ConfigField, value: FieldValue | undefined, bundle: ConfigBundle): string {
  if (!hasValue(value)) return '';
  if (typeof value === 'boolean') return value ? 'Yes' : 'No';
  const kind = fieldKind(field);
  if (kind === 'single' || kind === 'multi') {
    const options = optionsFor(field, bundle);
    const ids = Array.isArray(value) ? value : [value as number];
    return ids.map((id) => options.find((o) => o.id === id)?.label ?? '?').join(', ');
  }
  return String(value);
}

/** Required fields still without a value. */
export function missingRequired(session: CaptureSession | null | undefined, bundle: ConfigBundle | null | undefined): ConfigField[] {
  if (!session) return [];
  return enabledFields(bundle).filter((f) => f.required && !hasValue(session.values[f.key]));
}

/**
 * GrowDesk identifies a customer by WhatsApp number or Instagram name, so one of them is needed
 * whenever the admin offers either.
 */
export function needsIdentifier(session: CaptureSession | null | undefined, bundle: ConfigBundle | null | undefined): boolean {
  if (!session) return false;
  const keys = enabledFields(bundle).map((f) => f.key);
  const offered = keys.includes('whatsapp') || keys.includes('instagram');
  return offered && !hasValue(session.values.whatsapp) && !hasValue(session.values.instagram);
}

/** Why STOP is unavailable, or null when the lead can be saved. */
export function saveBlocker(session: CaptureSession | null | undefined, bundle: ConfigBundle | null | undefined): string | null {
  if (!session?.active) return 'Capture is not running.';
  if (!bundle) return 'Not connected to GrowDesk.';
  const missing = missingRequired(session, bundle);
  if (missing.length > 0) return `Still needed: ${missing.map((f) => f.label).join(', ')}`;
  if (needsIdentifier(session, bundle)) return 'Capture a WhatsApp number or an Instagram name.';
  return null;
}

export const canSave = (session: CaptureSession | null | undefined, bundle: ConfigBundle | null | undefined): boolean =>
  saveBlocker(session, bundle) === null;

/** Turns the captured values into the body of POST /api/capture/customers. */
export function buildRequest(session: CaptureSession, bundle: ConfigBundle): CaptureRequest {
  const request: CaptureRequest = {};
  const custom: NonNullable<CaptureRequest['customFields']> = {};

  for (const field of enabledFields(bundle)) {
    const value = session.values[field.key];
    if (!hasValue(value)) continue;
    const text = typeof value === 'string' ? value.trim() : '';

    switch (field.key) {
      case 'name': request.name = text; break;
      case 'whatsapp': request.whatsApp = text; break;
      case 'secondary_phone': request.secondaryPhone = text; break;
      case 'instagram': request.instagram = text; break;
      case 'email': request.email = text; break;
      case 'notes': request.notes = text; break;
      case 'stage': request.stageId = value as number; break;
      case 'treatments': request.treatmentIds = value as number[]; break;
      default:
        if (!field.isCustom) break;
        if (field.type === 'number' && typeof value === 'string') {
          const n = Number(value.replace(/[,\s]/g, ''));
          custom[field.key] = Number.isFinite(n) ? n : value;
        } else {
          custom[field.key] = typeof value === 'string' ? text : value;
        }
    }
  }

  if (Object.keys(custom).length > 0) request.customFields = custom;
  request.source = session.source === 'Instagram' ? 'instagram' : 'whatsapp';
  return request;
}
