import { CalendarCheck2, CalendarClock, CalendarX, CalendarX2, CircleDashed, History } from "lucide-react";
import { formatTime } from "@/components/bookings/booking-status";
import { cn } from "@/lib/utils";
import type { Consultation, ConsultationState } from "@/types/customers";

/** "5 Oct": the year only when it isn't this year. */
function shortDate(iso: string) {
  const [y, m, d] = iso.split("-").map(Number);
  return new Date(y, m - 1, d).toLocaleDateString(undefined, {
    day: "numeric",
    month: "short",
    ...(y !== new Date().getFullYear() && { year: "numeric" }),
  });
}

export const CONSULTATION: Record<ConsultationState, { label: string; icon: typeof CalendarClock; className: string }> = {
  booked: { label: "Booked", icon: CalendarClock, className: "bg-sky-50 text-sky-700" },
  rescheduled: { label: "Rescheduled", icon: History, className: "bg-violet-50 text-violet-700" },
  consulted: { label: "Consulted", icon: CalendarCheck2, className: "bg-emerald-50 text-emerald-700" },
  missed: { label: "Missed", icon: CalendarX2, className: "bg-amber-50 text-amber-800" },
  cancelled: { label: "Cancelled", icon: CalendarX, className: "bg-red-50 text-red-700" },
  none: { label: "Not booked", icon: CircleDashed, className: "bg-surface-muted text-muted" },
};

/**
 * Where the customer is with consultations, worked out from their bookings by the API (never set
 * by hand): booked or rescheduled (when), consulted (when, and the next treatment), missed (a
 * no-show), cancelled, or not booked.
 */
export function ConsultationBadge({ consultation, className }: { consultation: Consultation; className?: string }) {
  const meta = CONSULTATION[consultation.state];
  const Icon = meta.icon;
  const when =
    consultation.date &&
    ((consultation.state === "booked" || consultation.state === "rescheduled") && consultation.startTime
      ? `${shortDate(consultation.date)}, ${formatTime(consultation.startTime)}`
      : shortDate(consultation.date));

  return (
    <span className={cn("inline-flex max-w-full flex-wrap items-center gap-x-1.5 gap-y-0.5", className)}>
      <span className={cn("inline-flex items-center gap-1 whitespace-nowrap rounded-full px-2 py-0.5 text-xs font-semibold", meta.className)}>
        <Icon className="size-3.5 shrink-0" />
        {meta.label}
        {when && <span className="font-medium opacity-80">· {when}</span>}
      </span>
      {consultation.state === "consulted" && consultation.nextTreatmentDate && (
        <span className="whitespace-nowrap text-xs text-muted">Next treatment {shortDate(consultation.nextTreatmentDate)}</span>
      )}
    </span>
  );
}
