"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { AnimatePresence, motion } from "framer-motion";
import { ArrowRight, CalendarClock, Check, CircleCheck, Pencil, Stethoscope, StickyNote, UserX, XCircle } from "lucide-react";
import Link from "next/link";
import { useState } from "react";
import { useForm, useWatch, type UseFormRegisterReturn } from "react-hook-form";
import { toast } from "sonner";
import { z } from "zod";
import { StageBadge } from "@/components/customers/stage-badge";
import { Button } from "@/components/ui/button";
import { ConfirmDelete } from "@/components/ui/confirm-delete";
import { Drawer } from "@/components/ui/drawer";
import { Field, Input, RequiredMark, Select, Textarea } from "@/components/ui/form-controls";
import { Skeleton } from "@/components/ui/skeleton";
import { toastError } from "@/lib/api/admin";
import { useBooking, useBookingActions, useDoctorOptions, useLocale } from "@/lib/api/bookings";
import { ApiError } from "@/lib/api/client";
import { useActiveLookup } from "@/lib/api/customers";
import { useWaiveBalance } from "@/lib/api/payments";
import { useDeleteRecord } from "@/lib/api/recycle-bin";
import { useSession } from "@/lib/auth/session";
import { formatDate, today } from "@/lib/dates";
import { formatDateTime } from "@/lib/format";
import { can, Permission } from "@/lib/permissions";
import { cn } from "@/lib/utils";
import type { BookingDetail } from "@/types/bookings";
import { RecordPaymentDrawer } from "@/components/payments/record-payment-drawer";
import { BookingFormDrawer } from "./booking-form-drawer";
import { BookingStatusBadge, formatMoney, formatTime, hhmm } from "./booking-status";
import { DaySchedule } from "./day-schedule";

type View = "details" | "complete" | "reschedule" | "cancel" | "done";

interface Props {
  bookingId: number | null;
  onClose: () => void;
  /** Called when an action produces a different booking to show (reschedule). */
  onBookingChange?: (id: number) => void;
}

export function BookingDetailsDrawer({ bookingId, onClose, onBookingChange }: Props) {
  const [view, setView] = useState<View>("details");
  const [editing, setEditing] = useState(false);
  const { data: booking, isPending } = useBooking(bookingId);

  const close = () => {
    setView("details");
    onClose();
  };

  const titles: Record<View, string> = {
    details: "Consultation",
    complete: "Complete consultation",
    reschedule: "Reschedule",
    cancel: "Cancel booking",
    done: "Consultation completed",
  };

  return (
    <>
      <Drawer
        open={bookingId !== null && !editing}
        onOpenChange={(o) => !o && close()}
        title={titles[view]}
        description={booking && view !== "details" ? `${booking.customer.name} · ${formatDate(booking.date)}, ${formatTime(booking.startTime)}` : undefined}
        footer={booking && view !== "details" && view !== "done" ? <FormFooter view={view} onBack={() => setView("details")} /> : undefined}
      >
        {isPending || !booking ? (
          <DetailsSkeleton />
        ) : (
          <AnimatePresence mode="wait" initial={false}>
            <motion.div
              key={`${booking.id}-${view}`}
              initial={{ opacity: 0, x: 12 }}
              animate={{ opacity: 1, x: 0 }}
              exit={{ opacity: 0, x: -12 }}
              transition={{ duration: 0.18 }}
            >
              {view === "details" && <Details booking={booking} onAction={setView} onEdit={() => setEditing(true)} onOpen={onBookingChange} onClose={close} />}
              {view === "complete" && <CompleteForm booking={booking} onDone={() => setView("done")} />}
              {view === "reschedule" && (
                <RescheduleForm
                  booking={booking}
                  onDone={(next) => {
                    setView("details");
                    onBookingChange?.(next.id);
                  }}
                />
              )}
              {view === "cancel" && <CancelForm booking={booking} onDone={() => setView("details")} />}
              {view === "done" && <Done booking={booking} onContinue={() => setView("details")} />}
            </motion.div>
          </AnimatePresence>
        )}
      </Drawer>
      <BookingFormDrawer open={editing} onClose={() => setEditing(false)} booking={booking} />
    </>
  );
}

