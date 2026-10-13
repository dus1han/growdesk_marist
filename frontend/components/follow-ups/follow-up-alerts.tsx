"use client";

import { useRouter } from "next/navigation";
import { useEffect, useRef } from "react";
import { toast } from "sonner";
import { useFollowUpSummary } from "@/lib/api/follow-ups";
import { playChime, showDesktopAlert } from "@/lib/live-alerts";
import type { FollowUpSummary } from "@/types/follow-ups";

const DAILY_KEY = "growdesk.followups.daily";
const REMINDED_KEY = "growdesk.followups.reminded";
const SEEN_KEY = "growdesk.followups.seen";

function read(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function write(key: string, value: string) {
  try {
    localStorage.setItem(key, value);
  } catch {
    /* storage blocked: may repeat a reminder, which beats missing one */
  }
}

const nowHhMm = () => new Date().toTimeString().slice(0, 5);

/**
 * Follow-up reminders on every open GrowDesk screen (renders nothing):
 * - once a day when GrowDesk is opened: how many follow-ups are due and overdue;
 * - at the clinic's reminder times (Administration → Follow-ups), while any are still due;
 * - straight away when a new enquiry arrives, to contact them the same day.
 * In the background they are desktop notifications (bottom right on Windows; allow "Desktop
 * alerts" in the user menu); in front, a message on screen. Checked every minute.
 */
export function FollowUpAlerts() {
  const router = useRouter();
  const { data } = useFollowUpSummary();
  const started = useRef(false);

  useEffect(() => {
    if (!data) return;
    const open = () => router.push("/follow-ups");

    // ---- New enquiries -----------------------------------------------------------------------
    const seen = new Set((read(SEEN_KEY) ?? "").split(",").filter(Boolean).map(Number));
    const fresh = data.newEnquiries.filter((e) => !seen.has(e.id));
    // On first load, what's already there is covered by the daily summary: just remember it.
    if (started.current && fresh.length > 0) {
      playChime();
      for (const e of fresh.slice(0, 3)) {
        const detail = [e.treatment, e.leadSource].filter(Boolean).join(" · ");
        notify(`New enquiry: ${e.name}`, `${detail ? `${detail}. ` : ""}Follow up today.`, () => router.push(`/customers/${e.id}`));
      }
      if (fresh.length > 3) notify(`${fresh.length} new enquiries`, "Follow them up today.", open);
    }
    write(SEEN_KEY, [...new Set([...data.newEnquiries.map((e) => e.id), ...seen])].slice(0, 200).join(","));

    // ---- Once a day ----------------------------------------------------------------------------
    if (read(DAILY_KEY) !== data.today) {
      write(DAILY_KEY, data.today);
      if (data.due > 0) notify(...dueMessage(data), open);
    }

    // ---- At the reminder times -----------------------------------------------------------------
    const time = nowHhMm();
    const passed = data.reminderTimes.filter((t) => t <= time);
    const reminded = new Set((read(REMINDED_KEY) ?? "").split(",").filter((k) => k.startsWith(data.today)));
    const latest = passed.at(-1);
    if (latest && !reminded.has(`${data.today}|${latest}`)) {
      // Opened late in the day: the daily summary just covered it; only remind for times still to come.
      if (started.current && data.due > 0) notify(...dueMessage(data), open);
      for (const t of passed) reminded.add(`${data.today}|${t}`);
      write(REMINDED_KEY, [...reminded].join(","));
    }

    started.current = true;
  }, [data, router]);

  return null;
}

function dueMessage(s: FollowUpSummary): [string, string] {
  const title = `${s.due} follow-up${s.due === 1 ? "" : "s"} due today`;
  const parts = [s.overdue > 0 && `${s.overdue} overdue`, s.new > 0 && `${s.new} new enquir${s.new === 1 ? "y" : "ies"}`].filter(Boolean);
  return [title, parts.length > 0 ? `${parts.join(", ")}. Open GrowDesk to follow up.` : "Open GrowDesk to follow up."];
}

/** A desktop notification while GrowDesk is in the background; a message on screen otherwise. */
function notify(title: string, body: string, onOpen: () => void) {
  if (showDesktopAlert(title, body, onOpen)) return;
  toast.info(title, { description: body, duration: 12_000, action: { label: "Open", onClick: onOpen } });
}
