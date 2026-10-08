"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api/client";
import type { Paged } from "@/types/customers";
import type { OutstandingItem, PaymentListItem, PaymentQuery, PaymentSummary } from "@/types/payments";

export function paymentQueryString(q: PaymentQuery) {
  const p = new URLSearchParams();
  for (const [k, v] of Object.entries(q)) if (v !== undefined && v !== null && v !== "") p.set(k, String(v));
  return p.toString();
}

export function usePayments(q: PaymentQuery, enabled = true) {
  return useQuery({
    queryKey: ["payments", "list", q],
    queryFn: ({ signal }) => api.get<Paged<PaymentListItem>>(`/payments?${paymentQueryString(q)}`, { signal }),
    placeholderData: keepPreviousData,
    enabled,
  });
}

/** Summary uses the same filters, minus paging and status (see backend PaymentSummaryDto). */
export function usePaymentSummary(q: PaymentQuery, enabled = true) {
  const filters: PaymentQuery = { from: q.from, to: q.to, paymentMethodId: q.paymentMethodId, customerId: q.customerId, search: q.search };
  return useQuery({
    queryKey: ["payments", "summary", filters],
    queryFn: ({ signal }) => api.get<PaymentSummary>(`/payments/summary?${paymentQueryString(filters)}`, { signal }),
    placeholderData: keepPreviousData,
    enabled,
  });
}

/** Completed consultations still owed money, oldest first. */
export function useOutstanding(q: { customerId?: number; search?: string; page?: number; pageSize?: number }, enabled = true) {
  return useQuery({
    queryKey: ["payments", "outstanding", q],
    queryFn: ({ signal }) => api.get<Paged<OutstandingItem>>(`/payments/outstanding?${paymentQueryString(q)}`, { signal }),
    placeholderData: keepPreviousData,
    enabled,
  });
}

/** After money changes: payments, bookings and customers (their outstanding) are all stale. */
function useMoneyChanged() {
  const qc = useQueryClient();
  return (bookingId: number) => {
    void qc.invalidateQueries({ queryKey: ["payments"] });
    void qc.invalidateQueries({ queryKey: ["bookings"] });
    void qc.invalidateQueries({ queryKey: ["bookings", "detail", bookingId] });
    void qc.invalidateQueries({ queryKey: ["customers"] });
  };
}

/** Money received: any amount up to the balance. */
export function useRecordPayment() {
  const changed = useMoneyChanged();
  return useMutation({
    mutationFn: ({ bookingId, ...input }: { bookingId: number; amount: number; paymentMethodId: number; paymentDate: string | null }) =>
      api.post<PaymentListItem>(`/bookings/${bookingId}/payments`, input),
    onSuccess: (_p, { bookingId }) => changed(bookingId),
  });
}

/** Writes off what a consultation still owes. */
export function useWaiveBalance() {
  const changed = useMoneyChanged();
  return useMutation({
    mutationFn: (bookingId: number) => api.post<PaymentListItem>(`/bookings/${bookingId}/payments/waive`),
    onSuccess: (_p, bookingId) => changed(bookingId),
  });
}
