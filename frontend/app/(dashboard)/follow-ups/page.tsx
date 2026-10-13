"use client";

import { AnimatePresence, motion } from "framer-motion";
import { AlarmClock, BellRing, CalendarClock, CalendarPlus, CircleAlert, MessageCircle, PartyPopper, PhoneCall, RotateCcw, Sparkles } from "lucide-react";
import Link from "next/link";
import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { BookingFormDrawer, type BookingPrefill } from "@/components/bookings/booking-form-drawer";
import { StageBadge } from "@/components/customers/stage-badge";
import { whatsAppUrl } from "@/components/customers/whatsapp-link";
import { LogFollowUpDrawer, type FollowUpTarget } from "@/components/follow-ups/log-follow-up-drawer";
import { PageHeader } from "@/components/layout/page-header";
import { RequirePermission } from "@/components/layout/require-permission";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { Badge } from "@/components/ui/form-controls";
import { Skeleton } from "@/components/ui/skeleton";
import { followUpKeys, useFollowUpQueue } from "@/lib/api/follow-ups";
import { useSession } from "@/lib/auth/session";
import { formatDate } from "@/lib/dates";
import { formatRelative } from "@/lib/format";
import { can, Permission } from "@/lib/permissions";
import { cn } from "@/lib/utils";
import type { FollowUpItem } from "@/types/follow-ups";

const SCOPE_KEY = "growdesk.followups.scope";

type Group = { key: string; title: string; hint: string; icon: typeof BellRing; tone: string; items: FollowUpItem[] };

/**
 * The day's follow-ups: overdue first, then new enquiries (contact them the same day), then the
 * rest due today. Logging a follow-up schedules the next one; booking stops them.
 */
