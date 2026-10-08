"use client";

import { motion } from "framer-motion";
import { CalendarPlus } from "lucide-react";
import { BookingStatusBadge, formatMoney, formatTime } from "@/components/bookings/booking-status";
import { Card, CardHeader } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { useBookings, useLocale } from "@/lib/api/bookings";
import { formatDate, today } from "@/lib/dates";
import { cn } from "@/lib/utils";

/** A customer's next consultation, then their full booking history (spec §13). */
export function CustomerBookingsCard({
  customerId,
  onOpen,
  onBook,
}: {
  customerId: number;
  onOpen: (id: number) => void;
  onBook?: () => void;
}) {
  const { data, isPending } = useBookings({ customerId, pageSize: 100, sort: "desc" });
  const { data: locale } = useLocale();
  const t = locale?.today ?? today();

  const items = data?.items ?? [];
  const upcoming = items.filter((b) => b.status === "Booked" && b.date >= t).sort((a, b) => (a.date + a.startTime).localeCompare(b.date + b.startTime));
  const next = upcoming[0];
  const history = items.filter((b) => b.id !== next?.id);

  return (
    <Card>
      <CardHeader
        title="Bookings"
        action={
          onBook && (
            <button type="button" onClick={onBook} className="inline-flex items-center gap-1.5 text-sm font-semibold text-brand hover:text-brand-strong">
              <CalendarPlus className="size-4" /> Book
            </button>
          )
        }
      />
      <div className="p-5">
        {isPending ? (
          <div className="space-y-3" aria-busy="true" aria-label="Loading">
            <Skeleton className="h-20 w-full rounded-2xl" />
            <Skeleton className="h-10 w-full rounded-xl" />
          </div>
        ) : items.length === 0 ? (
          <p className="text-sm text-muted">No consultations booked yet.</p>
        ) : (
          <div className="space-y-5">
            {next && (
              <motion.button
                type="button"
                initial={{ opacity: 0, y: 6 }}
                animate={{ opacity: 1, y: 0 }}
                whileHover={{ y: -2 }}
                onClick={() => onOpen(next.id)}
                className="w-full rounded-2xl border border-brand/20 bg-gradient-to-br from-brand-soft to-transparent p-4 text-left"
              >
                <p className="text-[11px] font-semibold uppercase tracking-[0.06em] text-brand">Upcoming</p>
                <p className="mt-1 font-display text-lg font-bold">
                  {formatDate(next.date)} · {formatTime(next.startTime)}
                </p>
                <p className="mt-0.5 text-sm text-foreground/75">
                  {next.treatments.map((x) => x.name).join(" + ")}
                  {next.doctor && ` · ${next.doctor.name}`}
                </p>
              </motion.button>
            )}

            {history.length > 0 && (
              <ol className="relative space-y-1 before:absolute before:bottom-3 before:left-[7px] before:top-3 before:w-px before:bg-line">
                {history.map((b) => (
                  <li key={b.id}>
                    <button type="button" onClick={() => onOpen(b.id)} className="relative flex w-full items-center gap-3 rounded-xl py-2 pl-6 pr-2 text-left hover:bg-surface-muted/70">
                      <span className={cn("absolute left-0.5 top-1/2 size-3 -translate-y-1/2 rounded-full border-2 border-surface", b.status === "Completed" ? "bg-success" : b.status === "Booked" ? "bg-brand" : "bg-slate-300")} />
                      <span className="min-w-0 flex-1">
                        <span className="block text-sm font-medium">
                          {formatDate(b.date)}, {formatTime(b.startTime)}
                        </span>
                        <span className="block truncate text-xs text-muted">{b.treatments.map((x) => x.name).join(" + ")}</span>
                      </span>
                      <span className="flex shrink-0 flex-col items-end gap-0.5">
                        <BookingStatusBadge status={b.status} />
                        {b.status === "Completed" && b.consultationCharge !== null && (
                          <span className="text-[11px] text-muted">
                            {formatMoney(b.consultationCharge, locale?.currency)}
                            {b.balance > 0 && <span className="font-semibold text-amber-700"> · {formatMoney(b.balance, locale?.currency)} owed</span>}
                          </span>
                        )}
                      </span>
                    </button>
                  </li>
                ))}
              </ol>
            )}
          </div>
        )}
      </div>
    </Card>
  );
}
