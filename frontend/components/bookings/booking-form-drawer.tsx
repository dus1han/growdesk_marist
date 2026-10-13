"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { AnimatePresence, motion } from "framer-motion";
import { Search, X } from "lucide-react";
import { useState } from "react";
import { useForm, useWatch } from "react-hook-form";
import { toast } from "sonner";
import { z } from "zod";
import { StageBadge } from "@/components/customers/stage-badge";
import { Button } from "@/components/ui/button";
import { Drawer } from "@/components/ui/drawer";
import { Field, Input, RequiredMark, Select, Textarea } from "@/components/ui/form-controls";
import { Skeleton } from "@/components/ui/skeleton";
import { useDebouncedValue } from "@/hooks/use-debounced-value";
import { toastError } from "@/lib/api/admin";
import { useBookingActions, useCustomerSearch, useDoctorOptions } from "@/lib/api/bookings";
import { ApiError } from "@/lib/api/client";
import { useActiveLookup } from "@/lib/api/customers";
import { formatDate } from "@/lib/dates";
import { cn } from "@/lib/utils";
import type { BookingDetail } from "@/types/bookings";
import type { NamedRef } from "@/types/customers";
import { formatTime, hhmm } from "./booking-status";
import { DaySchedule } from "./day-schedule";

const DURATIONS = [15, 30, 45, 60, 90];

const toMinutes = (t: string) => {
  const [h, m] = t.split(":").map(Number);
  return h * 60 + m;
};
const fromMinutes = (n: number) => `${String(Math.floor(n / 60) % 24).padStart(2, "0")}:${String(n % 60).padStart(2, "0")}`;

const schema = z
  .object({
    customerId: z.number({ error: "Choose a customer." }).int().positive("Choose a customer."),
    date: z.string().min(1, "Choose a date."),
    startTime: z.string().min(1, "Choose a start time."),
    endTime: z.string().min(1, "Choose an end time."),
    doctorId: z.number().nullable(),
    treatmentIds: z.array(z.number()).min(1, "Choose at least one treatment."),
    notes: z.string().max(2000),
  })
  .refine((v) => !v.startTime || !v.endTime || toMinutes(v.endTime) > toMinutes(v.startTime), {
    path: ["endTime"],
    message: "The end time must be after the start time.",
  });
type FormValues = z.infer<typeof schema>;

export interface BookingPrefill {
  customer?: NamedRef & { treatmentIds?: number[] };
  date?: string;
  startTime?: string;
  endTime?: string;
  doctorId?: number | null;
  /** Booking a completed consultation's next treatment: that consultation's ID. */
  nextTreatmentOf?: number;
}

interface Props {
  open: boolean;
  onClose: () => void;
  prefill?: BookingPrefill;
  /** Edit an existing booked consultation (treatments, doctor, notes). */
  booking?: BookingDetail | null;
  onSaved?: (b: BookingDetail) => void;
}

export function BookingFormDrawer({ open, onClose, prefill, booking, onSaved }: Props) {
  const { create, update } = useBookingActions();
  const saving = create.isPending || update.isPending;
  return (
    <Drawer
      open={open}
      onOpenChange={(o) => !o && onClose()}
      title={booking ? "Edit booking" : "Book consultation"}
      description={booking ? "To change the date or time, use Reschedule." : undefined}
      footer={
        <>
          <Button type="button" variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="booking-form" disabled={saving}>
            {saving ? "Saving…" : booking ? "Save changes" : "Book consultation"}
          </Button>
        </>
      }
    >
      {open && <BookingForm key={booking?.id ?? "new"} prefill={prefill} booking={booking ?? null} onClose={onClose} onSaved={onSaved} />}
    </Drawer>
  );
}

