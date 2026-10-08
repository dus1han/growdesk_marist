"use client";

import { motion } from "framer-motion";
import { Ban, CircleAlert, Download, HandCoins, Hourglass, PartyPopper, RotateCcw, Wallet } from "lucide-react";
import { useCallback, useState } from "react";
import { toast } from "sonner";
import { BookingDetailsDrawer } from "@/components/bookings/booking-details-drawer";
import { formatMoney, formatTime } from "@/components/bookings/booking-status";
import { PageHeader } from "@/components/layout/page-header";
import { RequirePermission } from "@/components/layout/require-permission";
import { RecordPaymentDrawer, type PendingPayment } from "@/components/payments/record-payment-drawer";
import { AnimatedNumber } from "@/components/ui/animated-number";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { DateRangeFilter } from "@/components/ui/date-range-filter";
import { EmptyState } from "@/components/ui/empty-state";
import { FilterMenu } from "@/components/ui/filter-menu";
import { Pagination } from "@/components/ui/pagination";
import { SearchInput } from "@/components/ui/search-input";
import { Skeleton } from "@/components/ui/skeleton";
import { useDebouncedValue } from "@/hooks/use-debounced-value";
import { toastError } from "@/lib/api/admin";
import { downloadFile } from "@/lib/api/client";
import { useActiveLookup } from "@/lib/api/customers";
import { paymentQueryString, useOutstanding, usePayments, usePaymentSummary } from "@/lib/api/payments";
import { useSession } from "@/lib/auth/session";
import { formatDate, type DateRange } from "@/lib/dates";
import { formatDateTime } from "@/lib/format";
import { can, Permission } from "@/lib/permissions";
import { cn } from "@/lib/utils";
import type { OutstandingItem, PaymentListItem, PaymentQuery } from "@/types/payments";

const PAGE_SIZE = 25;

type Tab = "received" | "outstanding";

