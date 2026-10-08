"use client";

import { useState } from "react";
import { formatMoney } from "@/components/bookings/booking-status";
import { RecordPaymentDrawer, type PendingPayment } from "@/components/payments/record-payment-drawer";
import { Button } from "@/components/ui/button";
import { Card, CardHeader } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { useOutstanding, usePayments, usePaymentSummary } from "@/lib/api/payments";
import { useSession } from "@/lib/auth/session";
import { formatDate } from "@/lib/dates";
import { can, Permission } from "@/lib/permissions";
import { cn } from "@/lib/utils";

/**
 * The customer's money (spec §28): what they paid and still owe, each consultation with a balance
 * (to take a payment), then every payment, newest first.
 */
export function CustomerPaymentsCard({ customerId, onOpenBooking }: { customerId: number; onOpenBooking: (id: number) => void }) {
  const { data: session } = useSession();
  const canRecord = can(session?.user, Permission.PaymentsManage);
  const summary = usePaymentSummary({ customerId });
  const owing = useOutstanding({ customerId, pageSize: 50 });
  const list = usePayments({ customerId, pageSize: 50 });
  const [recording, setRecording] = useState<PendingPayment | null>(null);
  const currency = summary.data?.currency ?? "AED";
  const outstanding = summary.data?.outstanding ?? 0;

  return (
    <Card className="self-start">
      <CardHeader title="Payments" />
      <div className="p-5">
        {summary.isPending || list.isPending || owing.isPending ? (
          <div className="space-y-3" aria-busy="true" aria-label="Loading">
            <Skeleton className="h-14 w-full rounded-xl" />
            <Skeleton className="h-8 w-full" />
          </div>
        ) : (list.data?.items.length ?? 0) === 0 && (owing.data?.items.length ?? 0) === 0 ? (
          <p className="text-sm text-muted">No payments yet.</p>
        ) : (
          <>
            <div className="grid grid-cols-2 gap-3">
              <div className="rounded-xl bg-emerald-50/70 p-3">
                <p className="text-xs font-medium text-emerald-700">Paid</p>
                <p className="mt-0.5 font-display text-lg font-bold">{formatMoney(summary.data?.collected ?? 0, currency)}</p>
              </div>
              <div className={cn("rounded-xl p-3", outstanding > 0 ? "bg-amber-50/80" : "bg-surface-muted")}>
                <p className="text-xs font-medium text-amber-700">Outstanding</p>
                <p className="mt-0.5 font-display text-lg font-bold">{formatMoney(outstanding, currency)}</p>
              </div>
            </div>

            {owing.data && owing.data.items.length > 0 && (
              <div className="mt-4">
                <p className="text-xs font-semibold uppercase tracking-[0.06em] text-muted">Still owed</p>
                <ul className="mt-1 divide-y divide-line">
                  {owing.data.items.map((o) => (
                    <li key={o.bookingId} className="flex items-center justify-between gap-3 py-2.5">
                      <button type="button" onClick={() => onOpenBooking(o.bookingId)} className="min-w-0 text-left hover:text-brand-strong">
                        <span className="block text-sm font-medium">{formatDate(o.date)}</span>
                        <span className="block truncate text-xs text-muted">
                          {formatMoney(o.charge, currency)} · paid {formatMoney(o.paid, currency)}
                        </span>
                      </button>
                      <span className="flex shrink-0 items-center gap-2">
                        <span className="text-sm font-semibold tabular-nums text-amber-700">{formatMoney(o.balance, currency)}</span>
                        {canRecord && (
                          <Button
                            size="sm"
                            variant="secondary"
                            onClick={() => setRecording({ bookingId: o.bookingId, customerName: o.customer.name, amount: o.balance })}
                          >
                            Pay
                          </Button>
                        )}
                      </span>
                    </li>
                  ))}
                </ul>
              </div>
            )}

            {list.data && list.data.items.length > 0 && (
              <div className="mt-4">
                <p className="text-xs font-semibold uppercase tracking-[0.06em] text-muted">History</p>
                <ul className="mt-1 divide-y divide-line">
                  {list.data.items.map((p) => {
                    const waived = p.status === "Waived";
                    return (
                      <li key={p.id}>
                        <button
                          type="button"
                          onClick={() => onOpenBooking(p.booking.id)}
                          className="flex w-full items-center justify-between gap-3 py-2.5 text-left hover:text-brand-strong"
                        >
                          <span className="min-w-0">
                            <span className="block text-sm font-medium">{formatDate((p.paymentDate ?? p.createdAt).slice(0, 10))}</span>
                            <span className="block truncate text-xs text-muted">
                              {p.booking.treatments.join(" + ")}
                              {p.method && ` · ${p.method.name}`}
                            </span>
                          </span>
                          <span className="shrink-0 text-right">
                            <span className={cn("block text-sm font-semibold tabular-nums", waived && "text-muted line-through")}>
                              {formatMoney(p.amount, currency)}
                            </span>
                            <span className={cn("block text-[11px] font-semibold", waived ? "text-slate-500" : "text-emerald-700")}>
                              {waived ? "Waived" : "Received"}
                            </span>
                          </span>
                        </button>
                      </li>
                    );
                  })}
                </ul>
              </div>
            )}
          </>
        )}
      </div>
      <RecordPaymentDrawer pending={recording} onClose={() => setRecording(null)} />
    </Card>
  );
}