function BookingForm({
  prefill,
  booking,
  onClose,
  onSaved,
}: {
  prefill?: BookingPrefill;
  booking: BookingDetail | null;
  onClose: () => void;
  onSaved?: (b: BookingDetail) => void;
}) {
  const treatments = useActiveLookup("treatments");
  const doctors = useDoctorOptions();
  const { create, update } = useBookingActions();
  const [customer, setCustomer] = useState<NamedRef | null>(booking?.customer ?? prefill?.customer ?? null);

  const start = prefill?.startTime ?? "10:00";
  const {
    register,
    handleSubmit,
    control,
    setValue,
    setError,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      customerId: booking?.customer.id ?? prefill?.customer?.id ?? 0,
      date: booking?.date ?? prefill?.date ?? "",
      startTime: booking ? hhmm(booking.startTime) : start,
      endTime: booking ? hhmm(booking.endTime) : (prefill?.endTime ?? fromMinutes(toMinutes(start) + 30)),
      doctorId: booking?.doctor?.id ?? prefill?.doctorId ?? null,
      treatmentIds: booking?.treatments.map((t) => t.id) ?? prefill?.customer?.treatmentIds ?? [],
      notes: booking?.notes ?? "",
    },
  });

  const [date, startTime, endTime, doctorId, treatmentIds] = useWatch({
    control,
    name: ["date", "startTime", "endTime", "doctorId", "treatmentIds"],
  });
  const duration = startTime && endTime ? toMinutes(endTime) - toMinutes(startTime) : 0;

  const onSubmit = handleSubmit(async (v) => {
    try {
      const saved = booking
        ? await update.mutateAsync({ id: booking.id, doctorId: v.doctorId, treatmentIds: v.treatmentIds, notes: v.notes || null })
        : await create.mutateAsync({
            customerId: v.customerId,
            doctorId: v.doctorId,
            date: v.date,
            startTime: v.startTime,
            endTime: v.endTime,
            treatmentIds: v.treatmentIds,
            notes: v.notes || null,
            nextTreatmentOf: prefill?.nextTreatmentOf ?? null,
          });
      toast.success(booking ? "Booking saved" : `Consultation booked for ${saved.customer.name}`);
      onSaved?.(saved);
      onClose();
    } catch (error) {
      if (error instanceof ApiError && error.fieldErrors.length > 0) {
        for (const fe of error.fieldErrors) if (fe.field) setError(fe.field as keyof FormValues, { message: fe.message });
        return;
      }
      toastError(error);
    }
  });

  return (
    <form id="booking-form" onSubmit={onSubmit} className="space-y-6" noValidate>
      {/* Customer */}
      <div>
        <p className="mb-1.5 text-[13px] font-medium">
          Customer
          <RequiredMark />
        </p>
        {customer ? (
          <div className="flex items-center justify-between gap-3 rounded-xl border border-line bg-surface-muted/50 px-3.5 py-2.5">
            <span className="text-sm font-semibold">{customer.name}</span>
            {!booking && !prefill?.customer && (
              <button
                type="button"
                onClick={() => {
                  setCustomer(null);
                  setValue("customerId", 0);
                }}
                aria-label="Change customer"
                className="flex size-7 items-center justify-center rounded-lg text-muted hover:bg-surface hover:text-foreground"
              >
                <X className="size-4" />
              </button>
            )}
          </div>
        ) : (
          <CustomerPicker
            error={errors.customerId?.message}
            onPick={(c) => {
              setCustomer(c);
              setValue("customerId", c.id, { shouldValidate: true });
              // Start from what they are interested in.
              if (treatmentIds.length === 0 && c.treatmentIds.length > 0) setValue("treatmentIds", c.treatmentIds);
            }}
          />
        )}
      </div>

      {/* When */}
      {booking ? (
        <div className="rounded-xl border border-line px-3.5 py-3 text-sm">
          <span className="font-semibold">{formatDate(booking.date)}</span>
          <span className="text-muted">
            {" "}
            · {formatTime(booking.startTime)} – {formatTime(booking.endTime)}
          </span>
        </div>
      ) : (
        <div className="space-y-3">
          <div className="grid gap-4 sm:grid-cols-3">
            <Field label="Date" required error={errors.date?.message} className="sm:col-span-1">
              {(p) => <Input {...p} type="date" {...register("date")} />}
            </Field>
            <Field label="Start" required error={errors.startTime?.message}>
              {(p) => (
                <Input
                  {...p}
                  type="time"
                  step={900}
                  {...register("startTime", {
                    // Keep the chosen length when the start moves.
                    onChange: (e) => {
                      const s = e.target.value as string;
                      if (s && duration > 0) setValue("endTime", fromMinutes(toMinutes(s) + duration));
                    },
                  })}
                />
              )}
            </Field>
            <Field label="End" required error={errors.endTime?.message}>
              {(p) => <Input {...p} type="time" step={900} {...register("endTime")} />}
            </Field>
          </div>
          <div className="flex flex-wrap items-center gap-1.5" role="group" aria-label="Length">
            <span className="mr-1 text-xs text-muted">Length</span>
            {DURATIONS.map((m) => (
              <button
                key={m}
                type="button"
                aria-pressed={duration === m}
                onClick={() => startTime && setValue("endTime", fromMinutes(toMinutes(startTime) + m), { shouldValidate: true })}
                className={cn(
                  "rounded-lg border px-2.5 py-1 text-xs font-medium transition-colors",
                  duration === m ? "border-brand bg-brand-soft text-brand-strong" : "border-line text-muted hover:text-foreground",
                )}
              >
                {m < 60 ? `${m}m` : `${Math.floor(m / 60)}h${m % 60 ? ` ${m % 60}m` : ""}`}
              </button>
            ))}
          </div>
          <DaySchedule date={date} startTime={startTime} endTime={endTime} doctorId={doctorId} />
        </div>
      )}

      {doctors.data && doctors.data.length > 0 && (
        <Field label="Doctor" optional error={errors.doctorId?.message}>
          {(p) => (
            <Select
              {...p}
              {...register("doctorId", { setValueAs: (v) => (v === "" || v === null ? null : Number(v)) })}
            >
              <option value="">Any / not assigned</option>
              {doctors.data.map((d) => (
                <option key={d.id} value={d.id}>
                  {d.name}
                </option>
              ))}
            </Select>
          )}
        </Field>
      )}

      <fieldset>
        <legend className="mb-2 text-[13px] font-medium">
          Treatments
          <RequiredMark />
        </legend>
        <div className="flex flex-wrap gap-2" aria-busy={treatments.isPending}>
          {treatments.isPending && [72, 96, 84, 110].map((w) => <Skeleton key={w} className="h-7 rounded-lg" style={{ width: w }} />)}
          {treatments.data?.map((t) => {
            const on = treatmentIds.includes(t.id);
            return (
              <button
                key={t.id}
                type="button"
                aria-pressed={on}
                onClick={() =>
                  setValue("treatmentIds", on ? treatmentIds.filter((id) => id !== t.id) : [...treatmentIds, t.id], { shouldValidate: true })
                }
                className={cn(
                  "rounded-lg border px-3 py-1.5 text-xs font-medium transition-all",
                  on ? "border-brand bg-brand-soft text-brand-strong" : "border-line text-foreground/70 hover:border-slate-300",
                )}
              >
                {on && "✓ "}
                {t.name}
              </button>
            );
          })}
        </div>
        {errors.treatmentIds?.message && <p className="mt-1.5 text-xs text-danger">{errors.treatmentIds.message}</p>}
      </fieldset>

      <Field label="Notes" optional error={errors.notes?.message}>
        {(p) => <Textarea {...p} rows={3} {...register("notes")} />}
      </Field>
    </form>
  );
}