export default function PaymentsPage() {
  const { data: session } = useSession();
  const canRecord = can(session?.user, Permission.PaymentsManage);
  const methods = useActiveLookup("payment-methods");

  const [tab, setTab] = useState<Tab>("received");
  const [search, setSearch] = useState("");
  const [range, setRange] = useState<DateRange>({});
  const [status, setStatus] = useState<string | undefined>();
  const [method, setMethod] = useState<string | undefined>();
  const [page, setPage] = useState(1);
  const [openBooking, setOpenBooking] = useState<number | null>(null);
  const [recording, setRecording] = useState<PendingPayment | null>(null);
  const [exporting, setExporting] = useState(false);
  const debounced = useDebouncedValue(search, 300);

  const query: PaymentQuery = {
    search: debounced.trim() || undefined,
    from: range.from,
    to: range.to,
    status,
    paymentMethodId: method ? Number(method) : undefined,
  };
  const reset = () => setPage(1);
  const switchTab = (next: Tab) => {
    setTab(next);
    setPage(1);
  };
  const received = usePayments({ ...query, page, pageSize: PAGE_SIZE }, tab === "received");
  const outstanding = useOutstanding({ search: query.search, page, pageSize: PAGE_SIZE }, tab === "outstanding");
  const summary = usePaymentSummary(query);
  const currency = summary.data?.currency ?? "AED";
  const money = useCallback((n: number) => formatMoney(n, currency), [currency]);
  const filtered = !!(debounced || range.from || range.to || status || method);

  const exportExcel = async () => {
    setExporting(true);
    try {
      await downloadFile(`/payments/export?${paymentQueryString(query)}`, "payments.xlsx");
      toast.success(`Exported ${received.data?.totalCount ?? ""} payments to Excel`);
    } catch (error) {
      toastError(error);
    } finally {
      setExporting(false);
    }
  };

  const cards = [
    {
      label: range.from || range.to ? "Collected in period" : "Collected",
      value: summary.data?.collected,
      count: summary.data?.collectedCount,
      noun: "payment",
      icon: HandCoins,
      tint: "from-emerald-500/15 text-emerald-600",
      tab: "received" as Tab,
    },
    {
      label: "Outstanding now",
      value: summary.data?.outstanding,
      count: summary.data?.outstandingCount,
      noun: "consultation",
      icon: Hourglass,
      tint: "from-amber-500/15 text-amber-600",
      tab: "outstanding" as Tab,
    },
    {
      label: range.from || range.to ? "Waived in period" : "Waived",
      value: summary.data?.waived,
      count: summary.data?.waivedCount,
      noun: "consultation",
      icon: Ban,
      tint: "from-slate-400/15 text-slate-500",
      tab: "received" as Tab,
    },
  ];

  return (
    <RequirePermission permission={Permission.PaymentsView}>
      <PageHeader title="Payments" description="What has been collected, and what customers still owe." />

      <div className="mb-6 grid gap-4 sm:grid-cols-3">
        {cards.map(({ label, value, count, noun, icon: Icon, tint, tab: target }, i) => (
          <motion.button
            key={label}
            type="button"
            onClick={() => switchTab(target)}
            initial={{ opacity: 0, y: 12 }}
            animate={{ opacity: 1, y: 0 }}
            whileHover={{ y: -2 }}
            transition={{ duration: 0.35, delay: i * 0.06, ease: [0.22, 1, 0.36, 1] }}
            className="rounded-2xl text-left focus-visible:outline-offset-4"
          >
            <Card className={cn("relative h-full overflow-hidden p-5 transition-shadow", target === "outstanding" && tab === "outstanding" && "ring-2 ring-amber-300")}>
              <div className={cn("absolute inset-0 bg-gradient-to-br to-transparent", tint.split(" ")[0])} aria-hidden />
              <div className="relative flex items-start justify-between">
                <p className="text-sm font-medium text-muted">{label}</p>
                <span className={cn("flex size-9 items-center justify-center rounded-xl bg-surface shadow-card", tint.split(" ")[1])}>
                  <Icon className="size-[18px]" />
                </span>
              </div>
              {value === undefined ? (
                <Skeleton className="relative mt-4 h-8 w-32" />
              ) : (
                <p className="relative mt-3 font-display text-3xl font-bold tracking-tight">
                  <AnimatedNumber value={value} format={money} />
                </p>
              )}
              <p className="relative mt-1 text-xs text-muted">{count !== undefined ? `${count} ${noun}${count === 1 ? "" : "s"}` : " "}</p>
            </Card>
          </motion.button>
        ))}
      </div>

      <Card className="overflow-hidden">
        <div className="flex flex-col gap-3 border-b border-line p-4">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="grid grid-cols-2 gap-1 rounded-xl bg-surface-muted p-1" role="tablist" aria-label="Payments view">
              {(["received", "outstanding"] as const).map((t) => (
                <button
                  key={t}
                  type="button"
                  role="tab"
                  aria-selected={tab === t}
                  onClick={() => switchTab(t)}
                  className={cn("relative rounded-lg px-4 py-1.5 text-sm font-medium transition-colors", tab === t ? "text-foreground" : "text-muted hover:text-foreground")}
                >
                  {tab === t && (
                    <motion.span layoutId="payments-tab" className="absolute inset-0 rounded-lg bg-surface shadow-card" transition={{ type: "spring", stiffness: 500, damping: 38 }} />
                  )}
                  <span className="relative">{t === "received" ? "Received" : "Outstanding"}</span>
                </button>
              ))}
            </div>
            <div className="flex items-center gap-2">
              {(tab === "received" ? received : outstanding).isFetching && <span className="size-2 animate-pulse rounded-full bg-brand" aria-label="Loading" />}
              {tab === "received" && (
                <Button variant="secondary" onClick={exportExcel} disabled={exporting || !received.data || received.data.totalCount === 0}>
                  <Download className="size-4" />
                  {exporting ? "Exporting…" : `Export to Excel${received.data ? ` (${received.data.totalCount.toLocaleString()})` : ""}`}
                </Button>
              )}
            </div>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <SearchInput value={search} onChange={(v) => { setSearch(v); reset(); }} placeholder="Search by customer name or number…" className="flex-1 sm:max-w-sm" />
            {tab === "received" && (
              <>
                <DateRangeFilter value={range} onChange={(r) => { setRange(r); reset(); }} />
                <FilterMenu
                  label="Type"
                  value={status}
                  onChange={(v) => { setStatus(v); reset(); }}
                  options={[
                    { value: "Paid", label: "Received" },
                    { value: "Waived", label: "Waived" },
                  ]}
                />
                <FilterMenu
                  label="Method"
                  value={method}
                  onChange={(v) => { setMethod(v); reset(); }}
                  options={methods.data?.map((m) => ({ value: String(m.id), label: m.name })) ?? []}
                />
              </>
            )}
          </div>
        </div>

        {tab === "received" ? (
          <ListState
            loading={received.isPending}
            error={received.isError}
            onRetry={() => void received.refetch()}
            empty={received.data?.items.length === 0}
            emptyState={
              <EmptyState
                icon={Wallet}
                title={filtered ? "No payments match these filters" : "No payments yet"}
                description={filtered ? "Try widening the date range or clearing a filter." : "Payments appear here as consultations are paid."}
                className="py-16"
              />
            }
          >
            {received.data && (
              <div className={cn("transition-opacity", received.isFetching && "opacity-70")}>
                <ul className="divide-y divide-line">
                  {received.data.items.map((p, i) => (
                    <PaymentRow key={p.id} p={p} index={i} currency={currency} onOpen={() => setOpenBooking(p.booking.id)} />
                  ))}
                </ul>
                <Pagination page={received.data.page} pageSize={PAGE_SIZE} totalCount={received.data.totalCount} onPageChange={setPage} noun="payments" />
              </div>
            )}
          </ListState>
        ) : (
          <ListState
            loading={outstanding.isPending}
            error={outstanding.isError}
            onRetry={() => void outstanding.refetch()}
            empty={outstanding.data?.items.length === 0}
            emptyState={
              <EmptyState
                icon={PartyPopper}
                title={debounced ? "Nobody matching owes anything" : "Nothing outstanding"}
                description={debounced ? "Try another name or number." : "Every completed consultation is paid or waived."}
                className="py-16"
              />
            }
          >
            {outstanding.data && (
              <div className={cn("transition-opacity", outstanding.isFetching && "opacity-70")}>
                <ul className="divide-y divide-line">
                  {outstanding.data.items.map((o, i) => (
                    <OutstandingRow
                      key={o.bookingId}
                      o={o}
                      index={i}
                      currency={currency}
                      onOpen={() => setOpenBooking(o.bookingId)}
                      onRecord={canRecord ? () => setRecording({ bookingId: o.bookingId, customerName: o.customer.name, amount: o.balance }) : undefined}
                    />
                  ))}
                </ul>
                <Pagination page={outstanding.data.page} pageSize={PAGE_SIZE} totalCount={outstanding.data.totalCount} onPageChange={setPage} noun="consultations" />
              </div>
            )}
          </ListState>
        )}
      </Card>

      <BookingDetailsDrawer bookingId={openBooking} onClose={() => setOpenBooking(null)} onBookingChange={setOpenBooking} />
      <RecordPaymentDrawer pending={recording} onClose={() => setRecording(null)} />
    </RequirePermission>
  );
}

