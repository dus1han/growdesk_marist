"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api/client";
import type { AuditFilters, AuditLogEntry, AuditQuery } from "@/types/audit";
import type { Paged } from "@/types/customers";

function queryString(q: AuditQuery) {
  const p = new URLSearchParams();
  for (const [k, v] of Object.entries(q)) if (v !== undefined && v !== null && v !== "") p.set(k, String(v));
  return p.toString();
}

export function useAuditLogs(q: AuditQuery) {
  return useQuery({
    queryKey: ["audit-logs", q],
    queryFn: ({ signal }) => api.get<Paged<AuditLogEntry>>(`/audit-logs?${queryString(q)}`, { signal }),
    placeholderData: keepPreviousData,
  });
}

export function useAuditFilters() {
  return useQuery({
    queryKey: ["audit-logs", "filters"],
    queryFn: ({ signal }) => api.get<AuditFilters>("/audit-logs/filters", { signal }),
    staleTime: 60_000,
  });
}