function FormFooter({ view, onBack }: { view: View; onBack: () => void }) {
  const { complete, reschedule, cancel } = useBookingActions();
  const busy = complete.isPending || reschedule.isPending || cancel.isPending;
  const label = { complete: "Complete consultation", reschedule: "Reschedule", cancel: "Cancel booking" }[view as "complete"];
  return (
    <>
      <Button type="button" variant="secondary" onClick={onBack} disabled={busy}>
        Back
      </Button>
      <Button type="submit" form={`booking-${view}-form`} variant={view === "cancel" ? "danger" : "primary"} disabled={busy}>
        {busy ? "Saving…" : label}
      </Button>
    </>
  );
}

// ---- Details ---------------------------------------------------------------------------------------

function Details({
  booking,
  onAction,
  onEdit,
  onOpen,
  onClose,
}: {
  booking: BookingDetail;
  onAction: (v: View) => void;
  onEdit: () => void;
  onOpen?: (id: number) => void;
  onClose: () => void;
}) {
  const { data: session } = useSession();
  const { data: locale } = useLocale();
  const { noShow } = useBookingActions();
  const [confirmNoShow, setConfirmNoShow] = useState(false);
  const [recording, setRecording] = useState(false);
  const canManage = can(session?.user, Permission.BookingsManage);
  const canRecordPayment = can(session?.user, Permission.PaymentsManage);
  const canComplete = can(session?.user, Permission.BookingsComplete);
  const canDelete = can(session?.user, Permission.RecordsDelete);
  const deletePayment = useDeleteRecord("payment");
  const deleteBooking = useDeleteRecord("booking");
  const isBooked = booking.status === "Booked";
  const started = booking.date <= (locale?.today ?? today());

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-4">
        <div>
          <Link href={`/customers/${booking.customer.id}`} onClick={onClose} className="font-display text-xl font-bold tracking-tight hover:text-brand-strong">
            {booking.customer.name}
          </Link>
          <div className="mt-1.5 flex flex-wrap items-center gap-2">
            <StageBadge name={booking.customerStage.name} color={booking.customerStage.color} />
            {booking.customerWhatsApp && <span className="text-xs text-muted">{booking.customerWhatsApp}</span>}
          </div>
        </div>
        <BookingStatusBadge status={booking.status} />
      </div>

      <div className="rounded-2xl border border-line bg-gradient-to-br from-brand-soft/60 to-transparent p-4">
        <p className="font-display text-lg font-bold">{formatDate(booking.date)}</p>
        <p className="text-sm text-foreground/80">
          {formatTime(booking.startTime)} – {formatTime(booking.endTime)}
          {booking.doctor && <span className="text-muted"> · {booking.doctor.name}</span>}
        </p>
        <div className="mt-3 flex flex-wrap gap-1.5">
          {booking.treatments.map((t) => (
            <span key={t.id} className="rounded-lg bg-surface px-2.5 py-1 text-xs font-semibold text-brand-strong shadow-sm">
              {t.name}
            </span>
          ))}
          {booking.treatments.length === 0 && (
            <span className="rounded-lg border border-dashed border-amber-300 bg-amber-50 px-2.5 py-1 text-xs font-semibold text-amber-800">
              Treatment not chosen yet
            </span>
          )}
        </div>
      </div>

      {/* Always shown, so it's clear whether there is a note; empty ones offer to add one. */}
      <NoteCard
        label="Consultation notes"
        icon={StickyNote}
        className={cn(!booking.notes && "border-line bg-surface-muted/40")}
        action={
          !booking.notes && isBooked && canManage ? (
            <button type="button" onClick={onEdit} className="text-xs font-semibold text-brand hover:text-brand-strong">
              Add note
            </button>
          ) : undefined
        }
      >
        {booking.notes ?? <span className="text-muted">No notes for this consultation.</span>}
      </NoteCard>

      {(booking.rescheduledFrom || booking.rescheduledTo) && (
        <div className="space-y-2">
          {booking.rescheduledFrom && (
            <ChainLink label="Rescheduled from" link={booking.rescheduledFrom} onOpen={onOpen} />
          )}
          {booking.rescheduledTo && <ChainLink label="Moved to" link={booking.rescheduledTo} onOpen={onOpen} />}
        </div>
      )}


      {booking.status === "Completed" && (
        <div className="space-y-4 rounded-2xl border border-emerald-100 bg-emerald-50/50 p-4">
          <MoneySummary booking={booking} currency={locale?.currency} />
          {booking.payments.length > 0 && (
            <ul className="space-y-1.5 border-t border-emerald-100 pt-3">
              {booking.payments.map((p) => (
                <li key={p.id} className="flex items-center justify-between gap-3 text-sm">
                  <span className="min-w-0">
                    <span className="font-semibold">{p.status === "Waived" ? "Waived" : "Paid"}</span>
                    <span className="text-muted">
                      {p.method && ` · ${p.method.name}`} · {formatDateTime(p.paymentDate ?? p.createdAt)}
                    </span>
                  </span>
                  <span className="flex shrink-0 items-center gap-1">
                    <span className={cn("font-semibold tabular-nums", p.status === "Waived" && "text-muted")}>
                      {formatMoney(p.amount, locale?.currency)}
                    </span>
                    {canDelete && (
                      <ConfirmDelete
                        what={`the ${formatMoney(p.amount, locale?.currency)} payment`}
                        pending={deletePayment.isPending}
                        onConfirm={() =>
                          deletePayment.mutate(p.id, { onSuccess: () => toast.success("Payment moved to the recycle bin") })
                        }
                      />
                    )}
                  </span>
                </li>
              ))}
            </ul>
          )}
          {booking.balance > 0 && canRecordPayment && (
            <div className="flex flex-wrap items-center gap-2">
              <Button size="sm" onClick={() => setRecording(true)}>
                Record payment
              </Button>
              <WaiveBalance bookingId={booking.id} balance={booking.balance} currency={locale?.currency} />
            </div>
          )}
          {booking.nextTreatment && booking.nextTreatmentDate && (
            <p className="text-sm">
              Next treatment: <span className="font-semibold">{booking.nextTreatment.name}</span> on{" "}
              <span className="font-semibold">{formatDate(booking.nextTreatmentDate)}</span>
            </p>
          )}
          {booking.doctorNotes && (
            <NoteCard label="Doctor notes" icon={Stethoscope} className="border-emerald-100 bg-surface">
              {booking.doctorNotes}
            </NoteCard>
          )}
        </div>
      )}

      {booking.status === "Cancelled" && (
        <Info label="Cancelled">
          {booking.cancellationReason?.name}
          {booking.cancellationNote && ` — ${booking.cancellationNote}`}
        </Info>
      )}
      {booking.status === "Rescheduled" && <Info label="Charge">No charge for a rescheduled appointment.</Info>}

      {isBooked && (canManage || canComplete) && (
        <div className="space-y-2 border-t border-line pt-5">
          {canComplete && (
            <Button className="w-full" size="lg" onClick={() => onAction("complete")}>
              <CircleCheck className="size-4" /> Complete consultation
            </Button>
          )}
          {canManage && (
            <div className="grid grid-cols-2 gap-2">
              <Button variant="secondary" onClick={() => onAction("reschedule")}>
                <CalendarClock className="size-4" /> Reschedule
              </Button>
              <Button variant="secondary" onClick={onEdit}>
                <Pencil className="size-4" /> Edit
              </Button>
              <Button variant="secondary" onClick={() => onAction("cancel")}>
                <XCircle className="size-4" /> Cancel
              </Button>
              <Button
                variant="secondary"
                disabled={!started || noShow.isPending}
                title={started ? undefined : "Available from the day of the appointment"}
                onClick={() => setConfirmNoShow(true)}
              >
                <UserX className="size-4" /> No-show
              </Button>
            </div>
          )}
          <AnimatePresence>
            {confirmNoShow && (
              <motion.div
                initial={{ opacity: 0, height: 0 }}
                animate={{ opacity: 1, height: "auto" }}
                exit={{ opacity: 0, height: 0 }}
                className="overflow-hidden"
              >
                <div className="flex items-center justify-between gap-3 rounded-xl border border-red-100 bg-red-50 p-3 text-sm">
                  <span className="text-red-800">Mark {booking.customer.name} as a no-show?</span>
                  <div className="flex gap-1">
                    <Button size="sm" variant="ghost" onClick={() => setConfirmNoShow(false)}>
                      Keep
                    </Button>
                    <Button
                      size="sm"
                      variant="danger"
                      onClick={() =>
                        noShow.mutate(booking.id, {
                          onSuccess: () => {
                            setConfirmNoShow(false);
                            toast.success("Marked as no-show");
                          },
                          onError: toastError,
                        })
                      }
                    >
                      Mark no-show
                    </Button>
                  </div>
                </div>
              </motion.div>
            )}
          </AnimatePresence>
        </div>
      )}

      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-xs text-muted">
          Booked {formatDateTime(booking.createdAt)}
          {booking.source && <> by {booking.source}</>}
        </p>
        {canDelete && (
          <ConfirmDelete
            label="Delete booking"
            what="this booking"
            pending={deleteBooking.isPending}
            onConfirm={() =>
              deleteBooking.mutate(booking.id, {
                onSuccess: () => {
                  toast.success("Booking moved to the recycle bin");
                  onClose();
                },
              })
            }
          />
        )}
      </div>
      <RecordPaymentDrawer
        pending={recording && booking.balance > 0 ? { bookingId: booking.id, customerName: booking.customer.name, amount: booking.balance } : null}
        onClose={() => setRecording(false)}
      />
    </div>
  );
}