function ListState({
  loading,
  error,
  onRetry,
  empty,
  emptyState,
  children,
}: {
  loading: boolean;
  error: boolean;
  onRetry: () => void;
  empty: boolean;
  emptyState: React.ReactNode;
  children: React.ReactNode;
}) {
  if (loading)
    return (
      <div className="divide-y divide-line" aria-busy="true" aria-label="Loading">
        {Array.from({ length: 5 }, (_, i) => (
          <div key={i} className="flex items-center gap-4 px-4 py-4">
            <Skeleton className="h-4 w-40" />
            <Skeleton className="ml-auto h-5 w-20" />
          </div>
        ))}
      </div>
    );
  if (error)
    return (
      <EmptyState
        icon={CircleAlert}
        title="Couldn't load payments"
        description="Check your connection and try again."
        action={
          <Button variant="secondary" onClick={onRetry}>
            <RotateCcw className="size-4" /> Try again
          </Button>
        }
      />
    );
  return <>{empty ? emptyState : children}</>;
}

function PaymentRow({ p, index, currency, onOpen }: { p: PaymentListItem; index: number; currency: string; onOpen: () => void }) {
  const waived = p.status === "Waived";
  return (
    <motion.li
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      transition={{ delay: Math.min(index, 10) * 0.02 }}
      className="transition-colors hover:bg-brand-soft/40"
    >
      <button type="button" onClick={onOpen} className="flex w-full min-w-0 items-center gap-4 px-4 py-3.5 text-left">
        <div className="min-w-0 flex-1">
          <p className="truncate font-semibold">{p.customer.name}</p>
          <p className="mt-0.5 truncate text-xs text-muted">
            Consultation {formatDate(p.booking.date)}, {formatTime(p.booking.startTime)} · {p.booking.treatments.join(" + ")}
          </p>
        </div>
        <div className="hidden text-right text-xs text-muted sm:block">
          <p>{p.method?.name ?? (waived ? "Waived" : "—")}</p>
          <p title={formatDateTime(p.paymentDate ?? p.createdAt)}>{formatDate((p.paymentDate ?? p.createdAt).slice(0, 10))}</p>
        </div>
        <div className="w-32 text-right">
          <p className={cn("font-display font-bold tabular-nums", waived && "text-muted line-through")}>{formatMoney(p.amount, currency)}</p>
          <span
            className={cn(
              "mt-0.5 inline-block rounded-full px-2 py-0.5 text-[11px] font-semibold",
              waived ? "bg-slate-100 text-slate-600" : p.bookingBalance > 0 ? "bg-amber-50 text-amber-700" : "bg-emerald-50 text-emerald-700",
            )}
          >
            {waived ? "Waived" : p.bookingBalance > 0 ? `${formatMoney(p.bookingBalance, currency)} left` : "Received"}
          </span>
        </div>
      </button>
    </motion.li>
  );
}

