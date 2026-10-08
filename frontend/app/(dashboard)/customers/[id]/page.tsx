"use client";

import * as Popover from "@radix-ui/react-popover";
import { AnimatePresence, motion } from "framer-motion";
import {
  ArrowLeft,
  AtSign,
  CalendarPlus,
  Check,
  ChevronDown,
  CircleAlert,
  Mail,
  Megaphone,
  MessageCircle,
  Pencil,
  Phone,
  Wallet,
  type LucideIcon,
} from "lucide-react";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useState } from "react";
import { toast } from "sonner";
import { BookingDetailsDrawer } from "@/components/bookings/booking-details-drawer";
import { BookingFormDrawer } from "@/components/bookings/booking-form-drawer";
import { formatMoney } from "@/components/bookings/booking-status";
import { CustomerBookingsCard } from "@/components/customers/customer-bookings-card";
import { CustomerPaymentsCard } from "@/components/customers/customer-payments-card";
import { whatsAppUrl } from "@/components/customers/whatsapp-link";
import { CustomerFormDrawer } from "@/components/customers/customer-form-drawer";
import { ConsultationBadge } from "@/components/customers/consultation-badge";
import { StageBadge } from "@/components/customers/stage-badge";
import { RequirePermission } from "@/components/layout/require-permission";
import { Button } from "@/components/ui/button";
import { ConfirmDelete } from "@/components/ui/confirm-delete";
import { Card, CardHeader } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { Skeleton } from "@/components/ui/skeleton";
import { toastError } from "@/lib/api/admin";
import { useLocale } from "@/lib/api/bookings";
import { useDeleteRecord } from "@/lib/api/recycle-bin";
import { ApiError } from "@/lib/api/client";
import { describeActivity } from "@/components/activity/describe-activity";
import { toSavePayload, useActiveLookup, useCustomer, useCustomerActivity, useSaveCustomer } from "@/lib/api/customers";
import { useSession } from "@/lib/auth/session";
import { followUpState, formatDate } from "@/lib/dates";
import { formatDateTime, formatRelative } from "@/lib/format";
import { can, Permission } from "@/lib/permissions";
import { cn, initials } from "@/lib/utils";
import type { CustomerDetail } from "@/types/customers";

export default function CustomerProfilePage() {
  const { id } = useParams<{ id: string }>();
  return (
    <RequirePermission permission={Permission.CustomersView}>
      <Profile id={Number(id)} />
    </RequirePermission>
  );
}

