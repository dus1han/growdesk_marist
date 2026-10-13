"use client";

import { AnimatePresence, motion } from "framer-motion";
import { CircleAlert, Plus, Save, X } from "lucide-react";
import { useEffect, useState } from "react";
import { toast } from "sonner";
import { PageHeader } from "@/components/layout/page-header";
import { RequirePermission } from "@/components/layout/require-permission";
import { Button } from "@/components/ui/button";
import { Card, CardHeader } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { Field, Input } from "@/components/ui/form-controls";
import { Skeleton } from "@/components/ui/skeleton";
import { toastError } from "@/lib/api/admin";
import { ApiError } from "@/lib/api/client";
import { useFollowUpSettings, useSaveFollowUpSettings } from "@/lib/api/follow-ups";
import { Permission } from "@/lib/permissions";
import type { FollowUpSettings } from "@/types/follow-ups";

/** Administration → Follow-ups: how often to follow up, when to suggest Lost, and reminder times. */
export default function FollowUpSettingsPage() {
  const { data, isPending, isError, refetch } = useFollowUpSettings();
  return (
    <RequirePermission permission={Permission.SettingsManage}>
      <PageHeader
        title="Follow-ups"
        description="New enquiries are followed up the same day, then again every few days until they book. Set the rhythm here."
      />
      {isPending ? (
        <Skeleton className="h-72 rounded-2xl" />
      ) : isError || !data ? (
        <Card>
          <EmptyState
            icon={CircleAlert}
            title="Couldn't load the follow-up settings"
            description="Please try again."
            action={<Button variant="secondary" onClick={() => void refetch()}>Try again</Button>}
          />
        </Card>
      ) : (
        <SettingsForm key={JSON.stringify(data)} saved={data} />
      )}
    </RequirePermission>
  );
}

function SettingsForm({ saved }: { saved: FollowUpSettings }) {
  const save = useSaveFollowUpSettings();
  const [draft, setDraft] = useState(saved);
  const [newTime, setNewTime] = useState("");
  const [error, setError] = useState<string | null>(null);
  useEffect(() => setError(null), [draft]); // eslint-disable-line react-hooks/set-state-in-effect -- clear on edit
  const dirty = JSON.stringify(draft) !== JSON.stringify(saved);

  const addTime = () => {
    if (!newTime || draft.reminderTimes.includes(newTime)) return;
    setDraft({ ...draft, reminderTimes: [...draft.reminderTimes, newTime].sort() });
    setNewTime("");
  };

  const onSave = () =>
    save.mutate(draft, {
      onSuccess: () => toast.success("Follow-up settings saved"),
      onError: (e) => (e instanceof ApiError && e.status === 400 ? setError(e.message) : toastError(e)),
    });

  return (
    <Card className="overflow-hidden">
      <CardHeader title="Follow-up rhythm" description="Applies to everyone. Booking a consultation stops a customer's follow-ups." />
      <div className="grid gap-6 p-5 md:grid-cols-2">
        <Field label="Follow up again every" hint="After each follow-up without a booking. Staff can pick another date when logging.">
          {(p) => (
            <div className="flex items-center gap-2">
              <Input
                {...p}
                type="number"
                min={1}
                max={30}
                value={draft.intervalDays}
                onChange={(e) => setDraft({ ...draft, intervalDays: Number(e.target.value) })}
                className="w-24"
              />
              <span className="text-sm text-muted">days</span>
            </div>
          )}
        </Field>
        <Field label="Suggest marking Lost after" hint="Staff are asked whether to mark the customer Lost; nothing changes on its own.">
          {(p) => (
            <div className="flex items-center gap-2">
              <Input
                {...p}
                type="number"
                min={1}
                max={20}
                value={draft.maxAttempts}
                onChange={(e) => setDraft({ ...draft, maxAttempts: Number(e.target.value) })}
                className="w-24"
              />
              <span className="text-sm text-muted">follow-ups</span>
            </div>
          )}
        </Field>

        <div className="md:col-span-2">
          <p className="text-[13px] font-medium">Reminder times</p>
          <p className="mt-0.5 text-xs text-muted">
            While follow-ups are still due, open GrowDesk screens remind staff at these times (a desktop notification when GrowDesk is in
            the background). There is also one reminder when GrowDesk is first opened each day.
          </p>
          <div className="mt-3 flex flex-wrap items-center gap-2">
            <AnimatePresence initial={false}>
              {draft.reminderTimes.map((t) => (
                <motion.span
                  key={t}
                  layout
                  initial={{ opacity: 0, scale: 0.9 }}
                  animate={{ opacity: 1, scale: 1 }}
                  exit={{ opacity: 0, scale: 0.9 }}
                  className="inline-flex h-9 items-center gap-1.5 rounded-xl border border-line bg-surface-muted/60 pl-3 pr-1 text-sm font-semibold tabular-nums"
                >
                  {t}
                  <button
                    type="button"
                    aria-label={`Remove ${t}`}
                    onClick={() => setDraft({ ...draft, reminderTimes: draft.reminderTimes.filter((x) => x !== t) })}
                    className="flex size-7 items-center justify-center rounded-lg text-muted hover:bg-surface hover:text-danger"
                  >
                    <X className="size-3.5" />
                  </button>
                </motion.span>
              ))}
            </AnimatePresence>
            <span className="inline-flex items-center gap-1.5">
              <Input type="time" step={900} value={newTime} onChange={(e) => setNewTime(e.target.value)} className="h-9 w-32" aria-label="Add a reminder time" />
              <Button type="button" size="sm" variant="secondary" className="h-9" onClick={addTime} disabled={!newTime || draft.reminderTimes.length >= 8}>
                <Plus className="size-4" /> Add
              </Button>
            </span>
          </div>
        </div>
      </div>

      {error && (
        <p role="alert" className="px-5 pb-3 text-sm text-danger">
          {error}
        </p>
      )}
      <div className="flex items-center justify-end gap-2 border-t border-line bg-surface-muted/40 px-5 py-4">
        {dirty && (
          <Button variant="ghost" onClick={() => setDraft(saved)}>
            Discard
          </Button>
        )}
        <Button onClick={onSave} disabled={!dirty || save.isPending}>
          <Save className="size-4" /> {save.isPending ? "Saving…" : "Save settings"}
        </Button>
      </div>
    </Card>
  );
}
