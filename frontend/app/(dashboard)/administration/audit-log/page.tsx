"use client";

import { CircleAlert, RotateCcw, ScrollText } from "lucide-react";
import Link from "next/link";
import { useState } from "react";
import { PageHeader } from "@/components/layout/page-header";
import { RequirePermission } from "@/components/layout/require-permission";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { DateRangeFilter } from "@/components/ui/date-range-filter";
import { EmptyState } from "@/components/ui/empty-state";
import { FilterMenu } from "@/components/ui/filter-menu";
import { Pagination } from "@/components/ui/pagination";
import { SearchInput } from "@/components/ui/search-input";
import { Skeleton } from "@/components/ui/skeleton";
import { useDebouncedValue } from "@/hooks/use-debounced-value";
import { useAuditFilters, useAuditLogs } from "@/lib/api/audit";
import type { DateRange } from "@/lib/dates";
import { formatDateTime, formatRelative } from "@/lib/format";
import { Permission } from "@/lib/permissions";
import { cn } from "@/lib/utils";
import type { AuditLogEntry } from "@/types/audit";

const PAGE_SIZE = 50;

/** Colour of the dot beside an action: problems stand out, sign-ins recede. */
function actionTone(action: string) {
  if (/fail|refus|revok|deactivat|cancel|no show/i.test(action)) return "bg-amber-500";
  if (/logged (in|out)/i.test(action)) return "bg-slate-300";
  if (/creat|added|activat|complet|recorded/i.test(action)) return "bg-emerald-500";
  return "bg-brand";
}

/** "customerId" → "Customer id", "newBookingId" → "New booking id". */
function humanize(key: string) {
  const words = key.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[_-]+/g, " ").trim().toLowerCase();
  return words.charAt(0).toUpperCase() + words.slice(1);
}

function formatValue(value: unknown): string {
  if (value === null || value === undefined || value === "") return "";
  if (Array.isArray(value)) return value.map(formatValue).filter(Boolean).join(", ");
  if (typeof value === "object") {
    return Object.entries(value as Record<string, unknown>)
      .map(([k, v]) => {
        const text = formatValue(v);
        return text && `${humanize(k).toLowerCase()} ${text}`;
      })
      .filter(Boolean)
      .join(", ");
  }
  if (typeof value === "boolean") return value ? "yes" : "no";
  const text = String(value);
  return text.length > 80 ? `${text.slice(0, 77)}…` : text;
}

/** The entry's details as short "Label: value" pairs, skipping empty values. */
function describeDetails(details: AuditLogEntry["details"]): [string, string][] {
  if (details === null || details === undefined) return [];
  if (typeof details !== "object" || Array.isArray(details)) {
    const text = formatValue(details);
    return text ? [["Details", text]] : [];
  }
  return Object.entries(details)
    .map(([k, v]) => [humanize(k), formatValue(v)] as [string, string])
    .filter(([, v]) => v !== "");
}

function Subject({ entry }: { entry: AuditLogEntry }) {
  const kind = humanize(entry.entityType);
  const label = entry.subject ?? (entry.entityId ? `${kind} #${entry.entityId}` : null);
  if (!label) return null;
  const suffix = entry.entityType === "Booking" && entry.entityId ? ` · booking #${entry.entityId}` : "";
  return entry.customerId ? (
    <Link href={`/customers/${entry.customerId}`} className="truncate font-medium text-brand-strong hover:underline">
      {label}
      <span className="font-normal text-muted">{suffix}</span>
    </Link>
  ) : (
    <span className="truncate text-foreground/80">{label}</span>
  );
}

