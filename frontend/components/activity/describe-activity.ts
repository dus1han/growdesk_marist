import {
  CalendarPlus,
  Check,
  CircleAlert,
  Layers,
  Sparkles,
  UserPlus,
  UserRoundPen,
  Wallet,
  type LucideIcon,
} from "lucide-react";
import { formatMoney } from "@/components/bookings/booking-status";
import { formatDate } from "@/lib/dates";
import type { Activity } from "@/types/customers";

export interface ActivityView {
  icon: LucideIcon;
  title: string;
  detail?: string;
  tone: string;
}

const FIELD_NAMES: Record<string, string> = {
  Name: "name",
  WhatsAppNumber: "WhatsApp",
  SecondaryPhone: "secondary number",
  InstagramName: "Instagram",
  Email: "email",
  LeadSourceId: "lead source",
  AssignedUserId: "assignee",
  LastContactDate: "last contact",
  NextFollowUpDate: "follow-up date",
  Notes: "notes",
};

/** How an audit entry reads in a timeline: icon, title, optional detail and a colour. */
export function describeActivity(a: Pick<Activity, "action" | "details">): ActivityView {
  const view = describeAction(a);
  const d = a.details ?? {};
  if (d.source !== "capture") return view;
  const via = `Captured${typeof d.client === "string" ? ` on ${d.client}` : ""}`;
  return { ...view, detail: view.detail ? `${view.detail} · ${via}` : via };
}

function describeAction(a: Pick<Activity, "action" | "details">): ActivityView {
  const d = a.details ?? {};
  switch (a.action) {
    case "Customer Created":
      return { icon: UserPlus, title: "Customer added", tone: "bg-emerald-50 text-emerald-600" };
    case "Stage Changed":
      return { icon: Layers, title: `Status: ${String(d.to ?? "")}`, detail: d.from ? `was ${String(d.from)}` : undefined, tone: "bg-brand-soft text-brand" };
    case "Treatment Added": {
      const list = Array.isArray(d.treatments) ? (d.treatments as string[]).join(", ") : "";
      return { icon: Sparkles, title: `Interested in ${list}`, tone: "bg-fuchsia-50 text-fuchsia-600" };
    }
    case "Booking Created":
      return { icon: CalendarPlus, title: "Consultation booked", detail: d.date ? `for ${formatDate(String(d.date))}` : undefined, tone: "bg-brand-soft text-brand" };
    case "Consultation Completed": {
      // Since part payments: charge / paid / balance. Older entries carry a payment state instead.
      const balance = typeof d.balance === "number" ? d.balance : null;
      const detail =
        balance !== null
          ? balance > 0
            ? `Balance ${formatMoney(balance)}`
            : typeof d.charge === "number" && d.charge > 0
              ? "Paid in full"
              : undefined
          : d.payment
            ? `Payment ${String(d.payment).toLowerCase()}`
            : undefined;
      return { icon: Check, title: "Consultation completed", detail, tone: "bg-emerald-50 text-emerald-600" };
    }
    case "Booking Rescheduled": {
      const to = d.to as { date?: string } | undefined;
      return { icon: CalendarPlus, title: "Appointment rescheduled", detail: to?.date ? `to ${formatDate(to.date)}` : undefined, tone: "bg-amber-50 text-amber-600" };
    }
    case "Booking Cancelled":
      return { icon: CircleAlert, title: "Booking cancelled", detail: d.reason ? String(d.reason) : undefined, tone: "bg-slate-100 text-slate-500" };
    case "No Show":
      return { icon: CircleAlert, title: "Did not attend", tone: "bg-red-50 text-red-600" };
    case "Payment Recorded": {
      const amount = typeof d.amount === "number" ? d.amount : null;
      return {
        icon: Wallet,
        title: d.settledPending ? "Pending payment received" : "Payment recorded",
        detail:
          amount !== null
            ? typeof d.balance === "number" && d.balance > 0
              ? `${formatMoney(amount)} · ${formatMoney(d.balance)} still owed`
              : formatMoney(amount)
            : undefined,
        tone: "bg-emerald-50 text-emerald-600",
      };
    }
    case "Balance Waived":
      return {
        icon: Wallet,
        title: "Balance waived",
        detail: typeof d.amount === "number" ? formatMoney(d.amount) : undefined,
        tone: "bg-slate-100 text-slate-500",
      };
    case "Booking Updated":
      return { icon: UserRoundPen, title: "Booking updated", tone: "bg-sky-50 text-sky-600" };
    case "Customer Updated": {
      const fields = Array.isArray(d.fields)
        ? (d.fields as string[]).filter((f) => f !== "StageId").map((f) => FIELD_NAMES[f] ?? f)
        : [];
      return { icon: UserRoundPen, title: "Details updated", detail: fields.length ? `Changed ${fields.join(", ")}` : undefined, tone: "bg-sky-50 text-sky-600" };
    }
    default:
      return { icon: UserRoundPen, title: a.action, tone: "bg-surface-muted text-muted" };
  }
}