export default function FollowUpsPage() {
  const { data: session } = useSession();
  const canLog = can(session?.user, Permission.CustomersManage);
  const canBook = can(session?.user, Permission.BookingsManage);
  const queryClient = useQueryClient();
  const [scope, setScope] = useState<"mine" | "all">("mine");
  // The remembered choice, after mount (localStorage isn't available on the server).
  useEffect(() => {
    try {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- one-time sync from localStorage
      if (localStorage.getItem(SCOPE_KEY) === "all") setScope("all");
    } catch {
      /* default: mine */
    }
  }, []);
  const { data, isPending, isError, isFetching, refetch } = useFollowUpQueue(scope);
  const [logging, setLogging] = useState<FollowUpTarget | null>(null);
  const [booking, setBooking] = useState<BookingPrefill | null>(null);

  const changeScope = (next: "mine" | "all") => {
    setScope(next);
    try {
      localStorage.setItem(SCOPE_KEY, next);
    } catch {
      /* not remembered */
    }
  };

  const items = data?.items ?? [];
  const nextTreatments = data?.nextTreatments ?? [];
  const today = data?.today ?? "";
  const groups: Group[] = [
    {
      key: "overdue",
      title: "Overdue",
      hint: "Should have been followed up already",
      icon: AlarmClock,
      tone: "text-danger",
      items: items.filter((i) => i.due < today),
    },
    {
      key: "new",
      title: "New enquiries",
      hint: "Contact them today",
      icon: Sparkles,
      tone: "text-brand",
      items: items.filter((i) => i.due >= today && i.isNew),
    },
    {
      key: "today",
      title: "Due today",
      hint: "Haven't booked yet",
      icon: BellRing,
      tone: "text-amber-600",
      items: items.filter((i) => i.due >= today && !i.isNew),
    },
  ];

  return (
    <RequirePermission permission={Permission.CustomersView}>
      <PageHeader
        title="Follow-ups"
        description="Contact every new enquiry the same day, and keep following up until they book."
        actions={
          <div className="grid grid-cols-2 gap-1 rounded-xl bg-surface-muted p-1" role="tablist" aria-label="Whose follow-ups">
            {(["mine", "all"] as const).map((s) => (
              <button
                key={s}
                type="button"
                role="tab"
                aria-selected={scope === s}
                onClick={() => changeScope(s)}
                className={cn("relative rounded-lg px-3 py-1.5 text-sm font-medium transition-colors", scope === s ? "text-foreground" : "text-muted hover:text-foreground")}
                title={s === "mine" ? "Customers assigned to you, and unassigned ones" : "Everyone's customers"}
              >
                {scope === s && (
                  <motion.span layoutId="followup-scope" className="absolute inset-0 rounded-lg bg-surface shadow-card" transition={{ type: "spring", stiffness: 500, damping: 38 }} />
                )}
                <span className="relative">{s === "mine" ? "Mine" : "Everyone"}</span>
              </button>
            ))}
          </div>
        }
      />

      {isPending ? (
        <div className="space-y-6" aria-busy="true" aria-label="Loading">
          {Array.from({ length: 2 }, (_, g) => (
            <Card key={g} className="p-5">
              <Skeleton className="h-5 w-40" />
              <div className="mt-4 space-y-3">
                {Array.from({ length: 3 }, (_, i) => (
                  <Skeleton key={i} className="h-12 w-full rounded-xl" />
                ))}
              </div>
            </Card>
          ))}
        </div>
      ) : isError ? (
        <Card>
          <EmptyState
            icon={CircleAlert}
            title="Couldn't load follow-ups"
            description="Check your connection and try again."
            action={
              <Button variant="secondary" onClick={() => void refetch()}>
                <RotateCcw className="size-4" /> Try again
              </Button>
            }
          />
        </Card>
      ) : items.length === 0 && nextTreatments.length === 0 ? (
        <Card>
          <EmptyState
            icon={PartyPopper}
            title="All caught up"
            description={scope === "mine" ? "No follow-ups due for you today. Switch to Everyone to see the whole team's." : "No follow-ups due today."}
            className="py-16"
          />
        </Card>
      ) : (
        <div className={cn("space-y-6 transition-opacity", isFetching && "opacity-80")}>
          {groups
            .filter((g) => g.items.length > 0)
            .map((g) => (
              <motion.section key={g.key} initial={{ opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.3 }}>
                <Card className="overflow-hidden">
                  <div className="flex items-center gap-3 border-b border-line px-5 py-4">
                    <g.icon className={cn("size-5", g.tone)} />
                    <div className="min-w-0 flex-1">
                      <h2 className="font-display text-[15px] font-semibold">
                        {g.title} <span className="text-muted">· {g.items.length}</span>
                      </h2>
                      <p className="text-xs text-muted">{g.hint}</p>
                    </div>
                  </div>
                  <ul className="divide-y divide-line">
                    <AnimatePresence initial={false}>
                      {g.items.map((item) => (
                        <FollowUpRow
                          key={item.id}
                          item={item}
                          today={today}
                          maxAttempts={data.maxAttempts}
                          onLog={canLog ? () => setLogging({ id: item.id, name: item.name, whatsApp: item.whatsApp, attempts: item.attempts }) : undefined}
                          onBook={canBook ? () => setBooking({ customer: { id: item.id, name: item.name } }) : undefined}
                        />
                      ))}
                    </AnimatePresence>
                  </ul>
                </Card>
              </motion.section>
            ))}

          {nextTreatments.length > 0 && (
            <motion.section initial={{ opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.3 }}>
              <Card className="overflow-hidden">
                <div className="flex items-center gap-3 border-b border-line px-5 py-4">
                  <CalendarClock className="size-5 text-emerald-600" />
                  <div className="min-w-0 flex-1">
                    <h2 className="font-display text-[15px] font-semibold">
                      Next treatments to book <span className="text-muted">· {nextTreatments.length}</span>
                    </h2>
                    <p className="text-xs text-muted">Set when a consultation was completed, but not booked yet. Agree a time and book it.</p>
                  </div>
                </div>
                <ul className="divide-y divide-line">
                  {nextTreatments.map((n) => (
                    <li key={n.bookingId} className="flex flex-wrap items-center gap-x-4 gap-y-2 px-5 py-3.5 transition-colors hover:bg-emerald-50/40">
                      <Link href={`/customers/${n.customerId}`} className="min-w-0 flex-1 basis-56">
                        <p className="truncate font-semibold hover:text-brand-strong">{n.customerName}</p>
                        <p className="mt-0.5 truncate text-xs text-muted">
                          {n.treatment?.name ?? "Treatment"}
                          {n.doctor && ` · ${n.doctor.name}`}
                        </p>
                      </Link>
                      <p className={cn("w-40 text-xs", n.date < today ? "font-semibold text-danger" : "text-muted")}>
                        {n.date < today ? `Was due ${formatDate(n.date)}` : `Due ${formatDate(n.date)}`}
                      </p>
                      <div className="flex items-center gap-1.5">
                        {n.whatsApp && (
                          <a
                            href={whatsAppUrl(n.whatsApp)}
                            target="_blank"
                            rel="noopener noreferrer"
                            aria-label={`WhatsApp ${n.customerName}`}
                            title={n.whatsApp}
                            className="flex size-9 items-center justify-center rounded-xl border border-line text-emerald-600 transition-colors hover:border-emerald-300 hover:bg-emerald-50"
                          >
                            <MessageCircle className="size-4" />
                          </a>
                        )}
                        {canBook && (
                          <Button
                            size="sm"
                            className="h-9"
                            onClick={() =>
                              setBooking({
                                customer: { id: n.customerId, name: n.customerName, treatmentIds: n.treatment ? [n.treatment.id] : [] },
                                date: n.date >= today ? n.date : undefined,
                                doctorId: n.doctor?.id ?? null,
                                nextTreatmentOf: n.bookingId,
                              })
                            }
                          >
                            <CalendarPlus className="size-4" /> Book
                          </Button>
                        )}
                      </div>
                    </li>
                  ))}
                </ul>
              </Card>
            </motion.section>
          )}
        </div>
      )}

      <LogFollowUpDrawer
        target={logging}
        onClose={() => setLogging(null)}
        onBook={
          canBook
            ? (id) => {
                const item = items.find((i) => i.id === id);
                if (item) setBooking({ customer: { id: item.id, name: item.name } });
              }
            : undefined
        }
      />
      {canBook && (
        <BookingFormDrawer
          open={booking !== null}
          onClose={() => setBooking(null)}
          prefill={booking ?? undefined}
          onSaved={() => {
            setBooking(null);
            void queryClient.invalidateQueries({ queryKey: followUpKeys.all });
          }}
        />
      )}
    </RequirePermission>
  );
}