/** Customer autocomplete: type two letters or digits, pick from the matches. */
function CustomerPicker({ onPick, error }: { onPick: (c: NamedRef & { treatmentIds: number[] }) => void; error?: string }) {
  const [term, setTerm] = useState("");
  const debounced = useDebouncedValue(term, 250);
  const { data, isFetching } = useCustomerSearch(debounced);
  const results = debounced.trim().length >= 2 ? (data?.items ?? []) : [];

  return (
    <div>
      <div className="relative">
        <Search className="pointer-events-none absolute left-3.5 top-1/2 size-4 -translate-y-1/2 text-muted" />
        <Input
          value={term}
          onChange={(e) => setTerm(e.target.value)}
          placeholder="Search by name, WhatsApp or Instagram…"
          aria-label="Search customers"
          aria-invalid={!!error}
          autoFocus
          className="pl-10"
        />
        {isFetching && <span className="absolute right-3.5 top-1/2 size-2 -translate-y-1/2 animate-pulse rounded-full bg-brand" />}
      </div>
      {error && <p className="mt-1.5 text-xs text-danger">{error}</p>}
      <AnimatePresence>
        {results.length > 0 && (
          <motion.ul
            initial={{ opacity: 0, y: -4 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0 }}
            className="mt-2 overflow-hidden rounded-xl border border-line"
            role="listbox"
          >
            {results.map((c) => (
              <li key={c.id} role="option" aria-selected={false}>
                <button
                  type="button"
                  onClick={() => onPick({ id: c.id, name: c.name, treatmentIds: c.treatments.map((t) => t.id) })}
                  className="flex w-full items-center justify-between gap-3 border-b border-line px-3.5 py-2.5 text-left last:border-0 hover:bg-brand-soft/50"
                >
                  <span className="min-w-0">
                    <span className="block truncate text-sm font-semibold">{c.name}</span>
                    <span className="block truncate text-xs text-muted">
                      {[c.whatsApp, c.instagram && `@${c.instagram}`].filter(Boolean).join(" · ")}
                    </span>
                  </span>
                  <StageBadge name={c.stage.name} color={c.stage.color} className="shrink-0" />
                </button>
              </li>
            ))}
          </motion.ul>
        )}
      </AnimatePresence>
      {debounced.trim().length >= 2 && !isFetching && results.length === 0 && (
        <p className="mt-2 text-xs text-muted">No customers match. Add them from the Customers page first.</p>
      )}
    </div>
  );
}
