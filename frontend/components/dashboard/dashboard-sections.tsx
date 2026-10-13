"use client";

import { motion } from "framer-motion";
import { ArrowRight, Bell, CalendarDays, ListChecks, Plus, Sparkles } from "lucide-react";
import Link from "next/link";
import { describeActivity } from "@/components/activity/describe-activity";
import { BookingStatusBadge, formatTime } from "@/components/bookings/booking-status";
import { StageBadge } from "@/components/customers/stage-badge";
import { Button } from "@/components/ui/button";
import { Card, CardHeader } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { Skeleton } from "@/components/ui/skeleton";
import { followUpState, formatDate } from "@/lib/dates";
import { formatDateTime, formatRelative } from "@/lib/format";
import { cn } from "@/lib/utils";
import type { DashboardActivity, DashboardAppointment, DashboardFollowUp, DashboardStage } from "@/types/dashboard";

const ease = [0.22, 1, 0.36, 1] as const;

function ViewAll({ href, label = "View all" }: { href: string; label?: string }) {
  return (
    <Link href={href} className="group flex shrink-0 items-center gap-1 text-xs font-semibold text-brand hover:text-brand-strong">
      {label}
      <ArrowRight className="size-3.5 transition-transform duration-200 group-hover:translate-x-0.5" />
    </Link>
  );
}

function RowsSkeleton({ rows = 4 }: { rows?: number }) {
  return (
    <div className="divide-y divide-line" aria-busy="true" aria-label="Loading">
      {Array.from({ length: rows }, (_, i) => (
        <div key={i} className="flex items-center gap-4 px-5 py-4">
          <Skeleton className="h-4 w-14" />
          <div className="flex-1 space-y-2">
            <Skeleton className="h-4 w-40" />
            <Skeleton className="h-3 w-28" />
          </div>
          <Skeleton className="h-6 w-20 rounded-full" />
        </div>
      ))}
    </div>
  );
}

// ---- Today's appointments -------------------------------------------------------------------

export function TodaysAppointments({
  items,
  loading,
  onOpen,
  onBook,
}: {
  items: DashboardAppointment[] | undefined;
  loading: boolean;
  onOpen: (id: number) => void;
  onBook?: () => void;
}) {
  const remaining = items?.filter((a) => a.status === "Booked").length ?? 0;
  return (
    <Card className="flex flex-col">
      <CardHeader
        title="Today's Appointments"
        description={items && items.length > 0 ? `${items.length} consultation${items.length === 1 ? "" : "s"} · ${remaining} still to see` : "Consultations booked for today"}
        action={items && items.length > 0 ? <ViewAll href="/bookings?view=list&tab=today" /> : undefined}
      />
      {loading ? (
        <RowsSkeleton />
      ) : !items || items.length === 0 ? (
        <EmptyState
          icon={CalendarDays}
          title="No consultations today"
          description="Your calendar is clear. Booked consultations will appear here with their time and treatments."
          action={
            onBook && (
              <Button variant="secondary" size="sm" onClick={onBook}>
                <Plus className="size-4" /> Book consultation
              </Button>
            )
          }
        />
      ) : (
        <ul className="max-h-[420px] divide-y divide-line overflow-y-auto">
          {items.map((a, i) => (
            <motion.li key={a.id} initial={{ opacity: 0, y: 6 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.25, delay: Math.min(i, 8) * 0.04, ease }}>
              <button
                type="button"
                onClick={() => onOpen(a.id)}
                className={cn(
                  "flex w-full items-center gap-4 px-5 py-3.5 text-left transition-colors hover:bg-brand-soft/40",
                  a.status !== "Booked" && "opacity-70",
                )}
              >
                <div className="w-16 shrink-0">
                  <p className="font-display text-sm font-bold tabular-nums">{formatTime(a.startTime)}</p>
                  <p className="text-[11px] text-muted tabular-nums">{formatTime(a.endTime)}</p>
                </div>
                <div className="min-w-0 flex-1">
                  <p className="truncate font-semibold">{a.customer.name}</p>
                  <p className="truncate text-xs text-muted">
                    {a.treatments.join(" + ")}
                    {a.doctorName && ` · ${a.doctorName}`}
                  </p>
                </div>
                <BookingStatusBadge status={a.status} className="shrink-0" />
              </button>
            </motion.li>
          ))}
        </ul>
      )}
    </Card>
  );
}

