"use client";

import { motion } from "framer-motion";
import { CalendarX2, CircleAlert, Download, Plus, RotateCcw } from "lucide-react";
import { useState } from "react";
import { toast } from "sonner";
import { BookingStatusBadge, formatMoney, formatTime } from "./booking-status";
import { TreatmentChips } from "@/components/customers/stage-badge";
import { Button } from "@/components/ui/button";
import { DateRangeFilter, FUTURE_PRESETS, PAST_PRESETS } from "@/components/ui/date-range-filter";
import { EmptyState } from "@/components/ui/empty-state";
import { FilterMenu } from "@/components/ui/filter-menu";
import { Pagination } from "@/components/ui/pagination";
import { SearchInput } from "@/components/ui/search-input";
import { Skeleton } from "@/components/ui/skeleton";
import { useDebouncedValue } from "@/hooks/use-debounced-value";
import { toastError } from "@/lib/api/admin";
import { useBookings, useDoctorOptions, useLocale } from "@/lib/api/bookings";
import { downloadFile } from "@/lib/api/client";
import { useActiveLookup } from "@/lib/api/customers";
import { useSession } from "@/lib/auth/session";
import { formatDate, today, type DateRange } from "@/lib/dates";
import { can, Permission } from "@/lib/permissions";
import { cn } from "@/lib/utils";
import type { BookingListItem, BookingQuery } from "@/types/bookings";

const PAGE_SIZE = 25;

const TABS = [
  { id: "upcoming", label: "Upcoming", empty: "No upcoming consultations", hint: "Booked consultations from today onwards appear here." },
  { id: "today", label: "Today", empty: "No consultations today", hint: "Your calendar is clear." },
  { id: "history", label: "History", empty: "No past consultations", hint: "Completed, cancelled, rescheduled and no-show consultations appear here." },
] as const;
export type Tab = (typeof TABS)[number]["id"];

/** Which filters each tab offers. The doctor filter is shared by all tabs. */
const TAB_FILTERS: Record<Tab, { range: boolean; status: boolean; payment: boolean }> = {
  upcoming: { range: true, status: false, payment: false }, // always Booked, not paid yet
  today: { range: false, status: true, payment: true }, // always today
  history: { range: true, status: true, payment: true },
};

interface Filters {
  search: string;
  range: DateRange;
  status?: string;
  treatment?: string;
  payment?: string;
}

const NO_FILTERS: Filters = { search: "", range: {} };
const NO_FILTERS_BY_TAB: Record<Tab, Filters> = { upcoming: NO_FILTERS, today: NO_FILTERS, history: NO_FILTERS };

/** The list (and export) query for a tab: its fixed window plus the filters it offers. */
function buildQuery(tab: Tab, f: Filters, t: string, doctor: string | undefined): BookingQuery {
  const common: BookingQuery = {
    search: f.search.trim() || undefined,
    treatmentId: f.treatment ? Number(f.treatment) : undefined,
    doctorId: doctor ? Number(doctor) : undefined,
  };
  switch (tab) {
    case "upcoming":
      // Never earlier than today, whatever range is chosen.
      return { ...common, from: f.range.from && f.range.from > t ? f.range.from : t, to: f.range.to, status: "Booked", sort: "asc" };
    case "today":
      return { ...common, from: t, to: t, status: f.status, paymentStatus: f.payment, sort: "asc" };
    case "history":
      return {
        ...common,
        from: f.range.from,
        to: f.range.to ?? (f.range.from ? undefined : t),
        status: f.status,
        paymentStatus: f.payment,
        sort: "desc",
      };
  }
}

function isFiltered(tab: Tab, f: Filters) {
  const offers = TAB_FILTERS[tab];
  return (
    !!f.search.trim() ||
    !!f.treatment ||
    (offers.range && !!(f.range.from || f.range.to)) ||
    (offers.status && !!f.status) ||
    (offers.payment && !!f.payment)
  );
}

function toQueryString(q: object) {
  const p = new URLSearchParams();
  for (const [k, v] of Object.entries(q)) if (v !== undefined && v !== null && v !== "") p.set(k, String(v));
  return p.toString();
}


/**
 * The bookings list: Upcoming, Today and History, each with its own filters and an Excel export
 * of exactly what is shown. The page around it owns the booking drawers.
 */
