"use client";

import * as Popover from "@radix-ui/react-popover";
import { AnimatePresence, motion } from "framer-motion";
import { CalendarRange, ChevronDown, X } from "lucide-react";
import { useState } from "react";
import { addDays, formatDate, formatDateTime, isoDate, today, type DateRange } from "@/lib/dates";
import { cn } from "@/lib/utils";

export interface RangePreset {
  /** Set when the caller stores the preset itself (it keeps rolling), not just its dates. */
  key?: string;
  label: string;
  range: () => DateRange;
}

/** Looking back: history and reports. */
export const PAST_PRESETS: RangePreset[] = [
  { label: "Today", range: () => ({ from: today(), to: today() }) },
  { label: "Last 7 days", range: () => ({ from: isoDate(addDays(new Date(), -6)), to: today() }) },
  { label: "Last 30 days", range: () => ({ from: isoDate(addDays(new Date(), -29)), to: today() }) },
  {
    label: "This month",
    range: () => {
      const n = new Date();
      return { from: isoDate(new Date(n.getFullYear(), n.getMonth(), 1)), to: today() };
    },
  },
  {
    label: "Last month",
    range: () => {
      const n = new Date();
      return { from: isoDate(new Date(n.getFullYear(), n.getMonth() - 1, 1)), to: isoDate(new Date(n.getFullYear(), n.getMonth(), 0)) };
    },
  },
  {
    label: "This year",
    range: () => ({ from: isoDate(new Date(new Date().getFullYear(), 0, 1)), to: today() }),
  },
];

/** Looking ahead: upcoming bookings. */
export const FUTURE_PRESETS: RangePreset[] = [
  { label: "Tomorrow", range: () => ({ from: isoDate(addDays(new Date(), 1)), to: isoDate(addDays(new Date(), 1)) }) },
  { label: "Next 7 days", range: () => ({ from: today(), to: isoDate(addDays(new Date(), 6)) }) },
  { label: "Next 30 days", range: () => ({ from: today(), to: isoDate(addDays(new Date(), 29)) }) },
  {
    label: "This month",
    range: () => {
      const n = new Date();
      return { from: today(), to: isoDate(new Date(n.getFullYear(), n.getMonth() + 1, 0)) };
    },
  },
  {
    label: "Next month",
    range: () => {
      const n = new Date();
      return { from: isoDate(new Date(n.getFullYear(), n.getMonth() + 1, 1)), to: isoDate(new Date(n.getFullYear(), n.getMonth() + 2, 0)) };
    },
  },
];

function describe(r: DateRange) {
  // "2026-10-01" or, with a time, "2026-10-01T09:00".
  const f = (v: string) => (v.length > 10 ? formatDateTime(v) : formatDate(v));
  if (r.from && r.to) return r.from === r.to ? f(r.from) : `${f(r.from)} – ${f(r.to)}`;
  if (r.from) return `From ${f(r.from)}`;
  if (r.to) return `Until ${f(r.to)}`;
  return "";
}

/** A preset's whole days as date-times: from the start of the first to the last minute of the last. */
const wholeDays = (r: DateRange): DateRange => ({
  from: r.from && `${r.from}T00:00`,
  to: r.to && `${r.to}T23:59`,
});

/** A date-range pill with quick presets and custom from/to dates. */
export function DateRangeFilter({
  label = "Date",
  value,
  onChange,
  presets = PAST_PRESETS,
  min,
  activeLabel,
  onPreset,
  withTime = false,
}: {
  label?: string;
  value: DateRange;
  onChange: (r: DateRange) => void;
  presets?: RangePreset[];
  /** Shown instead of the dates while a rolling preset is selected (e.g. "Due today"). */
  activeLabel?: string;
  /** Receives the preset clicked; without it the preset's dates go to onChange. */
  onPreset?: (p: RangePreset) => void;
  /** Pick a date and time ("YYYY-MM-DDTHH:mm") instead of just a date. */
  withTime?: boolean;
  /** Earliest date the custom inputs allow (e.g. today for upcoming bookings). */
  min?: string;
}) {
  const [open, setOpen] = useState(false);
  const set = !!(value.from || value.to);

  return (
    <Popover.Root open={open} onOpenChange={setOpen}>
      <div
        className={cn(
          "inline-flex h-9 items-center rounded-xl border text-sm transition-colors",
          set ? "border-brand/30 bg-brand-soft text-brand-strong" : "border-line bg-surface text-foreground/80 hover:border-slate-300",
        )}
      >
        <Popover.Trigger className="flex h-full items-center gap-1.5 rounded-xl pl-3 pr-2.5 font-medium">
          <CalendarRange className="size-3.5" />
          <span className="whitespace-nowrap">{set ? (activeLabel ?? describe(value)) : label}</span>
          {!set && <ChevronDown className="size-3.5 text-muted" />}
        </Popover.Trigger>
        {set && (
          <button type="button" onClick={() => onChange({})} aria-label={`Clear ${label} filter`} className="mr-1 flex size-6 items-center justify-center rounded-lg hover:bg-brand/10">
            <X className="size-3.5" />
          </button>
        )}
      </div>
      <AnimatePresence>
        {open && (
          <Popover.Portal forceMount>
            <Popover.Content asChild forceMount align="start" sideOffset={6} collisionPadding={12}>
              <motion.div
                initial={{ opacity: 0, y: -4, scale: 0.98 }}
                animate={{ opacity: 1, y: 0, scale: 1 }}
                exit={{ opacity: 0, y: -4, scale: 0.98 }}
                transition={{ duration: 0.14 }}
                className={cn("z-50 rounded-xl border border-line bg-surface p-2 shadow-pop", withTime ? "w-80" : "w-72")}
              >
                <div className="grid grid-cols-2 gap-1">
                  {presets.map((p) => (
                    <button
                      key={p.label}
                      type="button"
                      onClick={() => {
                        if (onPreset) onPreset(p);
                        else onChange(withTime ? wholeDays(p.range()) : p.range());
                        setOpen(false);
                      }}
                      className="rounded-lg px-2.5 py-2 text-left text-sm hover:bg-surface-muted"
                    >
                      {p.label}
                    </button>
                  ))}
                </div>
                <div className={cn("mt-2 grid gap-2 border-t border-line pt-3", withTime ? "grid-cols-1" : "grid-cols-2")}>
                  <label className="text-xs font-medium text-muted">
                    From
                    <input
                      type={withTime ? "datetime-local" : "date"}
                      value={value.from ?? ""}
                      min={min}
                      max={value.to}
                      onChange={(e) => onChange({ ...value, from: e.target.value || undefined })}
                      className="mt-1 h-9 w-full rounded-lg border border-line bg-surface px-2 text-sm text-foreground focus:border-brand focus:outline-none"
                    />
                  </label>
                  <label className="text-xs font-medium text-muted">
                    To
                    <input
                      type={withTime ? "datetime-local" : "date"}
                      value={value.to ?? ""}
                      min={value.from ?? min}
                      onChange={(e) => onChange({ ...value, to: e.target.value || undefined })}
                      className="mt-1 h-9 w-full rounded-lg border border-line bg-surface px-2 text-sm text-foreground focus:border-brand focus:outline-none"
                    />
                  </label>
                </div>
              </motion.div>
            </Popover.Content>
          </Popover.Portal>
        )}
      </AnimatePresence>
    </Popover.Root>
  );
}