/** Amount, paid and balance at a glance. */
function MoneySummary({ booking, currency }: { booking: BookingDetail; currency?: string }) {
  const owed = booking.balance > 0;
  const cells = [
    { label: "Amount", value: formatMoney(booking.consultationCharge, currency), tone: "" },
    { label: "Paid", value: formatMoney(booking.amountPaid, currency), tone: "" },
    {
      label: "Balance",
      value: formatMoney(booking.balance, currency),
      tone: owed ? "text-amber-700" : "text-emerald-700",
    },
  ];
  return (
    <div>
      <div className="grid grid-cols-3 gap-2">
        {cells.map((c) => (
          <div key={c.label} className="min-w-0">
            <p className="truncate text-[11px] font-semibold uppercase tracking-[0.05em] text-emerald-800/70">{c.label}</p>
            <p className={cn("font-display text-base font-bold tabular-nums sm:text-lg", c.tone)}>{c.value}</p>
          </div>
        ))}
      </div>
      <p className={cn("mt-1 text-xs font-medium", owed ? "text-amber-700" : "text-emerald-700")}>
        {PAYMENT_STATE_TEXT[booking.paymentStatus ?? "Paid"]}
      </p>
    </div>
  );
}

const PAYMENT_STATE_TEXT: Record<NonNullable<BookingDetail["paymentStatus"]>, string> = {
  Paid: "Paid in full",
  PartlyPaid: "Partly paid: the balance is still owed",
  Unpaid: "Not paid yet",
  Waived: "Balance waived",
  NoCharge: "No charge",
};

