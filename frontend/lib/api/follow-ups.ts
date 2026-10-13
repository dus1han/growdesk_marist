"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api/client";
import type { FollowUpLogged, FollowUpOutcome, FollowUpQueue, FollowUpSettings, FollowUpSummary } from "@/types/follow-ups";

export const followUpKeys = {
  all: ["follow-ups"] as const,
  queue: (scope: string) => ["follow-ups", "queue", scope] as const,
  summary: ["follow-ups", "summary"] as const,
  settings: ["follow-ups", "settings"] as const,
};

/** Due today and overdue. "mine": own and unassigned customers; "all": everyone's. */
export function useFollowUpQueue(scope: "mine" | "all") {
  return useQuery({
    queryKey: followUpKeys.queue(scope),
    queryFn: ({ signal }) => api.get<FollowUpQueue>(`/follow-ups?scope=${scope}`, { signal }),
    refetchInterval: 60_000,
  });
}

/**
 * Counts for the sidebar badge, and new enquiries for alerts. Checked every minute while
 * GrowDesk is open, also in a background tab, so reminders and alerts arrive on time.
 */
export function useFollowUpSummary(enabled = true) {
  return useQuery({
    queryKey: followUpKeys.summary,
    queryFn: ({ signal }) => api.get<FollowUpSummary>("/follow-ups/summary", { signal }),
    enabled,
    refetchInterval: 60_000,
    refetchIntervalInBackground: true,
    staleTime: 30_000,
  });
}

export function useLogFollowUp() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ customerId, ...input }: { customerId: number; outcome: FollowUpOutcome; note: string | null; nextFollowUpDate: string | null }) =>
      api.post<FollowUpLogged>(`/customers/${customerId}/follow-ups`, input),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: followUpKeys.all });
      void qc.invalidateQueries({ queryKey: ["customers"] });
    },
  });
}

export function useFollowUpSettings() {
  return useQuery({
    queryKey: followUpKeys.settings,
    queryFn: ({ signal }) => api.get<FollowUpSettings>("/follow-ups/settings", { signal }),
    staleTime: 5 * 60_000,
  });
}

export function useSaveFollowUpSettings() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (input: FollowUpSettings) => api.put<FollowUpSettings>("/follow-ups/settings", input),
    onSuccess: (saved) => {
      qc.setQueryData(followUpKeys.settings, saved);
      void qc.invalidateQueries({ queryKey: followUpKeys.summary });
    },
  });
}
