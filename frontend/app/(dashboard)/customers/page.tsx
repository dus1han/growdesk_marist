"use client";

import { motion } from "framer-motion";
import { CircleAlert, Plus, RotateCcw, Users } from "lucide-react";
import { useRouter } from "next/navigation";
import { Suspense, useEffect, useState } from "react";
import { CustomerFormDrawer } from "@/components/customers/customer-form-drawer";
import { formatMoney } from "@/components/bookings/booking-status";
import { CONSULTATION, ConsultationBadge } from "@/components/customers/consultation-badge";
import { StageBadge, TreatmentChips } from "@/components/customers/stage-badge";
import { WhatsAppLink } from "@/components/customers/whatsapp-link";
import { PageHeader } from "@/components/layout/page-header";
import { RequirePermission } from "@/components/layout/require-permission";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { FilterMenu } from "@/components/ui/filter-menu";
import { Pagination } from "@/components/ui/pagination";
import { SearchInput } from "@/components/ui/search-input";
import { Skeleton } from "@/components/ui/skeleton";
import { useCustomerFilters } from "@/hooks/use-customer-filters";
import { useDebouncedValue } from "@/hooks/use-debounced-value";
import { useLocale } from "@/lib/api/bookings";
import { CUSTOMER_PAGE_SIZE, useActiveLookup, useCustomers, useUserOptions } from "@/lib/api/customers";
import { useSession } from "@/lib/auth/session";
import { CREATED_PRESETS, followUpState, FOLLOW_UP_PRESETS, formatDate } from "@/lib/dates";
import { can, Permission } from "@/lib/permissions";
import { cn, initials } from "@/lib/utils";
import type { CustomerListItem } from "@/types/customers";

export default function CustomersPage() {
  return (
    <RequirePermission permission={Permission.CustomersView}>
      {/* useSearchParams (URL filters) needs a Suspense boundary. */}
      <Suspense fallback={<ListSkeleton />}>
        <CustomersView />
      </Suspense>
    </RequirePermission>
  );
}