/** Writes off the remaining balance, after a confirmation. */
function WaiveBalance({ bookingId, balance, currency }: { bookingId: number; balance: number; currency?: string }) {
  const waive = useWaiveBalance();
  const [asking, setAsking] = useState(false);
  if (!asking)
    return (
      <Button size="sm" variant="ghost" onClick={() => setAsking(true)}>
        Waive balance
      </Button>
    );
  return (
    <span className="flex flex-wrap items-center gap-2 text-sm">
      <span className="text-muted">Waive {formatMoney(balance, currency)}?</span>
      <Button
        size="sm"
        variant="secondary"
        disabled={waive.isPending}
        onClick={() =>
          waive.mutate(bookingId, {
            onSuccess: () => toast.success(`${formatMoney(balance, currency)} waived`),
            onError: toastError,
            onSettled: () => setAsking(false),
          })
        }
      >
        {waive.isPending ? "Waiving…" : "Yes, waive"}
      </Button>
      <Button size="sm" variant="ghost" onClick={() => setAsking(false)}>
        Keep
      </Button>
    </span>
  );
}

function ChainLink({ label, link, onOpen }: { label: string; link: BookingDetail["rescheduledFrom"] & object; onOpen?: (id: number) => void }) {
  return (
    <button
      type="button"
      onClick={() => onOpen?.(link.id)}
      className="flex w-full items-center justify-between gap-3 rounded-xl border border-amber-100 bg-amber-50/60 px-3.5 py-2.5 text-left text-sm transition-colors hover:bg-amber-50"
    >
      <span>
        <span className="text-amber-800">{label}</span>{" "}
        <span className="font-semibold">
          {formatDate(link.date)}, {formatTime(link.startTime)}
        </span>
      </span>
      <ArrowRight className="size-4 text-amber-700" />
    </button>
  );
}

/** A note people need to read before or after the consultation, set apart from the rest. */
function NoteCard({
  label,
  icon: Icon,
  className,
  action,
  children,
}: {
  label: string;
  icon: typeof StickyNote;
  className?: string;
  action?: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <div className={cn("rounded-2xl border border-amber-100 bg-amber-50/60 p-4", className)}>
      <div className="flex items-center justify-between gap-2">
        <p className="flex items-center gap-1.5 text-xs font-semibold uppercase tracking-[0.06em] text-muted">
          <Icon className="size-3.5" /> {label}
        </p>
        {action}
      </div>
      <p className="mt-1.5 whitespace-pre-line text-sm leading-relaxed text-foreground">{children}</p>
    </div>
  );
}

