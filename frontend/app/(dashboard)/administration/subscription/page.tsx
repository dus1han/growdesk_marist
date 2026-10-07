"use client";

import { motion } from "framer-motion";
import { CircleAlert, CreditCard, Download, ExternalLink, Loader2, Receipt, RefreshCw, ShieldCheck } from "lucide-react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useEffect, useRef } from "react";
import { toast } from "sonner";
import { PayNowButton } from "@/components/billing/pay-now-button";
import { SubscriptionBanner } from "@/components/billing/subscription-banner";
import { PageHeader } from "@/components/layout/page-header";
import { RequirePermission } from "@/components/layout/require-permission";
import { Button, buttonVariants } from "@/components/ui/button";
import { Card, CardHeader } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { Skeleton } from "@/components/ui/skeleton";
import { toastError } from "@/lib/api/admin";
import {
  formatDate,
  formatStripeAmount,
  useBillingInvoices,
  useBillingOverview,
  useBillingPortal,
  useSyncBilling,
} from "@/lib/api/billing";
import { formatRelative } from "@/lib/format";
import { useSession } from "@/lib/auth/session";
import { can, Permission } from "@/lib/permissions";
import { cn } from "@/lib/utils";
import type { BillingInvoice, BillingOverview, BillingPlan } from "@/types/billing";

export default function SubscriptionPage() {
  return (
    <RequirePermission permission={Permission.BillingManage}>
      {/* useSearchParams (the return from Stripe Checkout) needs a Suspense boundary. */}
      <Suspense>
        <Subscription />
      </Suspense>
    </RequirePermission>
  );
}

function Subscription() {
  const router = useRouter();
  const params = useSearchParams();
  const { data: overview, isPending, isError, refetch } = useBillingOverview();
  const sync = useSyncBilling();
  const disabled = overview?.notice.state === "Disabled";
  const { data: session } = useSession();
  const isOwner = can(session?.user, Permission.PlatformBilling);
  const invoices = useBillingInvoices(!!overview && !disabled);

  // Stripe is the source of truth: refresh from it on arrival (always, and right after Checkout).
  const synced = useRef(false);
  const { mutate: syncNow } = sync;
  useEffect(() => {
    if (synced.current) return;
    synced.current = true;
    const checkout = params.get("checkout");
    if (checkout === "success") toast.success("Card saved. Your subscription is being activated.");
    if (checkout === "cancelled") toast.info("Checkout cancelled. Nothing was charged.");
    if (checkout) router.replace("/administration/subscription");
    syncNow(undefined, { onError: () => undefined });
  }, [params, router, syncNow]);

  return (
    <>
      <PageHeader
        title="Subscription"
        description="Your GrowDesk plan. Renewals are charged automatically to the card on file."
        actions={
          !disabled && (
            <Button variant="secondary" size="sm" onClick={() => sync.mutate(undefined, { onError: toastError })} disabled={sync.isPending}>
              <RefreshCw className={cn("size-3.5", sync.isPending && "animate-spin")} />
              Refresh
            </Button>
          )
        }
      />

      <SubscriptionBanner onSubscriptionPage />

      {isPending ? (
        <Skeleton className="h-52 rounded-2xl" />
      ) : isError || !overview ? (
        <Card>
          <EmptyState
            icon={CircleAlert}
            title="Couldn't load the subscription"
            description="Please try again."
            action={<Button variant="secondary" onClick={() => void refetch()}>Try again</Button>}
          />
        </Card>
      ) : disabled ? (
        <Card>
          <EmptyState
            icon={CreditCard}
            title="Online payments aren't set up"
            description={
              isOwner
                ? "Connect Stripe and switch billing on in Stripe Settings."
                : "There is nothing to pay here. Contact GrowDesk support if you expected to see your plan."
            }
            action={
              isOwner ? (
                <Link href="/administration/stripe-settings" className={buttonVariants({ variant: "secondary" })}>
                  Open Stripe Settings
                </Link>
              ) : undefined
            }
          />
        </Card>
      ) : (
        <>
          <PlanCard overview={overview} />
          <PaymentHistory invoices={invoices.data} loading={invoices.isPending} error={invoices.isError} onRetry={() => void invoices.refetch()} />
        </>
      )}
    </>
  );
}

