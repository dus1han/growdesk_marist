"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { AnimatePresence, motion } from "framer-motion";
import { ChevronDown, TriangleAlert } from "lucide-react";
import Link from "next/link";
import { useEffect, useState } from "react";
import { Controller, useForm, useWatch, type Control } from "react-hook-form";
import { toast } from "sonner";
import { z } from "zod";
import { Button } from "@/components/ui/button";
import { Drawer } from "@/components/ui/drawer";
import { Field, Input, RequiredMark, Select, Switch, Textarea } from "@/components/ui/form-controls";
import { Skeleton } from "@/components/ui/skeleton";
import { toastError } from "@/lib/api/admin";
import { ApiError } from "@/lib/api/client";
import { useActiveCustomFields, useActiveLookup, useIsSavingCustomer, useSaveCustomer, useUserOptions } from "@/lib/api/customers";
import { cn } from "@/lib/utils";
import type { CustomField } from "@/types/admin";
import type { CustomerDetail, CustomFieldValue, DuplicateCustomer } from "@/types/customers";

const optionalId = z.number().int().positive().nullable();

const schema = z
  .object({
    name: z.string().trim().min(1, "Please enter the customer's name.").max(150),
    whatsApp: z.string().trim().max(30),
    instagram: z.string().trim().max(100),
    secondaryPhone: z.string().trim().max(30),
    email: z.union([z.literal(""), z.string().trim().email("Please enter a valid email address.")]),
    stageId: optionalId,
    leadSourceId: optionalId,
    assignedUserId: optionalId,
    treatmentIds: z.array(z.number()),
    lastContactDate: z.string(),
    nextFollowUpDate: z.string(),
    notes: z.string().max(4000),
    customFields: z.record(z.string(), z.any()),
  })
  .refine((v) => v.whatsApp !== "" || v.instagram !== "", {
    path: ["whatsApp"],
    message: "Add a WhatsApp number or an Instagram name.",
  });
type FormValues = z.infer<typeof schema>;

/** Every customer saved from this form needs an interested treatment and a lead source (backend). */
const formSchema = schema
  .refine((v) => v.treatmentIds.length > 0, {
    path: ["treatmentIds"],
    message: "Choose at least one interested treatment.",
  })
  .refine((v) => v.leadSourceId !== null, {
    path: ["leadSourceId"],
    message: "Please choose a lead source.",
  });

/** Turns "" from a <select> into null and anything else into a number. */
const idOrNull = (v: unknown) => (v === "" || v === null || v === undefined ? null : Number(v));

interface CustomerFormDrawerProps {
  open: boolean;
  onClose: () => void;
  /** Omit to add a new customer. */
  customer?: CustomerDetail | null;
  onSaved?: (customer: CustomerDetail) => void;
}

export function CustomerFormDrawer({ open, onClose, customer, onSaved }: CustomerFormDrawerProps) {
  const saving = useIsSavingCustomer();
  return (
    <Drawer
      open={open}
      onOpenChange={(o) => !o && onClose()}
      title={customer ? "Edit customer" : "Add customer"}
      description={customer ? undefined : "A WhatsApp number is enough to get started. Instagram is under More details."}
      footer={
        <>
          <Button type="button" variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="customer-form" disabled={saving}>
            {saving ? "Saving…" : customer ? "Save changes" : "Add customer"}
          </Button>
        </>
      }
    >
      {open && <CustomerForm key={customer?.id ?? "new"} customer={customer ?? null} onClose={onClose} onSaved={onSaved} />}
    </Drawer>
  );
}