// ---- Stage summary --------------------------------------------------------------------------

export function StageSummary({ stages, loading }: { stages: DashboardStage[] | undefined; loading: boolean }) {
  const total = stages?.reduce((n, s) => n + s.count, 0) ?? 0;
  const max = Math.max(1, ...(stages?.map((s) => s.count) ?? [0]));
  return (
    <Card>
      <CardHeader title="Potential Customers" description={total > 0 ? `${total.toLocaleString()} customers by status` : "Customers by status"} />
      {loading ? (
        <div className="space-y-4 p-5" aria-busy="true" aria-label="Loading">
          {Array.from({ length: 5 }, (_, i) => (
            <Skeleton key={i} className="h-8 w-full" />
          ))}
        </div>
      ) : !stages || total === 0 ? (
        <EmptyState icon={ListChecks} title="No customers yet" description="Once customers are captured, you'll see how many have each status." />
      ) : (
        <ul className="space-y-1 p-3">
          {stages.map((s, i) => (
            <li key={s.id}>
              <Link
                href={`/customers?stage=${s.id}`}
                className="group block rounded-xl px-2.5 py-2 transition-colors hover:bg-surface-muted"
                aria-label={`${s.name}: ${s.count} customers`}
              >
                <div className="flex items-center justify-between gap-3 text-sm">
                  <span className="flex min-w-0 items-center gap-2">
                    <span className="size-2 shrink-0 rounded-full" style={{ backgroundColor: s.color }} />
                    <span className="truncate font-medium">{s.name}</span>
                  </span>
                  <span className="flex items-center gap-1.5 font-display font-bold tabular-nums">
                    {s.count.toLocaleString()}
                    <ArrowRight className="size-3.5 -translate-x-1 text-muted opacity-0 transition-all duration-200 group-hover:translate-x-0 group-hover:opacity-100" />
                  </span>
                </div>
                <div className="mt-1.5 h-1.5 overflow-hidden rounded-full bg-surface-muted">
                  <motion.div
                    className="h-full rounded-full"
                    style={{ backgroundColor: s.color }}
                    initial={{ width: 0 }}
                    animate={{ width: `${s.count === 0 ? 0 : Math.max(4, (s.count / max) * 100)}%` }}
                    transition={{ duration: 0.7, delay: 0.1 + i * 0.05, ease }}
                  />
                </div>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}

// ---- Follow-ups -----------------------------------------------------------------------------

const FOLLOW_UP_BADGE = {
  overdue: "bg-red-50 text-red-700",
  today: "bg-amber-50 text-amber-700",
  upcoming: "bg-surface-muted text-muted",
} as const;

export function FollowUps({ items, loading }: { items: DashboardFollowUp[] | undefined; loading: boolean }) {
  return (
    <Card>
      <CardHeader
        title="Follow-ups"
        description="Overdue, due today and the next 7 days"
        action={<ViewAll href="/follow-ups" />}
      />
      {loading ? (
        <RowsSkeleton rows={3} />
      ) : !items || items.length === 0 ? (
        <EmptyState icon={Bell} title="Nothing to follow up" description="Customers with a follow-up date in the next 7 days will show here." />
      ) : (
        <ul className="divide-y divide-line">
          {items.map((f, i) => {
            const state = followUpState(f.date) ?? "upcoming";
            return (
              <motion.li key={f.id} initial={{ opacity: 0, y: 6 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.25, delay: Math.min(i, 8) * 0.04, ease }}>
                <Link href={`/customers/${f.id}`} className="flex items-center gap-4 px-5 py-3.5 transition-colors hover:bg-brand-soft/40">
                  <div className="min-w-0 flex-1">
                    <p className="truncate font-semibold">{f.name}</p>
                    <p className="truncate text-xs text-muted">
                      {f.treatments.length > 0 ? f.treatments.join(", ") : "No treatments recorded"}
                      {f.whatsApp && ` · ${f.whatsApp}`}
                    </p>
                  </div>
                  <StageBadge name={f.stage.name} color={f.stage.color} className="hidden shrink-0 sm:inline-flex" />
                  <span className={cn("shrink-0 rounded-full px-2.5 py-1 text-xs font-semibold", FOLLOW_UP_BADGE[state])}>
                    {state === "overdue" ? `Overdue · ${formatDate(f.date)}` : state === "today" ? "Today" : formatDate(f.date)}
                  </span>
                </Link>
              </motion.li>
            );
          })}
        </ul>
      )}
    </Card>
  );
}

// ---- Recent activity ------------------------------------------------------------------------

export function RecentActivity({
  items,
  loading,
  onOpenBooking,
}: {
  items: DashboardActivity[] | undefined;
  loading: boolean;
  onOpenBooking: (id: number) => void;
}) {
  return (
    <Card>
      <CardHeader title="Recent Activity" />
      <div className="p-5">
        {loading ? (
          <div className="space-y-4" aria-busy="true" aria-label="Loading">
            {Array.from({ length: 4 }, (_, i) => (
              <div key={i} className="flex gap-3">
                <Skeleton className="size-8 rounded-full" />
                <div className="flex-1 space-y-2">
                  <Skeleton className="h-3.5 w-3/4" />
                  <Skeleton className="h-3 w-1/3" />
                </div>
              </div>
            ))}
          </div>
        ) : !items || items.length === 0 ? (
          <EmptyState icon={Sparkles} title="No activity yet" description="New customers, bookings and completions will appear here." className="py-6" />
        ) : (
          <ol className="relative space-y-5 before:absolute before:bottom-2 before:left-4 before:top-2 before:w-px before:bg-line">
            {items.map((a, i) => {
              const { icon: Icon, title, detail, tone } = describeActivity(a);
              const body = (
                <>
                  <span className={cn("relative z-10 flex size-8 shrink-0 items-center justify-center rounded-full ring-4 ring-surface", tone)}>
                    <Icon className="size-4" />
                  </span>
                  <span className="min-w-0 pt-0.5">
                    <span className="block text-sm">
                      {a.customer && <span className="font-semibold">{a.customer.name}</span>}
                      {a.customer && <span className="text-muted"> · </span>}
                      <span className={a.customer ? "text-foreground/80" : "font-medium"}>{title}</span>
                    </span>
                    {detail && <span className="block text-xs text-muted">{detail}</span>}
                    <span className="mt-0.5 block text-xs text-muted" title={formatDateTime(a.createdAt)}>
                      {formatRelative(a.createdAt)}
                      {a.userName && ` · ${a.userName}`}
                    </span>
                  </span>
                </>
              );
              const rowClass = "relative -mx-2 flex w-[calc(100%+1rem)] gap-3 rounded-xl px-2 py-1 text-left transition-colors hover:bg-surface-muted";
              return (
                <motion.li
                  key={a.id}
                  initial={{ opacity: 0, x: -8 }}
                  animate={{ opacity: 1, x: 0 }}
                  transition={{ duration: 0.25, delay: Math.min(i, 10) * 0.05 }}
                >
                  {a.bookingId ? (
                    <button type="button" onClick={() => onOpenBooking(a.bookingId!)} className={rowClass}>
                      {body}
                    </button>
                  ) : a.customer ? (
                    <Link href={`/customers/${a.customer.id}`} className={rowClass}>
                      {body}
                    </Link>
                  ) : (
                    <div className="relative flex gap-3">{body}</div>
                  )}
                </motion.li>
              );
            })}
          </ol>
        )}
      </div>
    </Card>
  );
}
