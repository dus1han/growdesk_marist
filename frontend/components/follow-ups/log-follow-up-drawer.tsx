"use client";

import { AnimatePresence, motion } from "framer-motion";
import { CalendarPlus, Loader2, MessageCircle, PhoneMissed, PhoneOutgoing, ThumbsDown, UserX } from "lucide-react";
import { useState } from "react";
import { toast } from "sonner";
import { whatsAppUrl } from "@/components/customers/whatsapp-link";
import { Button } from "@/components/ui/button";
import { Drawer } from "@/components/ui/drawer";
import { Field, Input, Textarea } from "@/components/ui/form-controls";
import { toastError } from "@/lib/api/admin";
import { useLocale } from "@/lib/api/bookings";
import { ApiError } from "@/lib/api/client";
import { useFollowUpSettings, useLogFollowUp } from "@/lib/api/follow-ups";
import { formatDate, today as localToday } from "@/lib/dates";
import { cn } from "@/lib/utils";
import type { FollowUpOutcome } from "@/types/follow-ups";

/** The customer being followed up. */
export interface FollowUpTarget {
  id: number;
  name: string;
  whatsApp: string | null;
  attempts: number;
}

const OUTCOMES: { value: FollowUpOutcome; label: string; hint: string; icon: typeof PhoneOutgoing }[] = [
  { value: "CallBack", label: "Spoke · call back", hint: "Interested, not booked yet", icon: PhoneOutgoing },
  { value: "NoAnswer", label: "No answer", hint: "No reply or didn't pick up", icon: PhoneMissed },
  { value: "NotInterested", label: "Not interested", hint: "Marked Lost, follow-ups stop", icon: ThumbsDown },
];

/** "2026-10-13" + n days, as yyyy-MM-dd. */
function addDays(date: string, n: number) {
  const d = new Date(`${date}T12:00:00`);
  d.setDate(d.getDate() + n);
  return d.toISOString().slice(0, 10);
}

/**
 * Logs how a follow-up went: the outcome, a note, and when to follow up again (default: the
 * clinic's interval). Booked instead? "Book consultation" opens the booking form, and the
 * booking itself stops the follow-ups.
 */
export function LogFollowUpDrawer({
  target,
  onClose,
  onBook,
}: {
  target: FollowUpTarget | null;
  onClose: () => void;
  onBook?: (customerId: number) => void;
}) {
  return (
    <Drawer
      open={target !== null}
      onOpenChange={(o) => !o && onClose()}
      title="Log follow-up"
      description={target ? `${target.name} · attempt ${target.attempts + 1}` : undefined}
    >
      {target && <LogFollowUpForm key={target.id} target={target} onClose={onClose} onBook={onBook} />}
    </Drawer>
  );
}