function CustomerForm({
  customer,
  onClose,
  onSaved,
}: {
  customer: CustomerDetail | null;
  onClose: () => void;
  onSaved?: (c: CustomerDetail) => void;
}) {
  const stages = useActiveLookup("stages");
  const treatments = useActiveLookup("treatments");
  const sources = useActiveLookup("lead-sources");
  const users = useUserOptions();
  const fields = useActiveCustomFields();
  const save = useSaveCustomer();
  const [duplicate, setDuplicate] = useState<DuplicateCustomer | null>(null);
  // Instagram lives under More details: open it when editing an Instagram-only customer.
  const [showMore, setShowMore] = useState(!!customer?.instagram && !customer?.whatsApp);

  const {
    register,
    handleSubmit,
    control,
    setError,
    setValue,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(formSchema),
    defaultValues: {
      name: customer?.name ?? "",
      whatsApp: customer?.whatsApp ?? "",
      instagram: customer?.instagram ? `@${customer.instagram}` : "",
      secondaryPhone: customer?.secondaryPhone ?? "",
      email: customer?.email ?? "",
      stageId: customer?.stage.id ?? null,
      leadSourceId: customer?.leadSource?.id ?? null,
      assignedUserId: customer?.assignedUser?.id ?? null,
      treatmentIds: customer?.treatments.map((t) => t.id) ?? [],
      lastContactDate: customer?.lastContactDate ?? "",
      nextFollowUpDate: customer?.nextFollowUpDate ?? "",
      notes: customer?.notes ?? "",
      customFields: Object.fromEntries(customer?.customFields.map((f) => [f.key, f.value]) ?? []),
    },
  });

  const stageId = useWatch({ control, name: "stageId" });
  const instagramError = errors.instagram?.message;
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- reveal the field that needs fixing
    if (instagramError) setShowMore(true);
  }, [instagramError]);
  const treatmentIds = useWatch({ control, name: "treatmentIds" });

  const requiredFields = fields.data?.filter((f) => f.isRequired) ?? [];
  const optionalFields = fields.data?.filter((f) => !f.isRequired) ?? [];

  const onSubmit = handleSubmit(async (v) => {
    setDuplicate(null);

    // Required custom fields are checked here too, so the error lands next to the field at once.
    for (const f of requiredFields) {
      const value = v.customFields[f.key];
      if (value === null || value === undefined || value === "" || (Array.isArray(value) && value.length === 0)) {
        setError(`customFields.${f.key}`, { message: `${f.label} is required.` });
        return;
      }
    }

    try {
      const saved = await save.mutateAsync({
        id: customer?.id,
        name: v.name,
        whatsApp: v.whatsApp || null,
        instagram: v.instagram || null,
        secondaryPhone: v.secondaryPhone || null,
        email: v.email || null,
        stageId: v.stageId,
        leadSourceId: v.leadSourceId,
        assignedUserId: v.assignedUserId,
        treatmentIds: v.treatmentIds,
        lastContactDate: v.lastContactDate || null,
        nextFollowUpDate: v.nextFollowUpDate || null,
        notes: v.notes || null,
        customFields: v.customFields as Record<string, CustomFieldValue>,
      });
      toast.success(customer ? `${saved.name} saved` : `${saved.name} added`);
      onSaved?.(saved);
      onClose();
    } catch (error) {
      if (error instanceof ApiError) {
        if (error.status === 409 && error.data) setDuplicate(error.data as DuplicateCustomer);
        for (const fe of error.fieldErrors) {
          if (!fe.field) continue;
          if (["secondaryPhone", "email", "assignedUserId"].includes(fe.field) || fe.field.startsWith("customFields."))
            setShowMore(true);
          setError(fe.field as keyof FormValues, { message: fe.message });
        }
        if (error.status === 409 || error.fieldErrors.length > 0) return;
      }
      toastError(error);
    }
  });

  const loading = stages.isPending || treatments.isPending || fields.isPending;
  if (loading) {
    return (
      <div className="space-y-5" aria-busy="true" aria-label="Loading">
        {Array.from({ length: 5 }, (_, i) => (
          <Skeleton key={i} className="h-10 w-full rounded-xl" />
        ))}
      </div>
    );
  }

  return (
    <form id="customer-form" onSubmit={onSubmit} className="space-y-6" noValidate>
      <AnimatePresence>
        {duplicate && (
          <motion.div
            initial={{ opacity: 0, height: 0 }}
            animate={{ opacity: 1, height: "auto" }}
            exit={{ opacity: 0, height: 0 }}
            className="overflow-hidden"
            role="alert"
          >
            <div className="flex gap-3 rounded-xl border border-amber-200 bg-amber-50 p-3.5 text-sm text-amber-900">
              <TriangleAlert className="mt-0.5 size-4 shrink-0" />
              <div>
                <p>
                  <span className="font-semibold">{duplicate.existingCustomerName}</span> already has this{" "}
                  {duplicate.matchedOn === "whatsApp" ? "WhatsApp number" : "Instagram name"}.
                </p>
                <Link href={`/customers/${duplicate.existingCustomerId}`} onClick={onClose} className="mt-1 inline-block font-semibold underline underline-offset-2">
                  Open their profile
                </Link>
              </div>
            </div>
          </motion.div>
        )}
      </AnimatePresence>

      <section className="space-y-4">
        <Field label="Name" required error={errors.name?.message}>
          {(p) => <Input {...p} autoFocus autoComplete="off" {...register("name")} />}
        </Field>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="WhatsApp" required hint="With its country code (e.g. +94…); UAE numbers work without it. For an Instagram-only lead, add the Instagram name under More details instead." error={errors.whatsApp?.message}>
            {(p) => <Input {...p} type="tel" inputMode="tel" placeholder="+971 50 123 4567" autoComplete="off" {...register("whatsApp")} />}
          </Field>
          <Field label="Lead source" required error={errors.leadSourceId?.message}>
            {(p) => (
              <Select {...p} {...register("leadSourceId", { setValueAs: idOrNull })}>
                <option value="">Choose…</option>
                {sources.data?.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.name}
                  </option>
                ))}
                {customer?.leadSource && !sources.data?.some((s) => s.id === customer.leadSource!.id) && (
                  <option value={customer.leadSource.id}>{customer.leadSource.name}</option>
                )}
              </Select>
            )}
          </Field>
        </div>
      </section>

      <fieldset>
        <legend className="mb-2 text-[13px] font-medium">Status</legend>
        {!customer && <p className="-mt-1 mb-2 text-xs text-muted">Leave empty to start as {stages.data?.find((s) => s.systemKey === "interested")?.name ?? "Interested"}.</p>}
        <div className="flex flex-wrap gap-2" role="radiogroup" aria-label="Status">
          {stages.data?.map((s) => {
            const selected = stageId === s.id;
            const color = s.color ?? "#64748B";
            return (
              <button
                key={s.id}
                type="button"
                role="radio"
                aria-checked={selected}
                onClick={() => setValue("stageId", selected && !customer ? null : s.id, { shouldDirty: true })}
                className={cn(
                  "inline-flex items-center gap-1.5 rounded-full border px-3 py-1.5 text-xs font-semibold transition-all",
                  selected ? "border-transparent" : "border-line text-muted hover:text-foreground",
                )}
                style={selected ? { backgroundColor: `${color}1F`, color, boxShadow: `inset 0 0 0 1.5px ${color}` } : undefined}
              >
                <span className="size-1.5 rounded-full" style={{ backgroundColor: color }} />
                {s.name}
              </button>
            );
          })}
        </div>
      </fieldset>

      <fieldset>
        <legend className="mb-2 text-[13px] font-medium">
          Interested treatments
          <RequiredMark />
        </legend>
        {errors.treatmentIds?.message && <p className="-mt-1 mb-2 text-xs text-danger">{errors.treatmentIds.message}</p>}
        <div className="flex flex-wrap gap-2">
          {treatments.data?.map((t) => {
            const selected = treatmentIds.includes(t.id);
            return (
              <button
                key={t.id}
                type="button"
                aria-pressed={selected}
                onClick={() =>
                  setValue("treatmentIds", selected ? treatmentIds.filter((id) => id !== t.id) : [...treatmentIds, t.id], { shouldDirty: true, shouldValidate: !!errors.treatmentIds })
                }
                className={cn(
                  "rounded-lg border px-3 py-1.5 text-xs font-medium transition-all",
                  selected ? "border-brand bg-brand-soft text-brand-strong" : "border-line text-foreground/70 hover:border-slate-300",
                )}
              >
                {t.name}
              </button>
            );
          })}
          {/* Keep interests in treatments that were since deactivated. */}
          {customer?.treatments
            .filter((t) => !treatments.data?.some((a) => a.id === t.id) && treatmentIds.includes(t.id))
            .map((t) => (
              <button
                key={t.id}
                type="button"
                aria-pressed
                title="No longer offered"
                onClick={() => setValue("treatmentIds", treatmentIds.filter((id) => id !== t.id), { shouldDirty: true })}
                className="rounded-lg border border-brand bg-brand-soft px-3 py-1.5 text-xs font-medium text-brand-strong opacity-70"
              >
                {t.name}
              </button>
            ))}
        </div>
      </fieldset>

      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="Next follow-up" optional error={errors.nextFollowUpDate?.message}>
          {(p) => <Input {...p} type="date" {...register("nextFollowUpDate")} />}
        </Field>
      </div>

      {requiredFields.map((f) => (
        <CustomFieldInput key={f.id} field={f} control={control} error={errors.customFields?.[f.key]?.message as string | undefined} />
      ))}

      <Field label="Notes" optional error={errors.notes?.message}>
        {(p) => <Textarea {...p} rows={3} {...register("notes")} />}
      </Field>

      {/* Progressive disclosure: the rest of the details, on request. */}
      <div className="rounded-xl border border-line">
        <button
          type="button"
          onClick={() => setShowMore((s) => !s)}
          aria-expanded={showMore}
          className="flex w-full items-center justify-between px-4 py-3 text-sm font-medium"
        >
          More details
          <motion.span animate={{ rotate: showMore ? 180 : 0 }}>
            <ChevronDown className="size-4 text-muted" />
          </motion.span>
        </button>
        <AnimatePresence initial={false}>
          {showMore && (
            <motion.div
              initial={{ height: 0, opacity: 0 }}
              animate={{ height: "auto", opacity: 1 }}
              exit={{ height: 0, opacity: 0 }}
              transition={{ duration: 0.2 }}
              className="overflow-hidden"
            >
              <div className="space-y-4 border-t border-line p-4">
                <div className="grid gap-4 sm:grid-cols-2">
                  <Field label="Secondary number" optional error={errors.secondaryPhone?.message}>
                    {(p) => <Input {...p} type="tel" inputMode="tel" autoComplete="off" {...register("secondaryPhone")} />}
                  </Field>
                  <Field label="Email" optional error={errors.email?.message}>
                    {(p) => <Input {...p} type="email" autoComplete="off" {...register("email")} />}
                  </Field>
                  <Field label="Instagram" optional error={errors.instagram?.message}>
                    {(p) => <Input {...p} placeholder="@username" autoCapitalize="none" spellCheck={false} autoComplete="off" {...register("instagram")} />}
                  </Field>
                  <Field label="Assigned to" optional error={errors.assignedUserId?.message}>
                    {(p) => (
                      <Select {...p} {...register("assignedUserId", { setValueAs: idOrNull })}>
                        <option value="">Nobody</option>
                        {users.data?.map((u) => (
                          <option key={u.id} value={u.id}>
                            {u.name}
                          </option>
                        ))}
                        {customer?.assignedUser && !users.data?.some((u) => u.id === customer.assignedUser!.id) && (
                          <option value={customer.assignedUser.id}>{customer.assignedUser.name}</option>
                        )}
                      </Select>
                    )}
                  </Field>
                  <Field label="Last contact" optional error={errors.lastContactDate?.message}>
                    {(p) => <Input {...p} type="date" {...register("lastContactDate")} />}
                  </Field>
                </div>
                {optionalFields.map((f) => (
                  <CustomFieldInput key={f.id} field={f} control={control} error={errors.customFields?.[f.key]?.message as string | undefined} />
                ))}
              </div>
            </motion.div>
          )}
        </AnimatePresence>
      </div>
    </form>
  );
}