function FollowUpRow({
  item,
  today,
  maxAttempts,
  onLog,
  onBook,
}: {
  item: FollowUpItem;
  today: string;
  maxAttempts: number;
  onLog?: () => void;
  onBook?: () => void;
}) {
  const overdue = item.due < today;
  return (
    <motion.li
      layout
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0, height: 0 }}
      className="flex flex-wrap items-center gap-x-4 gap-y-2 px-5 py-3.5 transition-colors hover:bg-brand-soft/30"
    >
      <Link href={`/customers/${item.id}`} className="min-w-0 flex-1 basis-56">
        <p className="flex flex-wrap items-center gap-2">
          <span className="truncate font-semibold hover:text-brand-strong">{item.name}</span>
          <StageBadge name={item.stage.name} color={item.stage.color} />
          {item.isNew && <Badge tone="brand">New</Badge>}
        </p>
        <p className="mt-0.5 truncate text-xs text-muted">
          {item.treatments.length > 0 ? item.treatments.join(", ") : "No treatment yet"}
          {item.leadSource && ` · ${item.leadSource}`}
          {item.assignedUser && ` · ${item.assignedUser.name}`}
        </p>
      </Link>

      <div className="w-40 text-xs text-muted">
        {item.isNew ? (
          <p>Enquired {formatRelative(item.createdAt)}</p>
        ) : (
          <p>
            Attempt {item.attempts + 1}
            <span className={cn(item.attempts + 1 >= maxAttempts && "font-semibold text-amber-700")}> of {maxAttempts}</span>
          </p>
        )}
        <p className={cn(overdue && "font-semibold text-danger")}>
          {overdue ? `Due ${formatDate(item.due)}` : item.lastContactDate ? `Last contact ${formatDate(item.lastContactDate)}` : "Due today"}
        </p>
      </div>

      <div className="flex items-center gap-1.5">
        {item.whatsApp && (
          <a
            href={whatsAppUrl(item.whatsApp)}
            target="_blank"
            rel="noopener noreferrer"
            aria-label={`WhatsApp ${item.name}`}
            title={item.whatsApp}
            className="flex size-9 items-center justify-center rounded-xl border border-line text-emerald-600 transition-colors hover:border-emerald-300 hover:bg-emerald-50"
          >
            <MessageCircle className="size-4" />
          </a>
        )}
        {onBook && (
          <Button size="sm" variant="secondary" className="h-9" onClick={onBook}>
            <CalendarPlus className="size-4" />
            <span className="hidden sm:inline">Book</span>
          </Button>
        )}
        {onLog && (
          <Button size="sm" className="h-9" onClick={onLog}>
            <PhoneCall className="size-4" /> Log follow-up
          </Button>
        )}
      </div>
    </motion.li>
  );
}