function CustomersView() {
  const router = useRouter();
  const { data: session } = useSession();
  const canManage = can(session?.user, Permission.CustomersManage);
  const showMoney = can(session?.user, Permission.PaymentsView);
  const { values, apiFilters, set, clearAll, activeCount } = useCustomerFilters();
  const [adding, setAdding] = useState(false);

  // Search box is local state, pushed to the URL after typing pauses.
  const [search, setSearch] = useState(values.q);
  const debounced = useDebouncedValue(search, 300);
  useEffect(() => {
    if (debounced !== values.q) set("q", debounced || undefined);
  }, [debounced]); // eslint-disable-line react-hooks/exhaustive-deps -- only react to typing

  const { data, isPending, isError, isFetching, refetch } = useCustomers(apiFilters);
  const stages = useActiveLookup("stages");
  const treatments = useActiveLookup("treatments");
  const sources = useActiveLookup("lead-sources");
  const users = useUserOptions();

  const filtered = activeCount > 0 || !!apiFilters.search;

  return (
    <>
      <PageHeader
        title="Customers"
        description="Everyone who has shown interest in a treatment."
        actions={
          canManage && (
            <Button onClick={() => setAdding(true)}>
              <Plus className="size-4" /> Add customer
            </Button>
          )
        }
      />

      <Card className="overflow-hidden">
        <div className="space-y-3 border-b border-line p-4">
          <div className="flex items-center gap-3">
            <SearchInput value={search} onChange={setSearch} placeholder="Search by name, WhatsApp or Instagram…" className="flex-1 sm:max-w-md" />
            {isFetching && !isPending && <span className="size-2 animate-pulse rounded-full bg-brand" aria-label="Loading" />}
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <FilterMenu
              label="Status"
              value={values.stage}
              onChange={(v) => set("stage", v)}
              options={stages.data?.map((s) => ({ value: String(s.id), label: s.name, color: s.color ?? undefined })) ?? []}
            />
            <FilterMenu
              label="Consultation"
              value={values.consultation}
              onChange={(v) => set("consultation", v)}
              options={Object.entries(CONSULTATION).map(([value, m]) => ({ value, label: m.label }))}
            />
            <FilterMenu
              label="Treatment"
              value={values.treatment}
              onChange={(v) => set("treatment", v)}
              options={treatments.data?.map((t) => ({ value: String(t.id), label: t.name })) ?? []}
            />
            <FilterMenu
              label="Lead source"
              value={values.source}
              onChange={(v) => set("source", v)}
              options={sources.data?.map((s) => ({ value: String(s.id), label: s.name })) ?? []}
            />
            <FilterMenu
              label="Assigned to"
              value={values.assigned}
              onChange={(v) => set("assigned", v)}
              options={users.data?.map((u) => ({ value: String(u.id), label: u.name })) ?? []}
            />
            <FilterMenu
              label="Created"
              value={values.created}
              onChange={(v) => set("created", v)}
              options={Object.entries(CREATED_PRESETS).map(([k, p]) => ({ value: k, label: p.label }))}
            />
            <FilterMenu
              label="Follow-up"
              value={values.followup}
              onChange={(v) => set("followup", v)}
              options={Object.entries(FOLLOW_UP_PRESETS).map(([k, p]) => ({ value: k, label: p.label }))}
            />
            {showMoney && (
              <FilterMenu
                label="Payment"
                value={values.owes}
                onChange={(v) => set("owes", v)}
                options={[{ value: "yes", label: "Owes money" }]}
              />
            )}
            {activeCount > 0 && (
              <button type="button" onClick={() => { setSearch(""); clearAll(); }} className="px-2 text-sm font-medium text-muted hover:text-foreground">
                Clear all
              </button>
            )}
          </div>
        </div>

        {isPending ? (
          <ListSkeleton />
        ) : isError ? (
          <EmptyState
            icon={CircleAlert}
            title="Couldn't load customers"
            description="Check your connection and try again."
            action={
              <Button variant="secondary" onClick={() => refetch()}>
                <RotateCcw className="size-4" /> Try again
              </Button>
            }
          />
        ) : data.items.length === 0 ? (
          <EmptyState
            icon={Users}
            title={filtered ? "No customers found" : "No customers yet"}
            description={filtered ? "Try changing your filters or add a new customer." : "Customers you add, or capture from WhatsApp, appear here."}
            action={
              canManage && (
                <Button onClick={() => setAdding(true)}>
                  <Plus className="size-4" /> Add customer
                </Button>
              )
            }
            className="py-16"
          />
        ) : (
          <div className={cn("transition-opacity", isFetching && "opacity-70")}>
            <CustomerTable items={data.items} onOpen={(id) => router.push(`/customers/${id}`)} showMoney={showMoney} />
            <CustomerCards items={data.items} onOpen={(id) => router.push(`/customers/${id}`)} />
            <Pagination
              page={data.page}
              pageSize={CUSTOMER_PAGE_SIZE}
              totalCount={data.totalCount}
              onPageChange={(p) => set("page", p)}
              noun="customers"
            />
          </div>
        )}
      </Card>

      <CustomerFormDrawer open={adding} onClose={() => setAdding(false)} onSaved={(c) => router.push(`/customers/${c.id}`)} />
    </>
  );
}

function FollowUp({ date }: { date: string | null }) {
  const state = followUpState(date);
  if (!date) return <span className="text-muted">—</span>;
  return (
    <span className={cn("whitespace-nowrap", state === "overdue" && "font-semibold text-danger", state === "today" && "font-semibold text-warning")}>
      {state === "today" ? "Today" : formatDate(date)}
    </span>
  );
}