/** One input per custom field type. Values match the API: see types/customers.ts CustomFieldValue. */
function CustomFieldInput({ field, control, error }: { field: CustomField; control: Control<FormValues>; error?: string }) {
  return (
    <Controller
      control={control}
      name={`customFields.${field.key}`}
      render={({ field: { value, onChange } }) => {
        const v = value as CustomFieldValue | undefined;
        switch (field.fieldType) {
          case "Boolean":
            return (
              <div className="flex items-center justify-between gap-4 rounded-xl border border-line px-4 py-3">
                <span className="text-sm font-medium">
                  {field.label}
                  {field.isRequired && <RequiredMark />}
                </span>
                <Switch checked={v === true} onCheckedChange={onChange} label={field.label} />
              </div>
            );
          case "MultiSelect": {
            const ids = Array.isArray(v) ? v : [];
            return (
              <fieldset>
                <legend className="mb-2 text-[13px] font-medium">
                  {field.label}
                  {field.isRequired && <RequiredMark />}
                </legend>
                <div className="flex flex-wrap gap-2">
                  {field.options.map((o) => {
                    const on = ids.includes(o.id);
                    return (
                      <button
                        key={o.id}
                        type="button"
                        aria-pressed={on}
                        onClick={() => onChange(on ? ids.filter((i) => i !== o.id) : [...ids, o.id])}
                        className={cn(
                          "rounded-lg border px-3 py-1.5 text-xs font-medium transition-all",
                          on ? "border-brand bg-brand-soft text-brand-strong" : "border-line text-foreground/70 hover:border-slate-300",
                        )}
                      >
                        {o.label}
                      </button>
                    );
                  })}
                </div>
                {error && <p className="mt-1.5 text-xs text-danger">{error}</p>}
              </fieldset>
            );
          }
          default:
            return (
              <Field label={field.label} optional={!field.isRequired} required={field.isRequired} error={error}>
                {(p) =>
                  field.fieldType === "Dropdown" ? (
                    <Select {...p} value={typeof v === "number" ? v : ""} onChange={(e) => onChange(e.target.value === "" ? null : Number(e.target.value))}>
                      <option value="">Not set</option>
                      {field.options.map((o) => (
                        <option key={o.id} value={o.id}>
                          {o.label}
                        </option>
                      ))}
                    </Select>
                  ) : field.fieldType === "Textarea" ? (
                    <Textarea {...p} value={typeof v === "string" ? v : ""} onChange={(e) => onChange(e.target.value)} />
                  ) : field.fieldType === "Number" ? (
                    <Input
                      {...p}
                      type="number"
                      inputMode="decimal"
                      value={typeof v === "number" || typeof v === "string" ? v : ""}
                      onChange={(e) => onChange(e.target.value === "" ? null : Number(e.target.value))}
                    />
                  ) : (
                    <Input
                      {...p}
                      type={field.fieldType === "Date" ? "date" : field.fieldType === "Email" ? "email" : field.fieldType === "Phone" ? "tel" : "text"}
                      value={typeof v === "string" ? v : ""}
                      onChange={(e) => onChange(e.target.value)}
                    />
                  )
                }
              </Field>
            );
        }
      }}
    />
  );
}