function Profile({ id }: { id: number }) {
  const { data: customer, isPending, error } = useCustomer(id);
  const { data: session } = useSession();
  const canManage = can(session?.user, Permission.CustomersManage);
  const canBook = can(session?.user, Permission.BookingsManage);
  const canSeeBookings = can(session?.user, Permission.BookingsView);
  const canSeePayments = can(session?.user, Permission.PaymentsView);
  const [editing, setEditing] = useState(false);
  const [booking, setBooking] = useState(false);
  const [openBookingId, setOpenBookingId] = useState<number | null>(null);

  if (isPending) return <ProfileSkeleton />;
  if (error || !customer) {
    const notFound = error instanceof ApiError && error.status === 404;
    return (
      <Card className="mx-auto mt-6 max-w-lg">
        <EmptyState
          icon={CircleAlert}
          title={notFound ? "Customer not found" : "Couldn't load this customer"}
          description={notFound ? "They may have been removed, or the link is wrong." : "Check your connection and try again."}
          action={
            <Link href="/customers" className="text-sm font-semibold text-brand hover:text-brand-strong">
              Back to customers
            </Link>
          }
          className="py-14"
        />
      </Card>
    );
  }

  return (
    <>
      <Link href="/customers" className="mb-4 inline-flex items-center gap-1.5 text-sm font-medium text-muted transition-colors hover:text-foreground">
        <ArrowLeft className="size-4" /> Customers
      </Link>

      <ProfileHeader customer={customer} canManage={canManage} onEdit={() => setEditing(true)} onBook={canBook ? () => setBooking(true) : undefined} />

      <div className="mt-6 grid gap-6 xl:grid-cols-[minmax(0,1fr)_360px]">
        <div className="space-y-6">
          {canSeeBookings && (
            <CustomerBookingsCard customerId={customer.id} onOpen={setOpenBookingId} onBook={canBook ? () => setBooking(true) : undefined} />
          )}

          <Card>
            <CardHeader title="Interested treatments" />
            <div className="flex flex-wrap gap-2 p-5">
              {customer.treatments.length === 0 ? (
                <p className="text-sm text-muted">No treatments recorded yet.</p>
              ) : (
                customer.treatments.map((t, i) => (
                  <motion.span
                    key={t.id}
                    initial={{ opacity: 0, scale: 0.9 }}
                    animate={{ opacity: 1, scale: 1 }}
                    transition={{ delay: i * 0.04 }}
                    className="rounded-xl border border-brand/20 bg-brand-soft px-3.5 py-2 text-sm font-semibold text-brand-strong"
                  >
                    {t.name}
                  </motion.span>
                ))
              )}
            </div>
          </Card>

          <Card>
            <CardHeader title="Details" />
            <dl className="grid gap-x-6 gap-y-5 p-5 sm:grid-cols-2">
              <Detail label="Next follow-up">
                <FollowUpValue date={customer.nextFollowUpDate} />
              </Detail>
              <Detail label="Last contact">{customer.lastContactDate ? formatDate(customer.lastContactDate) : <Empty />}</Detail>
              <Detail label="Instagram">
                {customer.instagram ? (
                  <a
                    href={`https://instagram.com/${customer.instagram}`}
                    target="_blank"
                    rel="noopener noreferrer"
                    title="Open on Instagram"
                    className="inline-flex items-center gap-1 text-brand-strong underline-offset-2 hover:underline"
                  >
                    <AtSign className="size-3.5" />
                    {customer.instagram}
                  </a>
                ) : (
                  <Empty />
                )}
              </Detail>
              <Detail label="Assigned to">{customer.assignedUser?.name ?? <Empty />}</Detail>
              <Detail label="Added">
                <span title={formatDateTime(customer.createdAt)}>{formatDate(customer.createdAt.slice(0, 10))}</span>
              </Detail>
              <Detail label="Last updated">
                <span title={formatDateTime(customer.updatedAt)}>{formatRelative(customer.updatedAt)}</span>
              </Detail>
              {customer.customFields.map((f) => (
                <Detail key={f.key} label={f.label}>
                  {f.display || <Empty />}
                </Detail>
              ))}
            </dl>
          </Card>

          <Card>
            <CardHeader title="Notes" />
            <p className="whitespace-pre-line p-5 text-sm leading-relaxed text-foreground/85">
              {customer.notes ?? <span className="text-muted">No notes yet.</span>}
            </p>
          </Card>
        </div>

        <div className="space-y-6">
          {canSeePayments && <CustomerPaymentsCard customerId={customer.id} onOpenBooking={setOpenBookingId} />}
          <ActivityCard id={customer.id} />
        </div>
      </div>

      <CustomerFormDrawer open={editing} onClose={() => setEditing(false)} customer={customer} />
      <BookingFormDrawer
        open={booking}
        onClose={() => setBooking(false)}
        prefill={{ customer: { id: customer.id, name: customer.name, treatmentIds: customer.treatments.map((t) => t.id) } }}
        onSaved={(b) => setOpenBookingId(b.id)}
      />
      <BookingDetailsDrawer bookingId={openBookingId} onClose={() => setOpenBookingId(null)} onBookingChange={setOpenBookingId} />
    </>
  );
}