/** Desktop: a real table (clickable rows, keyboard accessible). */
function CustomerTable({ items, onOpen, showMoney }: { items: CustomerListItem[]; onOpen: (id: number) => void; showMoney: boolean }) {
  const { data: locale } = useLocale();
  return (
    <div className="hidden overflow-x-auto md:block">
      <table className="w-full text-left text-sm">
        <thead className="border-b border-line bg-surface-muted/50 text-[11px] font-semibold uppercase tracking-[0.06em] text-muted">
          <tr>
            <th className="px-4 py-3 font-semibold">Name</th>
            <th className="px-4 py-3 font-semibold">WhatsApp</th>
            <th className="px-4 py-3 font-semibold">Treatments</th>
            <th className="px-4 py-3 font-semibold">Status</th>
            <th className="px-4 py-3 font-semibold">Consultation</th>
            <th className="px-4 py-3 font-semibold">Follow-up</th>
            {showMoney && <th className="px-4 py-3 text-right font-semibold">Outstanding</th>}
            <th className="px-4 py-3 font-semibold">Created</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-line">
          {items.map((c, i) => (
            <motion.tr
              key={c.id}
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              transition={{ duration: 0.2, delay: Math.min(i, 10) * 0.02 }}
              onClick={() => onOpen(c.id)}
              onKeyDown={(e) => e.key === "Enter" && onOpen(c.id)}
              tabIndex={0}
              role="link"
              aria-label={`Open ${c.name}`}
              className="group cursor-pointer transition-colors hover:bg-brand-soft/40 focus-visible:bg-brand-soft/40 focus-visible:outline-none"
            >
              <td className="px-4 py-3">
                <div className="flex items-center gap-3">
                  <span className="flex size-8 shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-brand/80 to-accent/80 text-[11px] font-bold text-white">
                    {initials(c.name)}
                  </span>
                  <div className="min-w-0">
                    <p className="truncate font-semibold transition-colors group-hover:text-brand-strong">{c.name}</p>
                    {c.instagram && <p className="truncate text-xs text-muted">@{c.instagram}</p>}
                  </div>
                </div>
              </td>
              <td className="whitespace-nowrap px-4 py-3 text-foreground/80">{c.whatsApp ? <WhatsAppLink number={c.whatsApp} /> : <span className="text-muted">—</span>}</td>
              <td className="px-4 py-3">
                <TreatmentChips treatments={c.treatments} max={2} />
              </td>
              <td className="px-4 py-3">
                <StageBadge name={c.stage.name} color={c.stage.color} />
              </td>
              <td className="px-4 py-3">
                <ConsultationBadge consultation={c.consultation} />
              </td>
              <td className="px-4 py-3">
                <FollowUp date={c.nextFollowUpDate} />
              </td>
              {showMoney && (
                <td className="whitespace-nowrap px-4 py-3 text-right tabular-nums">
                  {c.outstanding ? (
                    <span className="font-semibold text-amber-700">{formatMoney(c.outstanding, locale?.currency)}</span>
                  ) : (
                    <span className="text-muted">—</span>
                  )}
                </td>
              )}
              <td className="whitespace-nowrap px-4 py-3 text-muted">{formatDate(c.createdAt.slice(0, 10))}</td>
            </motion.tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** Phones: cards instead of a squeezed table (spec §45). */
function CustomerCards({ items, onOpen }: { items: CustomerListItem[]; onOpen: (id: number) => void }) {
  const { data: locale } = useLocale();
  return (
    <ul className="divide-y divide-line md:hidden">
      {items.map((c) => (
        <li key={c.id}>
          {/* A clickable card rather than a <button>, so the WhatsApp link inside is valid. */}
          <div
            role="link"
            tabIndex={0}
            aria-label={`Open ${c.name}`}
            onClick={() => onOpen(c.id)}
            onKeyDown={(e) => e.key === "Enter" && onOpen(c.id)}
            className="flex w-full cursor-pointer items-start gap-3 p-4 text-left focus-visible:bg-surface-muted focus-visible:outline-none active:bg-surface-muted"
          >
            <span className="flex size-10 shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-brand/80 to-accent/80 text-xs font-bold text-white">
              {initials(c.name)}
            </span>
            <div className="min-w-0 flex-1 space-y-1.5">
              <div className="flex items-start justify-between gap-2">
                <p className="truncate font-semibold">{c.name}</p>
                <StageBadge name={c.stage.name} color={c.stage.color} className="shrink-0" />
              </div>
              {(c.whatsApp || c.instagram) && (
                <p className="flex flex-wrap items-center gap-x-2 text-xs text-muted">
                  {c.whatsApp && <WhatsAppLink number={c.whatsApp} className="text-foreground/80" />}
                  {c.instagram && <span className="truncate">@{c.instagram}</span>}
                </p>
              )}
              <TreatmentChips treatments={c.treatments} max={3} />
              <ConsultationBadge consultation={c.consultation} />
              {!!c.outstanding && (
                <p className="text-xs font-semibold text-amber-700">Owes {formatMoney(c.outstanding, locale?.currency)}</p>
              )}
              {c.nextFollowUpDate && (
                <p className="text-xs text-muted">
                  Follow-up: <FollowUp date={c.nextFollowUpDate} />
                </p>
              )}
            </div>
          </div>
        </li>
      ))}
    </ul>
  );
}

function ListSkeleton() {
  return (
    <div className="divide-y divide-line" aria-busy="true" aria-label="Loading">
      {Array.from({ length: 6 }, (_, i) => (
        <div key={i} className="flex items-center gap-3 px-4 py-4">
          <Skeleton className="size-8 rounded-full" />
          <Skeleton className="h-4 w-40" />
          <Skeleton className="ml-auto hidden h-4 w-28 md:block" />
          <Skeleton className="hidden h-6 w-24 rounded-full md:block" />
        </div>
      ))}
    </div>
  );
}
