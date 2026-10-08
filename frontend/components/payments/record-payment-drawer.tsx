"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { AnimatePresence, motion } from "framer-motion";
import { useForm, useWatch } from "react-hook-form";
import { toast } from "sonner";
import { z } from "zod";
import { formatMoney } from "@/components/bookings/booking-status";
import { Button } from "@/components/ui/button";
import { Drawer } from "@/components/ui/drawer";
import { Field, Input, Select } from "@/components/ui/form-controls";
import { toastError } from "@/lib/api/admin";
import { useLocale } from "@/lib/api/bookings";
import { ApiError } from "@/lib/api/client";
import { useActiveLookup } from "@/lib/api/customers";
import { useRecordPayment } from "@/lib/api/payments";
import { today } from "@/lib/dates";
import { cn } from "@/lib/utils";

/** A consultation that still owes money. */
export interface PendingPayment {
  bookingId: number;
  customerName: string;
  /** The balance still owed. */
  amount: number;
}

const schema = (balance: number) =>
  z.object({
    amount: z
      .number({ error: "Enter the amount received." })
      .positive("Enter the amount received.")
      .max(balance, `That's more than the balance.`),
    paymentMethodId: z.number({ error: "Choose how the customer paid." }).int().positive("Choose how the customer paid."),
    paymentDate: z.string().min(1, "Choose the payment date."),
  });
type FormValues = z.infer<ReturnType<typeof schema>>;

/** Money received for a consultation: the whole balance or part of it. */
export function RecordPaymentDrawer({ pending, onClose }: { pending: PendingPayment | null; onClose: () => void }) {
  const record = useRecordPayment();
  return (
    <Drawer
      open={pending !== null}
      onOpenChange={(o) => !o && onClose()}
      title="Record payment"
      description={pending ? `${pending.customerName}'s consultation` : undefined}
      footer={
        <>
          <Button type="button" variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="record-payment-form" disabled={record.isPending}>
            {record.isPending ? "Saving…" : "Record payment"}
          </Button>
        </>
      }
    >
      {pending && <RecordPaymentForm key={pending.bookingId} pending={pending} onClose={onClose} />}
    </Drawer>
  );
}

function RecordPaymentForm({ pending, onClose }: { pending: PendingPayment; onClose: () => void }) {
  const record = useRecordPayment();
  const methods = useActiveLookup("payment-methods");
  const { data: locale } = useLocale();
  const max = locale?.today ?? today();
  const {
    register,
    handleSubmit,
    setError,
    control,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema(pending.amount)), defaultValues: { amount: pending.amount, paymentDate: max } });
  const amount = useWatch({ control, name: "amount" });
  const left = Number.isFinite(amount) ? Math.max(0, Math.round((pending.amount - amount) * 100) / 100) : pending.amount;

  const onSubmit = handleSubmit(async (v) => {
    try {
      await record.mutateAsync({ bookingId: pending.bookingId, amount: v.amount, paymentMethodId: v.paymentMethodId, paymentDate: v.paymentDate });
      toast.success(
        `${formatMoney(v.amount, locale?.currency)} received from ${pending.customerName}` +
          (left > 0 ? ` · ${formatMoney(left, locale?.currency)} still owed` : ""),
      );
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
    <form id="record-payment-form" onSubmit={onSubmit} className="space-y-5" noValidate>
      <div className="rounded-2xl border border-amber-100 bg-amber-50/60 p-4">
        <p className="text-xs font-semibold uppercase tracking-[0.06em] text-amber-700">Balance</p>
        <p className="mt-1 font-display text-2xl font-bold">{formatMoney(pending.amount, locale?.currency)}</p>
      </div>
      <Field label="Amount received" required error={errors.amount?.message} hint="The whole balance, or part of it.">
        {(p) => (
          <div className="relative">
            <span className="pointer-events-none absolute left-3.5 top-1/2 -translate-y-1/2 text-sm font-semibold text-muted">
              {locale?.currency ?? "AED"}
            </span>
            <Input
              {...p}
              type="number"
              inputMode="decimal"
              min={0}
              max={pending.amount}
              step="0.01"
              autoFocus
              className="pl-14 text-base font-semibold"
              {...register("amount", { valueAsNumber: true })}
            />
          </div>
        )}
      </Field>
      <AnimatePresence initial={false}>
        {Number.isFinite(amount) && amount > 0 && amount <= pending.amount && (
          <motion.p
            initial={{ opacity: 0, height: 0 }}
            animate={{ opacity: 1, height: "auto" }}
            exit={{ opacity: 0, height: 0 }}
            className={cn("-mt-2 overflow-hidden text-sm font-medium", left > 0 ? "text-amber-700" : "text-emerald-700")}
          >
            {left > 0 ? `${formatMoney(left, locale?.currency)} will still be owed` : "Settles the balance in full"}
          </motion.p>
        )}
      </AnimatePresence>
      <Field label="Payment method" required error={errors.paymentMethodId?.message}>
        {(p) => (
          <Select {...p} {...register("paymentMethodId", { setValueAs: (v) => (v === "" ? null : Number(v)) })}>
            <option value="">Choose…</option>
            {methods.data?.map((m) => (
              <option key={m.id} value={m.id}>
                {m.name}
              </option>
            ))}
          </Select>
        )}
      </Field>
      <Field label="Paid on" required error={errors.paymentDate?.message}>
        {(p) => <Input {...p} type="date" max={max} {...register("paymentDate")} />}
      </Field>
    </form>
  );
}