function ProfileHeader({
  customer,
  canManage,
  onEdit,
  onBook,
}: {
  customer: CustomerDetail;
  canManage: boolean;
  onEdit: () => void;
  onBook?: () => void;
}) {
  return (
    <Card className="relative overflow-hidden p-5 sm:p-6">
      <div
        className="pointer-events-none absolute -right-20 -top-24 size-72 rounded-full opacity-[0.12] blur-3xl"
        style={{ backgroundColor: customer.stage.color }}
        aria-hidden
      />
      <div className="relative flex flex-col gap-5 sm:flex-row sm:items-start sm:justify-between">
        <div className="flex min-w-0 items-start gap-4">
          <motion.span
            initial={{ scale: 0.8, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            transition={{ type: "spring", stiffness: 400, damping: 25 }}
            className="flex size-14 shrink-0 items-center justify-center rounded-2xl bg-gradient-to-br from-brand to-accent text-lg font-bold text-white shadow-[0_8px_24px_-8px_rgb(91_91_246/0.6)]"
          >
            {initials(customer.name)}
          </motion.span>
          <div className="min-w-0">
            <h1 className="truncate font-display text-2xl font-bold tracking-tight">{customer.name}</h1>
            <div className="mt-2 flex flex-wrap items-center gap-2">
              {canManage ? <StageMenu customer={customer} /> : <StageBadge name={customer.stage.name} color={customer.stage.color} />}
              <ConsultationBadge consultation={customer.consultation} />
            </div>
          </div>
        </div>
        <div className="flex flex-wrap items-center justify-end gap-2 self-start">
          <DeleteCustomer customer={customer} />
          {canManage && (
            <Button variant="secondary" onClick={onEdit}>
              <Pencil className="size-4" /> Edit
            </Button>
          )}
          {onBook && (
            <Button onClick={onBook}>
              <CalendarPlus className="size-4" /> Book consultation
            </Button>
          )}
        </div>
      </div>

      <div className="relative mt-5 flex flex-wrap gap-2">
        {customer.whatsApp && (
          <ContactPill icon={MessageCircle} label={customer.whatsApp} href={whatsAppUrl(customer.whatsApp)} title="Open in WhatsApp" />
        )}
        {customer.leadSource && (
          <span
            className="inline-flex items-center gap-2 rounded-xl border border-line bg-surface-muted/60 px-3 py-2 text-sm"
            title="Lead source: where this customer came from"
          >
            <Megaphone className="size-4 text-muted" />
            <span className="text-muted">Source</span>
            <span className="font-medium">{customer.leadSource.name}</span>
          </span>
        )}
        {!!customer.outstanding && <OwesPill amount={customer.outstanding} />}
        {customer.secondaryPhone && <ContactPill icon={Phone} label={customer.secondaryPhone} href={`tel:${customer.secondaryPhone.replace(/\s/g, "")}`} />}
        {customer.email && <ContactPill icon={Mail} label={customer.email} href={`mailto:${customer.email}`} />}
      </div>
    </Card>
  );
}

/**
 * To the recycle bin. The API refuses while the customer still has bookings, and says so; delete
 * those first (lowest level first), or restore from Administration → Recycle Bin.
 */
function DeleteCustomer({ customer }: { customer: CustomerDetail }) {
  const { data: session } = useSession();
  const router = useRouter();
  const remove = useDeleteRecord("customer");
  if (!can(session?.user, Permission.RecordsDelete)) return null;
  return (
    <ConfirmDelete
      label="Delete"
      what={customer.name}
      pending={remove.isPending}
      onConfirm={() =>
        remove.mutate(customer.id, {
          onSuccess: () => {
            toast.success(`${customer.name} moved to the recycle bin`);
            router.push("/customers");
          },
        })
      }
    />
  );
}

/** What the customer still owes on completed consultations (shown to users who can see payments). */
function OwesPill({ amount }: { amount: number }) {
  const { data: locale } = useLocale();
  return (
    <span
      className="inline-flex items-center gap-2 rounded-xl border border-amber-200 bg-amber-50 px-3 py-2 text-sm"
      title="Still owed on completed consultations. See Payments below."
    >
      <Wallet className="size-4 text-amber-600" />
      <span className="text-amber-800/80">Owes</span>
      <span className="font-semibold text-amber-800">{formatMoney(amount, locale?.currency)}</span>
    </span>
  );
}

function ContactPill({ icon: Icon, label, href, title }: { icon: LucideIcon; label: string; href: string; title?: string }) {
  const external = href.startsWith("http");
  return (
    <a
      href={href}
      title={title}
      target={external ? "_blank" : undefined}
      rel={external ? "noopener noreferrer" : undefined}
      className="inline-flex items-center gap-2 rounded-xl border border-line bg-surface px-3 py-2 text-sm font-medium transition-all hover:-translate-y-0.5 hover:border-brand/30 hover:text-brand-strong hover:shadow-card"
    >
      <Icon className="size-4 text-muted" />
      <span className="tabular-nums">{label}</span>
    </a>
  );
}

/** Change the status in two clicks, straight from the profile. */
function StageMenu({ customer }: { customer: CustomerDetail }) {
  const [open, setOpen] = useState(false);
  const stages = useActiveLookup("stages");
  const save = useSaveCustomer();

  const change = (stageId: number, name: string) => {
    setOpen(false);
    if (stageId === customer.stage.id) return;
    save.mutate(
      { id: customer.id, ...toSavePayload(customer), stageId },
      { onSuccess: () => toast.success(`${customer.name} is now ${name}`), onError: toastError },
    );
  };

  return (
    <Popover.Root open={open} onOpenChange={setOpen}>
      <Popover.Trigger className="group inline-flex items-center gap-1 rounded-full focus-visible:outline-offset-2" aria-label="Change status">
        <AnimatePresence mode="wait" initial={false}>
          <motion.span key={customer.stage.id} initial={{ opacity: 0, y: 4 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0, y: -4 }} transition={{ duration: 0.15 }}>
            <StageBadge name={customer.stage.name} color={customer.stage.color} />
          </motion.span>
        </AnimatePresence>
        <ChevronDown className="size-3.5 text-muted transition-transform group-data-[state=open]:rotate-180" />
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Content align="start" sideOffset={6} className="z-50 w-56 rounded-xl border border-line bg-surface p-1 shadow-pop">
          <p className="px-2.5 pb-1 pt-1.5 text-[11px] font-semibold uppercase tracking-[0.06em] text-muted">Change status to</p>
          {stages.data?.map((s) => (
            <button
              key={s.id}
              type="button"
              onClick={() => change(s.id, s.name)}
              className="flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left text-sm hover:bg-surface-muted"
            >
              <span className="size-2 rounded-full" style={{ backgroundColor: s.color ?? undefined }} />
              <span className="flex-1">{s.name}</span>
              {s.id === customer.stage.id && <Check className="size-4 text-brand" />}
            </button>
          ))}
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  );
}

function Detail({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <dt className="text-xs font-medium text-muted">{label}</dt>
      <dd className="mt-1 text-sm font-medium">{children}</dd>
    </div>
  );
}

const Empty = () => <span className="font-normal text-muted">—</span>;

function FollowUpValue({ date }: { date: string | null }) {
  const state = followUpState(date);
  if (!date) return <Empty />;
  return (
    <span className={cn("inline-flex items-center gap-2", state === "overdue" && "text-danger", state === "today" && "text-warning")}>
      {formatDate(date)}
      {state === "overdue" && <span className="rounded-full bg-red-50 px-2 py-0.5 text-[11px] font-semibold">Overdue</span>}
      {state === "today" && <span className="rounded-full bg-amber-50 px-2 py-0.5 text-[11px] font-semibold">Today</span>}
    </span>
  );
}

// ---- Activity timeline ---------------------------------------------------------------------------

function ActivityCard({ id }: { id: number }) {
  const { data, isPending } = useCustomerActivity(id);
  return (
    <Card className="self-start">
      <CardHeader title="Activity" />
      <div className="p-5">
        {isPending ? (
          <div className="space-y-4" aria-busy="true" aria-label="Loading">
            {Array.from({ length: 3 }, (_, i) => (
              <div key={i} className="flex gap-3">
                <Skeleton className="size-8 rounded-full" />
                <div className="flex-1 space-y-2">
                  <Skeleton className="h-3.5 w-3/4" />
                  <Skeleton className="h-3 w-1/3" />
                </div>
              </div>
            ))}
          </div>
        ) : !data || data.length === 0 ? (
          <p className="text-sm text-muted">No activity yet.</p>
        ) : (
          <ol className="relative space-y-5 before:absolute before:bottom-2 before:left-4 before:top-2 before:w-px before:bg-line">
            {data.map((a, i) => {
              const { icon: Icon, title, detail, tone } = describeActivity(a);
              return (
                <motion.li
                  key={a.id}
                  initial={{ opacity: 0, x: -8 }}
                  animate={{ opacity: 1, x: 0 }}
                  transition={{ duration: 0.25, delay: Math.min(i, 8) * 0.05 }}
                  className="relative flex gap-3"
                >
                  <span className={cn("relative z-10 flex size-8 shrink-0 items-center justify-center rounded-full ring-4 ring-surface", tone)}>
                    <Icon className="size-4" />
                  </span>
                  <div className="min-w-0 pt-0.5">
                    <p className="text-sm font-medium">{title}</p>
                    {detail && <p className="text-xs text-muted">{detail}</p>}
                    <p className="mt-0.5 text-xs text-muted" title={formatDateTime(a.createdAt)}>
                      {formatRelative(a.createdAt)}
                      {a.userName && ` · ${a.userName}`}
                    </p>
                  </div>
                </motion.li>
              );
            })}
          </ol>
        )}
      </div>
    </Card>
  );
}

function ProfileSkeleton() {
  return (
    <div aria-busy="true" aria-label="Loading">
      <Skeleton className="mb-4 h-4 w-24" />
      <Card className="p-6">
        <div className="flex items-center gap-4">
          <Skeleton className="size-14 rounded-2xl" />
          <div className="space-y-2">
            <Skeleton className="h-6 w-56" />
            <Skeleton className="h-5 w-24 rounded-full" />
          </div>
        </div>
      </Card>
      <div className="mt-6 grid gap-6 xl:grid-cols-[minmax(0,1fr)_360px]">
        <Skeleton className="h-64 rounded-2xl" />
        <Skeleton className="h-64 rounded-2xl" />
      </div>
    </div>
  );
}