export function BookingList({ onOpen, onBook, initialTab = "upcoming" }: { onOpen: (id: number) => void; onBook?: () => void; initialTab?: Tab }) {
  const { data: session } = useSession();
  const { data: locale } = useLocale();
  const [tab, setTab] = useState<Tab>(initialTab);
  const [page, setPage] = useState(1);
  const [doctor, setDoctor] = useState<string | undefined>();
  // Each tab remembers its own filters.
  const [filtersByTab, setFiltersByTab] = useState(NO_FILTERS_BY_TAB);
  const [exporting, setExporting] = useState(false);
  const doctors = useDoctorOptions();
  const treatments = useActiveLookup("treatments");
  const canSeePayments = can(session?.user, Permission.PaymentsView);

  const filters = filtersByTab[tab];
  const offers = TAB_FILTERS[tab];
  const search = useDebouncedValue(filters.search, 300);
  const t = locale?.today ?? today();
  const query = buildQuery(tab, { ...filters, search }, t, doctor);
  const { data, isPending, isError, isFetching, refetch } = useBookings({ ...query, page, pageSize: PAGE_SIZE });

  const setFilter = (patch: Partial<Filters>) => {
    setFiltersByTab((all) => ({ ...all, [tab]: { ...all[tab], ...patch } }));
    setPage(1);
  };
  const filtered = isFiltered(tab, filters);

  const exportExcel = async () => {
    setExporting(true);
    try {
      await downloadFile(`/bookings/export?${toQueryString(query)}`, "bookings.xlsx");
      toast.success(`Exported ${data?.totalCount ?? ""} bookings to Excel`);
    } catch (error) {
      toastError(error);
    } finally {
      setExporting(false);
    }
  };
  const meta = TABS.find((x) => x.id === tab)!;

  return (
    <>
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-line p-4">
        <div className="inline-flex rounded-xl bg-surface-muted p-1 text-sm font-medium" role="tablist" aria-label="Bookings">
          {TABS.map((x) => (
            <button
              key={x.id}
              type="button"
              role="tab"
              aria-selected={tab === x.id}
              onClick={() => {
                setTab(x.id);
                setPage(1);
              }}
              className={cn("relative rounded-lg px-4 py-1.5 transition-colors", tab === x.id ? "text-foreground" : "text-muted hover:text-foreground")}
            >
              {tab === x.id && (
                <motion.span layoutId="bookings-tab" className="absolute inset-0 rounded-lg bg-surface shadow-card" transition={{ type: "spring", stiffness: 500, damping: 38 }} />
              )}
              <span className="relative">{x.label}</span>
            </button>
          ))}
        </div>
        <div className="flex items-center gap-2">
          {isFetching && !isPending && <span className="size-2 animate-pulse rounded-full bg-brand" aria-label="Loading" />}
          {doctors.data && doctors.data.length > 0 && (
            <FilterMenu
              label="Doctor"
              value={doctor}
              onChange={(v) => {
                setDoctor(v);
                setPage(1);
              }}
              options={doctors.data.map((d) => ({ value: String(d.id), label: d.name }))}
            />
          )}
        </div>
      </div>

      <div className="flex flex-col gap-3 border-b border-line p-4">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <SearchInput
              value={filters.search}
              onChange={(v) => setFilter({ search: v })}
              placeholder="Search by customer name or number…"
              className="flex-1 sm:max-w-sm"
            />
            <Button variant="secondary" onClick={exportExcel} disabled={exporting || !data || data.totalCount === 0}>
              <Download className="size-4" />
              {exporting ? "Exporting…" : `Export to Excel${data ? ` (${data.totalCount.toLocaleString()})` : ""}`}
            </Button>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            {offers.range && (
              <DateRangeFilter
                value={filters.range}
                onChange={(range) => setFilter({ range })}
                presets={tab === "upcoming" ? FUTURE_PRESETS : PAST_PRESETS}
                min={tab === "upcoming" ? t : undefined}
              />
            )}
            {offers.status && (
              <FilterMenu
                label="Status"
                value={filters.status}
                onChange={(v) => setFilter({ status: v })}
                options={[
                  { value: "Completed", label: "Completed" },
                  { value: "Booked", label: "Booked" },
                  { value: "Rescheduled", label: "Rescheduled" },
                  { value: "Cancelled", label: "Cancelled" },
                  { value: "NoShow", label: "No-show" },
                ]}
              />
            )}
            <FilterMenu
              label="Treatment"
              value={filters.treatment}
              onChange={(v) => setFilter({ treatment: v })}
              options={treatments.data?.map((x) => ({ value: String(x.id), label: x.name })) ?? []}
            />
            {canSeePayments && offers.payment && (
              <FilterMenu
                label="Payment"
                value={filters.payment}
                onChange={(v) => setFilter({ payment: v })}
                options={[
                  { value: "Outstanding", label: "Owes money" },
                  { value: "Paid", label: "Paid" },
                  { value: "PartlyPaid", label: "Partly paid" },
                  { value: "Unpaid", label: "Unpaid" },
                  { value: "Waived", label: "Waived" },
                ]}
              />
            )}
            {filtered && (
              <button
                type="button"
                onClick={() => {
                  setFiltersByTab((all) => ({ ...all, [tab]: NO_FILTERS }));
                  setPage(1);
                }}
                className="px-2 text-sm font-medium text-muted hover:text-foreground"
              >
                Clear all
              </button>
            )}
          </div>
        </div>

      {isPending ? (
        <div className="divide-y divide-line" aria-busy="true" aria-label="Loading">
          {Array.from({ length: 5 }, (_, i) => (
            <div key={i} className="flex items-center gap-4 px-4 py-4">
              <Skeleton className="h-10 w-16 rounded-xl" />
              <Skeleton className="h-4 w-40" />
              <Skeleton className="ml-auto h-6 w-24 rounded-full" />
            </div>
          ))}
        </div>
      ) : isError ? (
        <EmptyState
          icon={CircleAlert}
          title="Couldn't load bookings"
          description="Check your connection and try again."
          action={
            <Button variant="secondary" onClick={() => refetch()}>
              <RotateCcw className="size-4" /> Try again
            </Button>
          }
        />
      ) : data.items.length === 0 ? (
        <EmptyState
          icon={CalendarX2}
          title={filtered ? "No bookings match these filters" : meta.empty}
          description={filtered ? "Try widening the date range or clearing a filter." : meta.hint}
          action={
            onBook &&
            tab !== "history" &&
            !filtered && (
              <Button onClick={onBook}>
                <Plus className="size-4" /> Book consultation
              </Button>
            )
          }
          className="py-16"
        />
      ) : (
        <div className={cn("transition-opacity", isFetching && "opacity-70")}>
          <ul className="divide-y divide-line">
            {data.items.map((b, i) => (
              <BookingRow key={b.id} b={b} index={i} currency={locale?.currency} onOpen={() => onOpen(b.id)} />
            ))}
          </ul>
          <Pagination page={data.page} pageSize={PAGE_SIZE} totalCount={data.totalCount} onPageChange={setPage} noun="bookings" />
        </div>
      )}
    </>
  );
}

