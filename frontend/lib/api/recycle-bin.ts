"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { toastError } from "@/lib/api/admin";
import { api } from "@/lib/api/client";
import type { Paged } from "@/types/customers";

/** A record type, as the API names it in URLs. */
export type RecordType =
  | "customer"
  | "booking"
  | "payment"
  | "treatment"
  | "stage"
  | "lead-source"
  | "payment-method"
  | "cancellation-reason";

export interface RecycleBinItem {
  type: RecordType;
  typeLabel: string;
  id: number;
  title: string;
  detail: string | null;
  deletedAt: string;
  deletedBy: string | null;
}

/** Where each type is deleted: DELETE /api/{path}/{id}. */
const DELETE_PATH: Record<RecordType, string> = {
  customer: "customers",
  booking: "bookings",
  payment: "payments",
  treatment: "treatments",
  stage: "stages",
  "lead-source": "lead-sources",
  "payment-method": "payment-methods",
  "cancellation-reason": "cancellation-reasons",
};

/** Deleting or restoring changes lists, totals and balances almost everywhere. */
function useEverythingChanged() {
  const qc = useQueryClient();
  return () => void qc.invalidateQueries();
}

/**
 * Deletes a record to the recycle bin. The API refuses (with a message saying what to delete
 * first) while lower-level records still depend on it; that message is shown as a toast.
 */
export function useDeleteRecord(type: RecordType) {
  const changed = useEverythingChanged();
  return useMutation({
    mutationFn: (id: number) => api.delete<null>(`/${DELETE_PATH[type]}/${id}`),
    onSuccess: changed,
    onError: toastError,
  });
}

export function useRecycleBin(q: { type?: string; search?: string; page?: number; pageSize?: number }) {
  const params = new URLSearchParams();
  for (const [k, v] of Object.entries(q)) if (v !== undefined && v !== "") params.set(k, String(v));
  return useQuery({
    queryKey: ["recycle-bin", q],
    queryFn: ({ signal }) => api.get<Paged<RecycleBinItem>>(`/recycle-bin?${params}`, { signal }),
    placeholderData: keepPreviousData,
  });
}

export function useRestoreRecord() {
  const changed = useEverythingChanged();
  return useMutation({
    mutationFn: (item: RecycleBinItem) => api.post<null>(`/recycle-bin/${item.type}/${item.id}/restore`),
    onSuccess: (_r, item) => {
      toast.success(`${item.title} restored`);
      changed();
    },
    onError: toastError,
  });
}