// ---- Plan ----------------------------------------------------------------------------------------

const SUBSCRIPTION_STATUS: Record<string, { label: string; badge: string; dot: string }> = {
  active: { label: "Active", badge: "bg-emerald-50 text-emerald-700", dot: "bg-emerald-500" },
  trialing: { label: "Trial", badge: "bg-brand-soft text-brand-strong", dot: "bg-brand" },
  past_due: { label: "Payment overdue", badge: "bg-amber-50 text-amber-700", dot: "bg-amber-500" },
  unpaid: { label: "Unpaid", badge: "bg-red-50 text-red-700", dot: "bg-red-500" },
  incomplete: { label: "Awaiting payment", badge: "bg-amber-50 text-amber-700", dot: "bg-amber-500" },
  incomplete_expired: { label: "Expired", badge: "bg-slate-100 text-slate-600", dot: "bg-slate-400" },
  canceled: { label: "Cancelled", badge: "bg-slate-100 text-slate-600", dot: "bg-slate-400" },
  paused: { label: "Paused", badge: "bg-slate-100 text-slate-600", dot: "bg-slate-400" },
};

function StatusBadge({ status }: { status: string | null }) {
  const s = (status && SUBSCRIPTION_STATUS[status]) || { label: "Not subscribed", badge: "bg-slate-100 text-slate-600", dot: "bg-slate-400" };
  return (
    <span className={cn("inline-flex items-center gap-1.5 whitespace-nowrap rounded-full px-2.5 py-1 text-xs font-semibold", s.badge)}>
      <span className={cn("size-1.5 rounded-full", s.dot)} />
      {s.label}
    </span>
  );
}

function planPrice(plan: BillingPlan) {
  const every = plan.intervalCount === 1 ? plan.interval : `${plan.intervalCount} ${plan.interval}s`;
  return { amount: formatStripeAmount(plan.amount, plan.currency), every };
}