function LogFollowUpForm({ target, onClose, onBook }: { target: FollowUpTarget; onClose: () => void; onBook?: (id: number) => void }) {
  const log = useLogFollowUp();
  const { data: settings } = useFollowUpSettings();
  const { data: locale } = useLocale();
  const today = locale?.today ?? localToday();
  const interval = settings?.intervalDays ?? 2;

  const [outcome, setOutcome] = useState<FollowUpOutcome | null>(null);
  const [next, setNext] = useState<string | null>(null);
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [askLost, setAskLost] = useState<number | null>(null);
  const nextDate = next ?? addDays(today, interval);

  const save = (chosen: FollowUpOutcome, after?: () => void) => {
    setError(null);
    log.mutate(
      { customerId: target.id, outcome: chosen, note: note.trim() || null, nextFollowUpDate: chosen === "NotInterested" ? null : nextDate },
      {
        onSuccess: (r) => {
          if (r.suggestLost) {
            setAskLost(r.attempts);
            return;
          }
          toast.success(
            chosen === "NotInterested"
              ? `${target.name} marked ${r.stage}. Follow-ups stopped.`
              : `Follow-up logged · next on ${formatDate(r.nextFollowUpDate)}`,
          );
          after?.();
          onClose();
        },
        onError: (e) => (e instanceof ApiError && e.status === 400 ? setError(e.message) : toastError(e)),
      },
    );
  };

  if (askLost !== null)
    return (
      <motion.div initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} className="space-y-5">
        <div className="rounded-2xl border border-amber-200 bg-amber-50/70 p-5">
          <span className="flex size-10 items-center justify-center rounded-xl bg-amber-100 text-amber-700">
            <UserX className="size-5" />
          </span>
          <p className="mt-3 font-display text-lg font-bold">That was attempt {askLost}</p>
          <p className="mt-1 text-sm text-amber-900/80">
            {target.name} still hasn&apos;t booked. Mark them as Lost to stop the follow-ups, or keep following up on{" "}
            {formatDate(nextDate)}.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button
            variant="danger"
            disabled={log.isPending}
            onClick={() => {
              setNote(`Marked Lost after ${askLost} follow-ups`);
              save("NotInterested");
            }}
          >
            {log.isPending ? <Loader2 className="size-4 animate-spin" /> : <UserX className="size-4" />}
            Mark as Lost
          </Button>
          <Button
            variant="secondary"
            onClick={() => {
              toast.success(`Follow-up logged · next on ${formatDate(nextDate)}`);
              onClose();
            }}
          >
            Keep following up
          </Button>
        </div>
      </motion.div>
    );

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap gap-2">
        {target.whatsApp && (
          <a
            href={whatsAppUrl(target.whatsApp)}
            target="_blank"
            rel="noopener noreferrer"
            className="inline-flex h-9 items-center gap-2 rounded-xl border border-line bg-surface px-3 text-sm font-medium transition-colors hover:border-emerald-300 hover:text-emerald-700"
          >
            <MessageCircle className="size-4 text-emerald-600" /> Open WhatsApp
          </a>
        )}
        {onBook && (
          <Button
            size="sm"
            className="h-9"
            onClick={() => {
              onClose();
              onBook(target.id);
            }}
          >
            <CalendarPlus className="size-4" /> Booked: book consultation
          </Button>
        )}
      </div>

      <fieldset>
        <legend className="mb-2 text-[13px] font-medium">How did it go?</legend>
        <div className="grid gap-2 sm:grid-cols-3" role="radiogroup" aria-label="Outcome">
          {OUTCOMES.map(({ value, label, hint, icon: Icon }) => {
            const on = outcome === value;
            return (
              <motion.button
                key={value}
                type="button"
                role="radio"
                aria-checked={on}
                whileTap={{ scale: 0.97 }}
                onClick={() => setOutcome(value)}
                className={cn(
                  "flex flex-col items-start gap-1 rounded-xl border p-3 text-left transition-colors",
                  on
                    ? value === "NotInterested"
                      ? "border-red-300 bg-red-50"
                      : "border-brand bg-brand-soft"
                    : "border-line hover:border-slate-300",
                )}
              >
                <Icon className={cn("size-4", on ? (value === "NotInterested" ? "text-danger" : "text-brand") : "text-muted")} />
                <span className="text-sm font-semibold">{label}</span>
                <span className="text-xs text-muted">{hint}</span>
              </motion.button>
            );
          })}
        </div>
      </fieldset>

      <AnimatePresence initial={false}>
        {outcome && outcome !== "NotInterested" && (
          <motion.div initial={{ opacity: 0, height: 0 }} animate={{ opacity: 1, height: "auto" }} exit={{ opacity: 0, height: 0 }} className="overflow-hidden">
            <Field label="Next follow-up" hint={`Default: in ${interval} ${interval === 1 ? "day" : "days"}.`}>
              {(p) => (
                <div className="flex flex-wrap items-center gap-2">
                  <Input {...p} type="date" min={today} value={nextDate} onChange={(e) => setNext(e.target.value || null)} className="max-w-44" />
                  {[
                    { label: "Tomorrow", days: 1 },
                    { label: `In ${interval} days`, days: interval },
                    { label: "Next week", days: 7 },
                  ]
                    .filter((c, i, all) => all.findIndex((x) => x.days === c.days) === i)
                    .map((c) => (
                      <button
                        key={c.label}
                        type="button"
                        onClick={() => setNext(addDays(today, c.days))}
                        className={cn(
                          "h-8 rounded-lg border px-2.5 text-xs font-medium transition-colors",
                          nextDate === addDays(today, c.days) ? "border-brand bg-brand-soft text-brand-strong" : "border-line text-muted hover:text-foreground",
                        )}
                      >
                        {c.label}
                      </button>
                    ))}
                </div>
              )}
            </Field>
          </motion.div>
        )}
      </AnimatePresence>

      <Field label="Note" optional>
        {(p) => <Textarea {...p} rows={3} value={note} onChange={(e) => setNote(e.target.value)} placeholder="What did they say?" maxLength={2000} />}
      </Field>

      {error && (
        <p role="alert" className="text-sm text-danger">
          {error}
        </p>
      )}

      <div className="flex justify-end gap-2 border-t border-line pt-4">
        <Button variant="secondary" onClick={onClose}>
          Cancel
        </Button>
        <Button disabled={!outcome || log.isPending} onClick={() => outcome && save(outcome)}>
          {log.isPending && <Loader2 className="size-4 animate-spin" />}
          Save follow-up
        </Button>
      </div>
    </div>
  );
}
