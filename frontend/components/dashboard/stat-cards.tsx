"use client";

import { motion } from "framer-motion";
import { Bell, CalendarClock, CalendarDays, UserRoundSearch, type LucideIcon } from "lucide-react";
import Link from "next/link";
import { AnimatedNumber } from "@/components/ui/animated-number";
import { Card } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/utils";
import type { Dashboard } from "@/types/dashboard";

interface Stat {
  label: string;
  icon: LucideIcon;
  tint: string;
  iconColor: string;
  href: string;
  value: number;
  note: string;
  noteTone?: string;
}

/** Whole numbers, also mid-animation (5 → 6 must never show "5.978"). */
const count = (n: number) => Math.round(n).toLocaleString();

function changeNote(today: number, yesterday: number): { note: string; tone?: string } {
  const diff = today - yesterday;
  if (diff === 0) return { note: "Same as yesterday" };
  return diff > 0
    ? { note: `+${diff} from yesterday`, tone: "text-emerald-600" }
    : { note: `${diff} from yesterday`, tone: "text-muted" };
}

function buildStats(d: Dashboard): Stat[] {
  const stats: Stat[] = [];
  if (d.bookings) {
    const change = changeNote(d.bookings.today, d.bookings.yesterday);
    stats.push(
      {
        label: "Today's Consultations",
        icon: CalendarDays,
        tint: "from-brand/15",
        iconColor: "text-brand",
        href: "/bookings?view=list&tab=today",
        value: d.bookings.today,
        note: change.note,
        noteTone: change.tone,
      },
      {
        label: "Upcoming",
        icon: CalendarClock,
        tint: "from-sky-500/15",
        iconColor: "text-sky-600",
        href: "/bookings?view=list&tab=upcoming",
        value: d.bookings.upcoming,
        note: `${d.bookings.upcomingThisWeek} in the next 7 days`,
      },
    );
  }
  if (d.customers) {
    stats.push(
      {
        label: "Follow-ups",
        icon: Bell,
        tint: "from-amber-500/15",
        iconColor: "text-amber-600",
        href: "/customers?followup=due",
        value: d.customers.followUpsDue,
        note: d.customers.followUpsOverdue > 0 ? `${d.customers.followUpsOverdue} overdue` : d.customers.followUpsDue > 0 ? "Due today" : "All caught up",
        noteTone: d.customers.followUpsOverdue > 0 ? "text-red-600" : undefined,
      },
      {
        label: "Potential Customers",
        icon: UserRoundSearch,
        tint: "from-accent/15",
        iconColor: "text-teal-600",
        href: "/customers",
        value: d.customers.potential,
        note: d.customers.newToday > 0 ? `+${d.customers.newToday} new today` : "Interested or following up",
        noteTone: d.customers.newToday > 0 ? "text-emerald-600" : undefined,
      },
    );
  }
  return stats;
}

/** The four headline numbers (spec §10). Each card links to the list behind it. */
export function StatCards({ data }: { data: Dashboard | undefined }) {
  if (!data) {
    return (
      <div className="grid grid-cols-2 gap-3 sm:gap-4 xl:grid-cols-4" aria-busy="true" aria-label="Loading">
        {Array.from({ length: 4 }, (_, i) => (
          <Skeleton key={i} className="h-[124px] rounded-2xl sm:h-[138px]" />
        ))}
      </div>
    );
  }

  const stats = buildStats(data);
  if (stats.length === 0) return null;

  return (
    <div className={cn("grid grid-cols-2 gap-3 sm:gap-4 [&>*]:min-w-0", stats.length === 4 && "xl:grid-cols-4")}>
      {stats.map((s, i) => (
        <motion.div
          key={s.label}
          initial={{ opacity: 0, y: 14 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.4, delay: 0.05 + i * 0.06, ease: [0.22, 1, 0.36, 1] }}
          whileHover={{ y: -3 }}
          whileTap={{ scale: 0.98 }}
        >
          <Link href={s.href} className="block h-full rounded-2xl focus-visible:outline-offset-2">
            <Card className="relative h-full overflow-hidden p-4 transition-shadow hover:shadow-pop sm:p-5">
              <div className={cn("absolute inset-0 bg-gradient-to-br to-transparent", s.tint)} aria-hidden />
              <div className="relative flex items-start justify-between gap-2">
                <p className="text-[13px] font-medium leading-snug text-muted sm:text-sm">{s.label}</p>
                <span className={cn("hidden size-9 shrink-0 items-center justify-center rounded-xl bg-surface shadow-card sm:flex", s.iconColor)}>
                  <s.icon className="size-[18px]" />
                </span>
              </div>
              <p className="relative mt-3 font-display text-3xl font-bold tracking-tight sm:mt-4">
                <AnimatedNumber value={s.value} format={count} />
              </p>
              <p className={cn("relative mt-1 truncate text-xs text-muted", s.noteTone)}>{s.note}</p>
            </Card>
          </Link>
        </motion.div>
      ))}
    </div>
  );
}
