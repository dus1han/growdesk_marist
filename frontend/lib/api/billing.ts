"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api/client";
import type { BillingInvoice, BillingNotice, BillingOverview, BillingSettings, PayNowResult, SaveBillingSettings } from "@/types/billing";

export const billingKeys = {
  all: ["billing"] as const,
  notice: ["billing", "notice"] as const,
  overview: ["billing", "overview"] as const,
  invoices: ["billing", "invoices"] as const,
  settings: ["billing", "settings"] as const,
};

/** Whether the system is blocked and how many days are left to pay. Every signed-in user. */
export function useBillingNotice(enabled = true) {
  return useQuery({
    queryKey: billingKeys.notice,
    queryFn: ({ signal }) => api.get<BillingNotice>("/billing/notice", { signal }),
    enabled,
    staleTime: 5 * 60_000,
    // A countdown measured in days: re-check now and then so a tab left open doesn't go stale.
    refetchInterval: 15 * 60_000,
  });
}

export function useBillingOverview(enabled = true) {
  return useQuery({
    queryKey: billingKeys.overview,
    queryFn: ({ signal }) => api.get<BillingOverview>("/billing", { signal }),
    enabled,
  });
}

export function useBillingInvoices(enabled = true) {
  return useQuery({
    queryKey: billingKeys.invoices,
    queryFn: ({ signal }) => api.get<BillingInvoice[]>("/billing/invoices", { signal }),
    enabled,
  });
}

/** Re-reads the subscription from Stripe, e.g. after paying on a Stripe page. */
export function useSyncBilling() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => api.post<BillingOverview>("/billing/sync"),
    onSuccess: (overview) => {
      queryClient.setQueryData(billingKeys.overview, overview);
      queryClient.setQueryData(billingKeys.notice, overview.notice);
      void queryClient.invalidateQueries({ queryKey: billingKeys.invoices });
    },
  });
}

export function usePayNow() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => api.post<PayNowResult>("/billing/pay-now"),
    onSuccess: (result) => {
      if (result.paid) void queryClient.invalidateQueries({ queryKey: billingKeys.all });
    },
  });
}

export function useSubscribe() {
  return useMutation({ mutationFn: () => api.post<{ url: string }>("/billing/subscribe") });
}

export function useBillingPortal() {
  return useMutation({ mutationFn: () => api.post<{ url: string }>("/billing/portal") });
}

/** Stripe Settings (platform owners only). */
export function useBillingSettings() {
  return useQuery({
    queryKey: billingKeys.settings,
    queryFn: ({ signal }) => api.get<BillingSettings>("/billing/settings", { signal }),
  });
}

export function useSaveBillingSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: SaveBillingSettings) => api.put<BillingSettings>("/billing/settings", input),
    onSuccess: (settings) => {
      queryClient.setQueryData(billingKeys.settings, settings);
      // Switching billing on or off, or a new plan, changes every billing screen.
      void queryClient.invalidateQueries({ queryKey: billingKeys.notice });
      void queryClient.invalidateQueries({ queryKey: billingKeys.overview });
      void queryClient.invalidateQueries({ queryKey: billingKeys.invoices });
    },
  });
}

/** "AED 500.00" from Stripe's minor units, respecting zero-decimal currencies. */
export function formatStripeAmount(amount: number, currency: string) {
  const code = currency.toUpperCase();
  const format = new Intl.NumberFormat(undefined, { style: "currency", currency: code });
  const digits = format.resolvedOptions().maximumFractionDigits ?? 2;
  return format.format(amount / 10 ** digits);
}

export function formatDate(iso: string | null | undefined) {
  if (!iso) return "";
  return new Date(iso).toLocaleDateString(undefined, { day: "numeric", month: "short", year: "numeric" });
}
