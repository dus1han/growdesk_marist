/**
 * Shapes of the GrowDesk Capture API (GrowDesk docs/CAPTURE_API.md). Every response is wrapped
 * in the CRM's envelope: { success, data, message, errors }.
 */

export interface Envelope<T> {
  success: boolean;
  data: T | null;
  message: string | null;
  errors?: { field: string | null; message: string }[] | null;
}

export interface CaptureOption {
  id: number;
  label: string;
}

/** One field the admin configured for the toolbar. */
export interface ConfigField {
  key: string;
  label: string;
  /** text, textarea, phone, email, number, date, dropdown, multiselect, boolean */
  type: string;
  enabled: boolean;
  required: boolean;
  order: number;
  isCustom: boolean;
  options: CaptureOption[] | null;
}

export interface Lookup {
  id: number;
  name: string;
  color: string | null;
}

/** Everything the toolbar needs to build its fields, fetched when a capture starts. */
export interface ConfigBundle {
  server: string;
  fields: ConfigField[];
  treatments: Lookup[];
  stages: Lookup[];
  sources: Lookup[];
  fetchedAt: string;
  /** The newest toolbar GrowDesk offers (GET /capture/latest.json); null if unknown. */
  latest?: LatestRelease | null;
}

export interface LatestRelease {
  version: string;
  /** Download for Load unpacked. */
  download: string;
  guide: string;
}

export interface CaptureRequest {
  name?: string;
  whatsApp?: string;
  secondaryPhone?: string;
  instagram?: string;
  email?: string;
  stageId?: number;
  leadSourceId?: number;
  treatmentIds?: number[];
  customFields?: Record<string, string | number | number[] | boolean>;
  notes?: string;
  /** The site captured on. GrowDesk sets the lead source from it. */
  source?: 'whatsapp' | 'instagram';
}

export interface CaptureResult {
  customerId: number;
  action: 'created' | 'updated';
  customerName: string;
  warnings: string[];
}