function BookingRow({ b, index, currency, onOpen }: { b: BookingListItem; index: number; currency?: string; onOpen: () => void }) {
  const [, month, day] = b.date.split("-");
  const monthName = new Date(2000, Number(month) - 1, 1).toLocaleString(undefined, { month: "short" });
  return (
    <motion.li initial={{ opacity: 0 }} animate={{ opacity: 1 }} transition={{ delay: Math.min(index, 10) * 0.02 }}>
      <button type="button" onClick={onOpen} className="flex w-full items-center gap-4 px-4 py-3.5 text-left transition-colors hover:bg-brand-soft/40">
        <span className="flex w-14 shrink-0 flex-col items-center rounded-xl border border-line bg-surface py-1.5">
          <span className="text-[10px] font-semibold uppercase text-muted">{monthName}</span>
          <span className="font-display text-lg font-bold leading-none">{Number(day)}</span>
        </span>
        <div className="min-w-0 flex-1">
          <p className="truncate font-semibold">{b.customer.name}</p>
          <p className="mt-0.5 text-xs tabular-nums text-muted">
            {formatDate(b.date)} · {formatTime(b.startTime)} – {formatTime(b.endTime)}
            {b.doctor && ` · ${b.doctor.name}`}
          </p>
          <div className="mt-1.5 md:hidden">
            <TreatmentChips treatments={b.treatments} max={3} />
          </div>
        </div>
        <div className="hidden w-64 md:block">
          <TreatmentChips treatments={b.treatments} max={2} />
        </div>
        <div className="flex shrink-0 flex-col items-end gap-1">
          <BookingStatusBadge status={b.status} />
          {b.status === "Completed" && b.consultationCharge !== null && (
            <span className="text-xs text-muted">
              {formatMoney(b.consultationCharge, currency)}
              {b.balance > 0 && <span className="font-semibold text-amber-700"> · {formatMoney(b.balance, currency)} owed</span>}
              {b.paymentStatus === "Waived" && " · waived"}
            </span>
          )}
        </div>
      </button>
    </motion.li>
  );
}
