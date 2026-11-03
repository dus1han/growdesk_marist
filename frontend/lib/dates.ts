/** Local calendar dates as yyyy-MM-dd, the format the API uses for date-only values. */
export function isoDate(d: Date) {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

export function addDays(d: Date, days: number) {
  const copy = new Date(d);
  copy.setDate(copy.getDate() + days);
  return copy;
}

export const today = () => isoDate(new Date());

/** "12 Oct 2026" from a yyyy-MM-dd string, without timezone drift. */
/** "2026-10-01T09:00" → "01 Oct 2026, 9:00 AM" (a clinic-local date and time, no time zone). */
export function formatDateTime(local: string) {
  const [h, m] = local.slice(11, 16).split(":").map(Number);
  return `${formatDate(local.slice(0, 10))}, ${new Date(2000, 0, 1, h, m).toLocaleTimeString(undefined, { hour: "numeric", minute: "2-digit" })}`;
}

export function formatDate(iso: string | null | undefined) {
  if (!iso) return "";
  const [y, m, d] = iso.slice(0, 10).split("-").map(Number);
  return new Date(y, m - 1, d).toLocaleDateString(undefined, { day: "numeric", month: "short", year: "numeric" });
}

/** Follow-up state relative to today, for colouring. */
export function followUpState(iso: string | null | undefined): "overdue" | "today" | "upcoming" | null {
  if (!iso) return null;
  const t = today();
  return iso < t ? "overdue" : iso === t ? "today" : "upcoming";
}

export interface DateRange {
  from?: string;
  to?: string;
}

export interface DatePreset {
  label: string;
  range: () => DateRange;
}

/** "Created" filter presets. Values are local calendar dates. */
export const CREATED_PRESETS: Record<string, DatePreset> = {
  today: { label: "Today", range: () => ({ from: today(), to: today() }) },
  "7d": { label: "Last 7 days", range: () => ({ from: isoDate(addDays(new Date(), -6)), to: today() }) },
  "30d": { label: "Last 30 days", range: () => ({ from: isoDate(addDays(new Date(), -29)), to: today() }) },
  month: {
    label: "This month",
    range: () => {
      const n = new Date();
      return { from: isoDate(new Date(n.getFullYear(), n.getMonth(), 1)), to: today() };
    },
  },
  lastmonth: {
    label: "Last month",
    range: () => {
      const n = new Date();
      return { from: isoDate(new Date(n.getFullYear(), n.getMonth() - 1, 1)), to: isoDate(new Date(n.getFullYear(), n.getMonth(), 0)) };
    },
  },
  year: { label: "This year", range: () => ({ from: isoDate(new Date(new Date().getFullYear(), 0, 1)), to: today() }) },
};

/** "Follow-up" filter presets. */
export const FOLLOW_UP_PRESETS: Record<string, DatePreset> = {
  due: { label: "Due now (incl. overdue)", range: () => ({ to: today() }) },
  overdue: { label: "Overdue", range: () => ({ to: isoDate(addDays(new Date(), -1)) }) },
  today: { label: "Due today", range: () => ({ from: today(), to: today() }) },
  week: { label: "Next 7 days", range: () => ({ from: today(), to: isoDate(addDays(new Date(), 6)) }) },
  soon: { label: "Within 7 days (incl. overdue)", range: () => ({ to: isoDate(addDays(new Date(), 6)) }) },
};

/**
 * A date filter in the URL: a preset key ("7d", which keeps rolling) or a custom range
 * "2026-01-01_2026-03-31" (either side may be empty).
 */
export function parseRangeParam(value: string | undefined, presets: Record<string, DatePreset>): { range: DateRange; preset?: DatePreset } {
  if (!value) return { range: {} };
  const preset = presets[value];
  if (preset) return { range: preset.range(), preset };
  const [from, to] = value.split("_");
  const valid = (d?: string) => (d && /^\d{4}-\d{2}-\d{2}$/.test(d) ? d : undefined);
  return { range: { from: valid(from), to: valid(to) } };
}

/** The URL value for a custom range; undefined when both ends are empty. */
export function rangeParam(r: DateRange): string | undefined {
  return r.from || r.to ? `${r.from ?? ""}_${r.to ?? ""}` : undefined;
}