function Info({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <p className="text-xs font-medium text-muted">{label}</p>
      <p className="mt-1 whitespace-pre-line text-sm">{children}</p>
    </div>
  );
}

// ---- Complete ----------------------------------------------------------------------------------------

/** Spec §24: next treatment date and treatment are both empty or both filled. Mirrors the backend. */
const completeSchema = z
  .object({
    consultationCharge: z.number({ error: "Enter the consultation amount (0 if free)." }).min(0, "The amount can't be negative."),
    paidAmount: z.number({ error: "Enter the amount paid (0 if nothing yet)." }).min(0, "The paid amount can't be negative."),
    paymentMethodId: z.number().nullable(),
    nextTreatmentDate: z.string(),
    nextTreatmentId: z.number().nullable(),
    doctorNotes: z.string().max(4000),
    /** Only asked for when the booking has no treatment yet. */
    treatmentIds: z.array(z.number()),
    needsTreatment: z.boolean(),
  })
  .superRefine((v, ctx) => {
    if (v.needsTreatment && v.treatmentIds.length === 0)
      ctx.addIssue({ code: "custom", path: ["treatmentIds"], message: "Choose the treatment for this consultation." });
    if (v.paidAmount > v.consultationCharge)
      ctx.addIssue({ code: "custom", path: ["paidAmount"], message: "The paid amount can't be more than the consultation amount." });
    if (v.paidAmount > 0 && v.paymentMethodId === null)
      ctx.addIssue({ code: "custom", path: ["paymentMethodId"], message: "Choose how the customer paid." });
    if (v.nextTreatmentDate && v.nextTreatmentId === null)
      ctx.addIssue({ code: "custom", path: ["nextTreatmentId"], message: "Choose the next treatment, or clear the date." });
    if (!v.nextTreatmentDate && v.nextTreatmentId !== null)
      ctx.addIssue({ code: "custom", path: ["nextTreatmentDate"], message: "Choose the next treatment date, or clear the treatment." });
  });
type CompleteValues = z.infer<typeof completeSchema>;

const idOrNull = (v: unknown) => (v === "" || v === null || v === undefined ? null : Number(v));