export default function AuditLogPage() {
  const [search, setSearch] = useState("");
  const [range, setRange] = useState<DateRange>({});
  const [action, setAction] = useState<string | undefined>();
  const [user, setUser] = useState<string | undefined>();
  const [page, setPage] = useState(1);
  const debounced = useDebouncedValue(search, 300);
  const reset = () => setPage(1);

  const filters = useAuditFilters();
  const { data, isPending, isError, isFetching, refetch } = useAuditLogs({
    search: debounced.trim() || undefined,
    from: range.from,
    to: range.to,
    action,
    userId: user ? Number(user) : undefined,
    page,
    pageSize: PAGE_SIZE,
  });
  const filtered = !!(debounced.trim() || range.from || range.to || action || user);

  return (
    <RequirePermission permission={Permission.AuditView}>
      <PageHeader title="Audit Log" description="Every sign-in, change and setting update, newest first." />

      <Card className="overflow-hidden">
        <div className="flex flex-col gap-3 border-b border-line p-4">
          <div className="flex items-center gap-3">
            <SearchInput
              value={search}
              onChange={(v) => { setSearch(v); reset(); }}
              placeholder="Search by user, action or customer…"
              className="flex-1 sm:max-w-sm"
            />
            {isFetching && !isPending && <span className="size-2 animate-pulse rounded-full bg-brand" aria-label="Loading" />}
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <DateRangeFilter value={range} onChange={(r) => { setRange(r); reset(); }} />
            <FilterMenu
              label="Action"
              value={action}
              onChange={(v) => { setAction(v); reset(); }}
              options={filters.data?.actions.map((a) => ({ value: a, label: a })) ?? []}
            />
            <FilterMenu
              label="User"
              value={user}
              onChange={(v) => { setUser(v); reset(); }}
              options={filters.data?.users.map((u) => ({ value: String(u.id), label: u.name })) ?? []}
            />
          </div>
        </div>

        {isPending ? (
          <div className="divide-y divide-line" aria-busy="true" aria-label="Loading">
            {Array.from({ length: 8 }, (_, i) => (
              <div key={i} className="flex items-start gap-3 p-4">
                <Skeleton className="mt-1.5 size-2 rounded-full" />
                <div className="flex-1 space-y-2">
                  <Skeleton className="h-4 w-48" />
                  <Skeleton className="h-3 w-72 max-w-full" />
                </div>
                <Skeleton className="h-3 w-24" />
              </div>
            ))}
          </div>
        ) : isError ? (
          <EmptyState
            icon={CircleAlert}
            title="Couldn't load the audit log"
            description="Check your connection and try again."
            action={
              <Button variant="secondary" onClick={() => refetch()}>
                <RotateCcw className="size-4" /> Try again
              </Button>
            }
          />
        ) : data.items.length === 0 ? (
          <EmptyState
            icon={ScrollText}
            title={filtered ? "No entries match" : "Nothing recorded yet"}
            description={filtered ? "Try a different search, date or filter." : "Sign-ins and changes will appear here as people use the CRM."}
          />
        ) : (
          <ul className={cn("divide-y divide-line transition-opacity", isFetching && "opacity-70")}>
            {data.items.map((e) => {
              const details = describeDetails(e.details);
              return (
                <li key={e.id} className="flex gap-3 p-4 transition-colors hover:bg-surface-muted/40">
                  <span className={cn("mt-1.5 size-2 shrink-0 rounded-full", actionTone(e.action))} aria-hidden />
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5 text-sm">
                      <span className="font-semibold">{e.action}</span>
                      <Subject entry={e} />
                    </div>
                    {details.length > 0 && (
                      <dl className="mt-1 flex flex-wrap gap-x-3 gap-y-0.5 text-xs text-muted">
                        {details.map(([k, v]) => (
                          <div key={k} className="min-w-0 break-words">
                            <dt className="inline">{k}: </dt>
                            <dd className="inline text-foreground/70">{v}</dd>
                          </div>
                        ))}
                      </dl>
                    )}
                    <p className="mt-1 text-xs text-muted sm:hidden">
                      {e.userName ?? "System"} · {formatRelative(e.createdAt)}
                    </p>
                  </div>
                  <div className="hidden w-44 shrink-0 text-right text-xs sm:block">
                    <p className="truncate font-medium text-foreground/80">{e.userName ?? "System"}</p>
                    <p className="text-muted" title={formatRelative(e.createdAt)}>
                      {formatDateTime(e.createdAt)}
                    </p>
                  </div>
                </li>
              );
            })}
          </ul>
        )}

        {data && data.totalCount > 0 && (
          <Pagination page={page} pageSize={PAGE_SIZE} totalCount={data.totalCount} onPageChange={setPage} noun="entries" />
        )}
      </Card>
    </RequirePermission>
  );
}