function PlanCard({ overview }: { overview: BillingOverview }) {
  const portal = useBillingPortal();
  const running = overview.subscriptionStatus !== null && !["canceled", "incomplete_expired"].includes(overview.subscriptionStatus);
  const price = overview.plan ? planPrice(overview.plan) : null;
  const needsPayment = ["PaymentDue", "FinalAttempt", "Blocked"].includes(overview.notice.state);

  return (
    <motion.div initial={{ opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.35, ease: [0.22, 1, 0.36, 1] }}>
      <Card className="overflow-hidden">
        <div className="relative grid gap-6 p-6 md:grid-cols-[minmax(0,1fr)_auto] md:items-center">
          <div className="pointer-events-none absolute -right-24 -top-24 size-64 rounded-full bg-brand/10 blur-3xl" aria-hidden />

          <div className="relative min-w-0">
            <div className="flex flex-wrap items-center gap-2">
              <h2 className="font-display text-lg font-semibold">{overview.plan?.name ?? "GrowDesk"}</h2>
              <StatusBadge status={overview.subscriptionStatus} />
            </div>

            {price && (
              <p className="mt-3 flex items-baseline gap-1.5">
                <span className="font-display text-3xl font-bold tracking-tight">{price.amount}</span>
                <span className="text-sm text-muted">/ {price.every}</span>
              </p>
            )}

            <dl className="mt-5 grid gap-x-8 gap-y-3 text-sm sm:grid-cols-2">
              {running && overview.currentPeriodEnd && (
                <Detail label={overview.cancelAtPeriodEnd ? "Ends on" : "Next payment"}>{formatDate(overview.currentPeriodEnd)}</Detail>
              )}
              <Detail label="Card on file">
                {overview.cardLast4 ? (
                  <span className="inline-flex items-center gap-2">
                    <CreditCard className="size-4 text-muted" />
                    <span className="capitalize">{overview.cardBrand}</span> •••• {overview.cardLast4}
                  </span>
                ) : (
                  <span className="text-muted">None yet</span>
                )}
              </Detail>
              {overview.finalRetryError && (
                <Detail label="Last automatic charge">
                  <span className="text-danger">
                    {overview.finalRetryError} ({formatDate(overview.finalRetryAt)})
                  </span>
                </Detail>
              )}
            </dl>

            <p className="mt-5 flex items-center gap-1.5 text-xs text-muted">
              <ShieldCheck className="size-3.5" />
              Card details are held by Stripe and never stored in GrowDesk.
              {overview.lastSyncedAt && <> Checked {formatRelative(overview.lastSyncedAt)}.</>}
            </p>
          </div>

          <div className="relative flex flex-col gap-2 md:w-56">
            {needsPayment ? (
              <PayNowButton size="lg" />
            ) : !running ? (
              <PayNowButton size="lg" label="Subscribe" />
            ) : null}
            {overview.cardLast4 && (
              <Button
                variant="secondary"
                onClick={() => portal.mutate(undefined, { onSuccess: ({ url }) => window.location.assign(url), onError: toastError })}
                disabled={portal.isPending}
              >
                {portal.isPending ? <Loader2 className="size-4 animate-spin" /> : <CreditCard className="size-4" />}
                Update card
              </Button>
            )}
          </div>
        </div>
      </Card>
    </motion.div>
  );
}

function Detail({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <dt className="text-xs font-medium text-muted">{label}</dt>
      <dd className="mt-0.5 font-medium">{children}</dd>
    </div>
  );
}

// ---- Payment history -----------------------------------------------------------------------------

const INVOICE_STATUS: Record<string, { label: string; badge: string }> = {
  paid: { label: "Paid", badge: "bg-emerald-50 text-emerald-700" },
  open: { label: "Unpaid", badge: "bg-amber-50 text-amber-700" },
  uncollectible: { label: "Unpaid", badge: "bg-red-50 text-red-700" },
  void: { label: "Void", badge: "bg-slate-100 text-slate-600" },
};

function InvoiceStatus({ invoice }: { invoice: BillingInvoice }) {
  const s = INVOICE_STATUS[invoice.status] ?? { label: invoice.status, badge: "bg-slate-100 text-slate-600" };
  return <span className={cn("inline-flex whitespace-nowrap rounded-full px-2.5 py-1 text-xs font-semibold", s.badge)}>{s.label}</span>;
}

function period(i: BillingInvoice) {
  return i.periodStart && i.periodEnd ? `${formatDate(i.periodStart)} – ${formatDate(i.periodEnd)}` : "";
}

function InvoiceLinks({ invoice }: { invoice: BillingInvoice }) {
  const unpaid = invoice.status === "open" || invoice.status === "uncollectible";
  return (
    <span className="flex items-center justify-end gap-1">
      {invoice.hostedUrl && (
        <a
          href={invoice.hostedUrl}
          target="_blank"
          rel="noreferrer"
          className={cn(
            "inline-flex h-8 items-center gap-1.5 rounded-lg px-2.5 text-xs font-semibold transition-colors",
            unpaid ? "text-amber-700 hover:bg-amber-50" : "text-muted hover:bg-surface-muted hover:text-foreground",
          )}
        >
          {unpaid ? "Pay" : "View"} <ExternalLink className="size-3.5" />
        </a>
      )}
      {invoice.pdfUrl && (
        <a
          href={invoice.pdfUrl}
          target="_blank"
          rel="noreferrer"
          aria-label={`Download invoice ${invoice.number ?? ""} as PDF`}
          className="inline-flex size-8 items-center justify-center rounded-lg text-muted transition-colors hover:bg-surface-muted hover:text-foreground"
        >
          <Download className="size-4" />
        </a>
      )}
    </span>
  );
}