function OutstandingRow({
  o,
  index,
  currency,
  onOpen,
  onRecord,
}: {
  o: OutstandingItem;
  index: number;
  currency: string;
  onOpen: () => void;
  onRecord?: () => void;
}) {
  return (
    <motion.li
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      transition={{ delay: Math.min(index, 10) * 0.02 }}
      className="flex items-center gap-4 px-4 py-3.5 transition-colors hover:bg-amber-50/50"
    >
      <button type="button" onClick={onOpen} className="flex min-w-0 flex-1 items-center gap-4 text-left">
        <div className="min-w-0 flex-1">
          <p className="truncate font-semibold">{o.customer.name}</p>
          <p className="mt-0.5 truncate text-xs text-muted">
            Consultation {formatDate(o.date)}, {formatTime(o.startTime)} · {o.treatments.join(" + ")}
          </p>
        </div>
        <div className="hidden text-right text-xs text-muted sm:block">
          <p>Amount {formatMoney(o.charge, currency)}</p>
          <p>Paid {formatMoney(o.paid, currency)}</p>
        </div>
        <div className="w-28 text-right">
          <p className="font-display font-bold tabular-nums text-amber-700">{formatMoney(o.balance, currency)}</p>
          <span className="mt-0.5 inline-block rounded-full bg-amber-50 px-2 py-0.5 text-[11px] font-semibold text-amber-700">
            {o.paid > 0 ? "Balance" : "Unpaid"}
          </span>
        </div>
      </button>
      {onRecord && (
        <Button size="sm" onClick={onRecord}>
          Record payment
        </Button>
      )}
    </motion.li>
  );
}