function CompleteForm({ booking, onDone }: { booking: BookingDetail; onDone: () => void }) {
  const { complete } = useBookingActions();
  const methods = useActiveLookup("payment-methods");
  const treatments = useActiveLookup("treatments");
  const { data: locale } = useLocale();
  const {
    register,
    handleSubmit,
    control,
    setValue,
    setError,
    formState: { errors },
  } = useForm<CompleteValues>({
    resolver: zodResolver(completeSchema),
    defaultValues: {
      paymentMethodId: null,
      nextTreatmentDate: "",
      nextTreatmentId: null,
      doctorNotes: "",
      treatmentIds: [],
      needsTreatment: booking.treatments.length === 0,
    },
  });
  const charge = useWatch({ control, name: "consultationCharge" });
  const paid = useWatch({ control, name: "paidAmount" });
  const paidSomething = Number.isFinite(paid) && paid > 0;
  const chosenTreatments = useWatch({ control, name: "treatmentIds" });

  const onSubmit = handleSubmit(async (v) => {
    try {
      await complete.mutateAsync({
        id: booking.id,
        consultationCharge: v.consultationCharge,
        paidAmount: v.paidAmount,
        paymentMethodId: v.paidAmount > 0 ? v.paymentMethodId : null,
        nextTreatmentDate: v.nextTreatmentDate || null,
        nextTreatmentId: v.nextTreatmentId,
        doctorNotes: v.doctorNotes || null,
        treatmentIds: v.needsTreatment ? v.treatmentIds : null,
      });
      onDone();
    } catch (error) {
      if (error instanceof ApiError && error.fieldErrors.length > 0) {
        for (const fe of error.fieldErrors) if (fe.field) setError(fe.field as keyof CompleteValues, { message: fe.message });
        return;
      }
      toastError(error);
    }
  });

  return (
    <form id="booking-complete-form" onSubmit={onSubmit} className="space-y-5" noValidate>
      {booking.treatments.length > 0 ? (
        <div className="flex flex-wrap gap-1.5">
          {booking.treatments.map((t) => (
            <span key={t.id} className="rounded-lg bg-brand-soft px-2.5 py-1 text-xs font-semibold text-brand-strong">
              {t.name}
            </span>
          ))}
        </div>
      ) : (
        // Booked without a treatment (the WhatsApp BOT couldn't tell): choose what was done.
        <fieldset>
          <legend className="mb-2 text-[13px] font-medium">
            Treatment
            <RequiredMark />
          </legend>
          <div className="flex flex-wrap gap-2">
            {treatments.data?.map((t) => {
              const on = chosenTreatments.includes(t.id);
              return (
                <button
                  key={t.id}
                  type="button"
                  aria-pressed={on}
                  onClick={() =>
                    setValue("treatmentIds", on ? chosenTreatments.filter((id) => id !== t.id) : [...chosenTreatments, t.id], { shouldValidate: true })
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
      )}
      {booking.notes && (
        <NoteCard label="Consultation notes" icon={StickyNote}>
          {booking.notes}
        </NoteCard>
      )}

      <div className="grid gap-4 sm:grid-cols-2">
        <MoneyInput
          label="Consultation amount"
          currency={locale?.currency}
          error={errors.consultationCharge?.message}
          autoFocus
          registration={register("consultationCharge", { valueAsNumber: true })}
        />
        <MoneyInput
          label="Paid amount"
          currency={locale?.currency}
          error={errors.paidAmount?.message}
          registration={register("paidAmount", { valueAsNumber: true })}
          action={
            Number.isFinite(charge) && charge > 0 ? (
              <button
                type="button"
                onClick={() => setValue("paidAmount", charge, { shouldValidate: true })}
                className="text-xs font-semibold text-brand hover:text-brand-strong"
              >
                Paid in full
              </button>
            ) : undefined
          }
        />
      </div>

      <BalancePreview charge={charge} paid={paid} currency={locale?.currency} />

      <AnimatePresence initial={false}>
        {paidSomething && (
          <motion.div initial={{ opacity: 0, height: 0 }} animate={{ opacity: 1, height: "auto" }} exit={{ opacity: 0, height: 0 }} className="overflow-hidden">
            <Field label="Payment method" required error={errors.paymentMethodId?.message}>
              {(p) => (
                <Select {...p} {...register("paymentMethodId", { setValueAs: idOrNull })}>
                  <option value="">Choose…</option>
                  {methods.data?.map((m) => (
                    <option key={m.id} value={m.id}>
                      {m.name}
                    </option>
                  ))}
                </Select>
              )}
            </Field>
          </motion.div>
        )}
      </AnimatePresence>

      <fieldset className="rounded-xl border border-line p-4">
        <legend className="px-1 text-[13px] font-medium">Next treatment</legend>
        <p className="-mt-1 mb-3 text-xs text-muted">Optional. Fill both, or leave both empty.</p>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Date" error={errors.nextTreatmentDate?.message}>
            {(p) => <Input {...p} type="date" min={booking.date} {...register("nextTreatmentDate")} />}
          </Field>
          <Field label="Treatment" error={errors.nextTreatmentId?.message}>
            {(p) => (
              <Select {...p} {...register("nextTreatmentId", { setValueAs: idOrNull })}>
                <option value="">None</option>
                {treatments.data?.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.name}
                  </option>
                ))}
              </Select>
            )}
          </Field>
        </div>
      </fieldset>

      <Field label="Doctor notes" optional error={errors.doctorNotes?.message}>
        {(p) => <Textarea {...p} rows={3} {...register("doctorNotes")} />}
      </Field>
    </form>
  );
}

/** A money field with the currency in front and an optional action (e.g. "Paid in full") by the label. */
function MoneyInput({
  label,
  currency,
  error,
  registration,
  autoFocus,
  action,
}: {
  label: string;
  currency?: string;
  error?: string;
  registration: UseFormRegisterReturn;
  autoFocus?: boolean;
  action?: React.ReactNode;
}) {
  return (
    <div className="relative">
      {action && <span className="absolute right-0 top-0">{action}</span>}
      <Field label={label} required error={error}>
        {(p) => (
          <div className="relative">
            <span className="pointer-events-none absolute left-3.5 top-1/2 -translate-y-1/2 text-sm font-semibold text-muted">
              {currency ?? "AED"}
            </span>
            <Input {...p} type="number" inputMode="decimal" min={0} step="0.01" autoFocus={autoFocus} className="pl-14 text-base font-semibold" {...registration} />
          </div>
        )}
      </Field>
    </div>
  );
}

/** Balance = amount − paid, worked out as they type. */
function BalancePreview({ charge, paid, currency }: { charge: number; paid: number; currency?: string }) {
  if (!Number.isFinite(charge)) return null;
  const paidNow = Number.isFinite(paid) ? paid : 0;
  const balance = Math.round((charge - paidNow) * 100) / 100;
  const over = balance < 0;
  return (
    <motion.div
      layout
      className={cn(
        "flex items-center justify-between rounded-xl border px-4 py-3",
        over ? "border-red-200 bg-red-50" : balance > 0 ? "border-amber-200 bg-amber-50/70" : "border-emerald-200 bg-emerald-50/70",
      )}
    >
      <div>
        <p className="text-sm font-semibold">Balance</p>
        <p className={cn("text-xs", over ? "text-red-700" : balance > 0 ? "text-amber-700" : "text-emerald-700")}>
          {over ? "More than the consultation amount" : balance > 0 ? "Owed: can be paid later or waived" : charge > 0 ? "Paid in full" : "No charge"}
        </p>
      </div>
      <motion.p
        key={balance}
        initial={{ opacity: 0.4, y: -3 }}
        animate={{ opacity: 1, y: 0 }}
        className={cn("font-display text-xl font-bold tabular-nums", over ? "text-red-700" : balance > 0 ? "text-amber-700" : "text-emerald-700")}
      >
        {formatMoney(Math.max(balance, 0), currency)}
      </motion.p>
    </motion.div>
  );
}

function Done({ booking, onContinue }: { booking: BookingDetail; onContinue: () => void }) {
  const { data: locale } = useLocale();
  return (
    <div className="flex flex-col items-center py-10 text-center">
      <motion.div
        initial={{ scale: 0 }}
        animate={{ scale: 1 }}
        transition={{ type: "spring", stiffness: 300, damping: 16 }}
        className="relative flex size-20 items-center justify-center rounded-full bg-emerald-500 text-white shadow-[0_12px_32px_-8px_rgb(16_185_129/0.6)]"
      >
        <motion.span initial={{ scale: 0, rotate: -45 }} animate={{ scale: 1, rotate: 0 }} transition={{ delay: 0.15, type: "spring", stiffness: 400, damping: 15 }}>
          <Check className="size-10" strokeWidth={3} />
        </motion.span>
        <motion.span
          className="absolute inset-0 rounded-full border-2 border-emerald-400"
          initial={{ scale: 1, opacity: 0.8 }}
          animate={{ scale: 1.6, opacity: 0 }}
          transition={{ duration: 0.9, delay: 0.1 }}
        />
      </motion.div>
      <motion.div initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: 0.25 }}>
        <p className="mt-6 font-display text-xl font-bold">Consultation completed</p>
        <p className="mt-1 text-sm text-muted">
          {booking.customer.name} · {formatMoney(booking.consultationCharge, locale?.currency)}
          {booking.balance > 0
            ? ` · ${formatMoney(booking.balance, locale?.currency)} still owed`
            : booking.paymentStatus === "Paid"
              ? " paid in full"
              : ""}
        </p>
        {booking.customerStage.systemKey === "customer" && (
          <p className="mt-3 inline-flex items-center gap-1.5 text-xs text-muted">
            <Stethoscope className="size-3.5" /> {booking.customer.name} is now a {booking.customerStage.name}
          </p>
        )}
        <div className="mt-6">
          <Button variant="secondary" onClick={onContinue}>
            View consultation
          </Button>
        </div>
      </motion.div>
    </div>
  );
}

