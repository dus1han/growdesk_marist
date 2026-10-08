"use client";

import { AnimatePresence, motion } from "framer-motion";
import { CircleAlert, RotateCcw } from "lucide-react";
import { useState } from "react";
import { toast } from "sonner";
import { CaptureConnections } from "@/components/admin/capture-connections";
import { CaptureToolbarPreview } from "@/components/admin/capture-toolbar-preview";
import { fieldTypeMeta } from "@/components/admin/field-types";
import { SortableList } from "@/components/admin/sortable-list";
import { PageHeader } from "@/components/layout/page-header";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { Badge, Switch } from "@/components/ui/form-controls";
import { Skeleton } from "@/components/ui/skeleton";
import { toastError, useCaptureFields, useSaveCaptureFields } from "@/lib/api/admin";
import { cn } from "@/lib/utils";
import type { CaptureField } from "@/types/admin";

/** Not chosen in the toolbar: GrowDesk sets it from the site (WhatsApp or Instagram). */
const AUTOMATIC_FIELD = "lead_source";

export default function CaptureToolPage() {
  const { data: saved, isPending, isError, refetch } = useCaptureFields();
  const save = useSaveCaptureFields();
  // Local draft; null means "no edits yet, show what the server has".
  const [draft, setDraft] = useState<CaptureField[] | null>(null);
  const fields = draft ?? saved ?? [];
  const dirty = draft !== null && JSON.stringify(draft) !== JSON.stringify(saved);

  const patch = (key: string, change: Partial<CaptureField>) =>
    setDraft(
      fields.map((f) => {
        if (f.key !== key) return f;
        const next = { ...f, ...change };
        if (!next.isEnabled) next.isRequired = false; // a hidden field can't be required
        return next;
      }),
    );

  const onSave = () =>
    save.mutate(
      fields.map(({ key, isEnabled, isRequired }) => ({ key, isEnabled, isRequired })),
      {
        onSuccess: () => {
          setDraft(null);
          toast.success("Capture tool fields saved");
        },
        onError: toastError,
      },
    );

  return (
    <>
      <PageHeader
        title="Capture Tool"
        description="Choose which fields the GrowDesk Capture toolbar collects on WhatsApp Web and Instagram, and their order. Required fields must be captured before STOP can save."
      />

      {isPending ? (
        <Card className="space-y-3 p-4" aria-busy="true" aria-label="Loading">
          {Array.from({ length: 6 }, (_, i) => (
            <Skeleton key={i} className="h-12 w-full rounded-xl" />
          ))}
        </Card>
      ) : isError ? (
        <Card>
          <EmptyState
            icon={CircleAlert}
            title="Couldn't load the capture fields"
            description="Check your connection and try again."
            action={
              <Button variant="secondary" onClick={() => refetch()}>
                <RotateCcw className="size-4" /> Try again
              </Button>
            }
          />
        </Card>
      ) : (
        <>
          <Card className="overflow-hidden">
            <div className="flex items-center gap-2 border-b border-line bg-surface-muted/50 px-4 py-2.5 text-[11px] font-semibold uppercase tracking-[0.06em] text-muted">
              <span className="flex-1 pl-9">Field</span>
              <span className="w-12 text-center">Show</span>
              <span className="w-16 text-center">Required</span>
            </div>
            <SortableList
              items={fields}
              getId={(f) => f.key}
              onReorder={(next) => setDraft(next)}
              className="divide-y divide-line"
              renderItem={(f, handle) => {
                const meta = fieldTypeMeta(f.type);
                // GrowDesk Capture sets the lead source from the site it is used on (1.0.13+).
                if (f.key === AUTOMATIC_FIELD)
                  return (
                    <div className="flex items-center gap-2 bg-surface px-2 py-2.5 sm:px-3">
                      {handle}
                      <span className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-brand-soft text-brand">
                        <meta.icon className="size-4" />
                      </span>
                      <div className="flex min-w-0 flex-1 flex-wrap items-center gap-x-2 gap-y-1 px-1">
                        <span className="truncate text-sm font-medium">{f.label}</span>
                        <Badge tone="brand">Automatic</Badge>
                        <span className="text-xs text-muted">WhatsApp or Instagram, from the site the toolbar is used on</span>
                      </div>
                    </div>
                  );
                return (
                  <div className={cn("flex items-center gap-2 bg-surface px-2 py-2.5 sm:px-3", !f.isEnabled && "bg-surface-muted/40")}>
                    {handle}
                    <span className={cn("flex size-8 shrink-0 items-center justify-center rounded-lg", f.isEnabled ? "bg-brand-soft text-brand" : "bg-surface-muted text-muted")}>
                      <meta.icon className="size-4" />
                    </span>
                    <div className="flex min-w-0 flex-1 flex-wrap items-center gap-x-2 gap-y-1 px-1">
                      <span className={cn("truncate text-sm font-medium", !f.isEnabled && "text-muted")}>{f.label}</span>
                      {f.isCustom && <Badge>Custom</Badge>}
                    </div>
                    <span className="flex w-12 justify-center">
                      <Switch size="sm" checked={f.isEnabled} onCheckedChange={(v) => patch(f.key, { isEnabled: v })} label={`Show ${f.label}`} />
                    </span>
                    <span className="flex w-16 justify-center">
                      <Switch
                        size="sm"
                        checked={f.isRequired}
                        disabled={!f.isEnabled}
                        onCheckedChange={(v) => patch(f.key, { isRequired: v })}
                        label={`Require ${f.label}`}
                      />
                    </span>
                  </div>
                );
              }}
            />
          </Card>

          <CaptureToolbarPreview fields={fields.filter((f) => f.isEnabled && f.key !== AUTOMATIC_FIELD)} />
          <CaptureConnections />
        </>
      )}

      {/* Save bar */}
      <AnimatePresence>
        {dirty && (
          <motion.div
            initial={{ y: 80, opacity: 0 }}
            animate={{ y: 0, opacity: 1 }}
            exit={{ y: 80, opacity: 0 }}
            transition={{ type: "spring", stiffness: 420, damping: 36 }}
            className="fixed inset-x-4 bottom-20 z-40 mx-auto flex max-w-xl items-center gap-3 rounded-2xl border border-line bg-surface/95 p-3 pl-5 shadow-pop backdrop-blur-xl lg:bottom-6"
            role="status"
          >
            <p className="flex-1 text-sm font-medium">You have unsaved changes</p>
            <Button variant="ghost" size="sm" onClick={() => setDraft(null)} disabled={save.isPending}>
              Discard
            </Button>
            <Button size="sm" onClick={onSave} disabled={save.isPending}>
              {save.isPending ? "Saving…" : "Save changes"}
            </Button>
          </motion.div>
        )}
      </AnimatePresence>
    </>
  );
}