function PaymentHistory({
  invoices,
  loading,
  error,
  onRetry,
}: {
  invoices: BillingInvoice[] | undefined;
  loading: boolean;
  error: boolean;
  onRetry: () => void;
}) {
  return (
    <Card className="mt-6 overflow-hidden">
      <CardHeader title="Payment history" description="Every subscription invoice, with receipts to download." />
      {loading ? (
        <div className="space-y-3 p-5" aria-busy="true" aria-label="Loading">
          {Array.from({ length: 4 }, (_, i) => (
            <Skeleton key={i} className="h-10 w-full rounded-xl" />
          ))}
        </div>
      ) : error ? (
        <EmptyState
          icon={CircleAlert}
          title="Couldn't load the payment history"
          description="Please try again."
          action={<Button variant="secondary" onClick={onRetry}>Try again</Button>}
          className="py-10"
        />
      ) : !invoices || invoices.length === 0 ? (
        <EmptyState icon={Receipt} title="No payments yet" description="Invoices appear here once your subscription starts." className="py-10" />
      ) : (
        <>
          {/* Desktop: a table */}
          <table className="hidden w-full text-sm md:table">
            <thead>
              <tr className="border-b border-line text-left text-xs font-medium text-muted">
                <th className="px-5 py-3 font-medium">Date</th>
                <th className="px-5 py-3 font-medium">Invoice</th>
                <th className="px-5 py-3 font-medium">Period</th>
                <th className="px-5 py-3 text-right font-medium">Amount</th>
                <th className="px-5 py-3 font-medium">Status</th>
                <th className="px-5 py-3">
                  <span className="sr-only">Links</span>
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-line">
              {invoices.map((invoice, i) => (
                <motion.tr
                  key={invoice.id}
                  initial={{ opacity: 0 }}
                  animate={{ opacity: 1 }}
                  transition={{ delay: i * 0.03 }}
                  className="transition-colors hover:bg-surface-muted/60"
                >
                  <td className="whitespace-nowrap px-5 py-3 font-medium">{formatDate(invoice.date)}</td>
                  <td className="whitespace-nowrap px-5 py-3 text-muted">{invoice.number ?? "—"}</td>
                  <td className="whitespace-nowrap px-5 py-3 text-muted">{period(invoice)}</td>
                  <td className="whitespace-nowrap px-5 py-3 text-right font-semibold tabular-nums">
                    {formatStripeAmount(invoice.amount, invoice.currency)}
                  </td>
                  <td className="px-5 py-3">
                    <InvoiceStatus invoice={invoice} />
                    {invoice.paidAt && invoice.status === "paid" && invoice.attemptCount > 1 && (
                      <span className="ml-2 text-xs text-muted">after {invoice.attemptCount} attempts</span>
                    )}
                  </td>
                  <td className="px-5 py-2">
                    <InvoiceLinks invoice={invoice} />
                  </td>
                </motion.tr>
              ))}
            </tbody>
          </table>

          {/* Phones: cards */}
          <ul className="divide-y divide-line md:hidden">
            {invoices.map((invoice) => (
              <li key={invoice.id} className="flex items-center gap-3 px-4 py-3">
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <span className="font-semibold tabular-nums">{formatStripeAmount(invoice.amount, invoice.currency)}</span>
                    <InvoiceStatus invoice={invoice} />
                  </div>
                  <p className="mt-0.5 truncate text-xs text-muted">
                    {formatDate(invoice.date)}
                    {invoice.number && ` · ${invoice.number}`}
                  </p>
                </div>
                <InvoiceLinks invoice={invoice} />
              </li>
            ))}
          </ul>
        </>
      )}
    </Card>
  );
}