// ---- Reschedule ----------------------------------------------------------------------------------

const toMinutes = (t: string) => {
  const [h, m] = t.split(":").map(Number);
  return h * 60 + m;
};

const rescheduleSchema = z
  .object({
    date: z.string().min(1, "Choose a date."),
    startTime: z.string().min(1, "Choose a start time."),
    endTime: z.string().min(1, "Choose an end time."),
    doctorId: z.number().nullable(),
  })
  .refine((v) => toMinutes(v.endTime) > toMinutes(v.startTime), { path: ["endTime"], message: "The end time must be after the start time." });
type RescheduleValues = z.infer<typeof rescheduleSchema>;

function RescheduleForm({ booking, onDone }: { booking: BookingDetail; onDone: (next: BookingDetail) => void }) {
  const { reschedule } = useBookingActions();
  const doctors = useDoctorOptions();
  const length = toMinutes(booking.endTime) - toMinutes(booking.startTime);
  const {
    register,
    handleSubmit,
    control,
    setValue,
    setError,
    formState: { errors },
  } = useForm<RescheduleValues>({
    resolver: zodResolver(rescheduleSchema),
    defaultValues: { date: booking.date, startTime: hhmm(booking.startTime), endTime: hhmm(booking.endTime), doctorId: booking.doctor?.id ?? null },
  });
  const [date, startTime, endTime, doctorId] = useWatch({ control, name: ["date", "startTime", "endTime", "doctorId"] });

  const onSubmit = handleSubmit(async (v) => {
    try {
      const next = await reschedule.mutateAsync({ id: booking.id, ...v });
      toast.success(`Rescheduled to ${formatDate(next.date)}, ${formatTime(next.startTime)}`);
      onDone(next);
    } catch (error) {
      if (error instanceof ApiError && error.fieldErrors.length > 0) {
        for (const fe of error.fieldErrors) if (fe.field) setError(fe.field as keyof RescheduleValues, { message: fe.message });
        return;
      }
      toastError(error);
    }
  });

  return (
    <form id="booking-reschedule-form" onSubmit={onSubmit} className="space-y-5" noValidate>
      <p className="rounded-xl bg-amber-50 px-3.5 py-3 text-sm text-amber-900">
        The current appointment is kept as <span className="font-semibold">Rescheduled</span> with no charge, and a new booking is created with the same treatments.
      </p>
      <div className="grid gap-4 sm:grid-cols-3">
        <Field label="New date" required error={errors.date?.message}>
          {(p) => <Input {...p} type="date" autoFocus {...register("date")} />}
        </Field>
        <Field label="Start" required error={errors.startTime?.message}>
          {(p) => (
            <Input
              {...p}
              type="time"
              step={900}
              {...register("startTime", {
                onChange: (e) => {
                  const s = e.target.value as string;
                  if (s) {
                    const end = toMinutes(s) + length;
                    setValue("endTime", `${String(Math.floor(end / 60) % 24).padStart(2, "0")}:${String(end % 60).padStart(2, "0")}`);
                  }
                },
              })}
            />
          )}
        </Field>
        <Field label="End" required error={errors.endTime?.message}>
          {(p) => <Input {...p} type="time" step={900} {...register("endTime")} />}
        </Field>
      </div>
      {doctors.data && doctors.data.length > 0 && (
        <Field label="Doctor" optional>
          {(p) => (
            <Select {...p} {...register("doctorId", { setValueAs: idOrNull })}>
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
      <DaySchedule date={date} startTime={startTime} endTime={endTime} doctorId={doctorId} excludeId={booking.id} />
    </form>
  );
}

// ---- Cancel --------------------------------------------------------------------------------------

const cancelSchema = z.object({
  cancellationReasonId: z.number({ error: "Choose a reason." }).int().positive("Choose a reason."),
  note: z.string().max(1000),
});

function CancelForm({ booking, onDone }: { booking: BookingDetail; onDone: () => void }) {
  const { cancel } = useBookingActions();
  const reasons = useActiveLookup("cancellation-reasons");
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<z.infer<typeof cancelSchema>>({ resolver: zodResolver(cancelSchema), defaultValues: { note: "" } });

  const onSubmit = handleSubmit(async (v) => {
    try {
      await cancel.mutateAsync({ id: booking.id, cancellationReasonId: v.cancellationReasonId, note: v.note || null });
      toast.success("Booking cancelled");
      onDone();
    } catch (error) {
      toastError(error);
    }
  });

  return (
    <form id="booking-cancel-form" onSubmit={onSubmit} className="space-y-5" noValidate>
      <p className="text-sm text-muted">The booking stays in the history with status Cancelled.</p>
      <Field label="Reason" required error={errors.cancellationReasonId?.message}>
        {(p) => (
          <Select {...p} autoFocus {...register("cancellationReasonId", { setValueAs: idOrNull })}>
            <option value="">Choose a reason…</option>
            {reasons.data?.map((r) => (
              <option key={r.id} value={r.id}>
                {r.name}
              </option>
            ))}
          </Select>
        )}
      </Field>
      <Field label="Note" optional error={errors.note?.message}>
        {(p) => <Textarea {...p} rows={3} {...register("note")} />}
      </Field>
    </form>
  );
}

function DetailsSkeleton() {
  return (
    <div className="space-y-5" aria-busy="true" aria-label="Loading">
      <Skeleton className="h-7 w-48" />
      <Skeleton className="h-28 w-full rounded-2xl" />
      <Skeleton className="h-12 w-full rounded-xl" />
    </div>
  );
}
