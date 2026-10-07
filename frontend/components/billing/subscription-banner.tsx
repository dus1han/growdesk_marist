"use client";

import { AnimatePresence, motion } from "framer-motion";
import { AlertTriangle, ArrowRight, CalendarClock, RefreshCw } from "lucide-react";
import Link from "next/link";
import { PayNowButton } from "@/components/billing/pay-now-button";
import { AnimatedNumber } from "@/components/ui/animated-number";
import { formatDate, formatStripeAmount, useBillingNotice } from "@/lib/api/billing";
import { cn } from "@/lib/utils";
import type { BillingNotice } from "@/types/billing";

/**
 * The overdue-payment notice for admins (dashboard and Subscription page): how many days are
 * left before GrowDesk is paused, with Pay now. Hidden for users who can't pay and when the
 * subscription is in order.
 */
export function SubscriptionBanner({
  onSubscriptionPage = false,
  className,
}: {
  /** On the Subscription page itself the plan card carries the buttons; the banner only informs. */
  onSubscriptionPage?: boolean;
  className?: string;
}) {
  const { data: notice } = useBillingNotice();
  const content = notice?.canManage ? describe(notice) : null;

  return (
    <AnimatePresence initial={false}>
      {notice && content && (
        <motion.section
          key="subscription-banner"
          initial={{ opacity: 0, y: -8 }}
          animate={{ opacity: 1, y: 0 }}
          exit={{ opacity: 0, y: -8 }}
          transition={{ duration: 0.3, ease: [0.22, 1, 0.36, 1] }}
          aria-labelledby="subscription-banner-title"
          className={cn(
            "relative mb-6 overflow-hidden rounded-2xl border shadow-card",
            content.urgent ? "border-amber-300/70 bg-gradient-to-br from-amber-50 via-white to-orange-50" : "border-line bg-surface",
            className,
          )}
        >
          <div className="flex flex-col gap-5 p-5 sm:flex-row sm:items-center">
            {notice.daysLeft !== null && notice.daysLeft > 0 ? (
              <Countdown days={notice.daysLeft} />
            ) : (
              <span
                className={cn(
                  "flex size-14 shrink-0 items-center justify-center rounded-2xl",
                  content.urgent ? "bg-amber-100 text-amber-700" : "bg-brand-soft text-brand",
                )}
              >
                {notice.state === "FinalAttempt" ? <RefreshCw className="size-6" /> : content.urgent ? <AlertTriangle className="size-6" /> : <CalendarClock className="size-6" />}
              </span>
            )}

            <div className="min-w-0 flex-1">
              <h2 id="subscription-banner-title" className="font-display text-[15px] font-semibold">
                {content.title}
              </h2>
              <p className="mt-1 text-sm text-muted">{content.body}</p>
            </div>

            {!onSubscriptionPage && (
              <div className="flex shrink-0 flex-col gap-2 sm:w-52">
                {content.action === "pay" && <PayNowButton label={content.payLabel} />}
                {content.action === "subscribe" && <PayNowButton label="Subscribe" variant="secondary" />}
                <Link href="/administration/subscription" className="inline-flex items-center justify-center gap-1 text-xs font-semibold text-muted hover:text-foreground">
                  Subscription details <ArrowRight className="size-3.5" />
                </Link>
              </div>
            )}
          </div>
        </motion.section>
      )}
    </AnimatePresence>
  );
}

function Countdown({ days }: { days: number }) {
  return (
    <div
      className="flex size-16 shrink-0 flex-col items-center justify-center rounded-2xl bg-gradient-to-br from-amber-500 to-orange-600 text-white shadow-[0_10px_24px_-10px_rgb(217_119_6/0.8)]"
      aria-label={`${days} ${days === 1 ? "day" : "days"} left`}
    >
      <motion.span
        animate={{ scale: [1, 1.06, 1] }}
        transition={{ duration: 2.4, repeat: Infinity, ease: "easeInOut" }}
        className="font-display text-2xl font-bold leading-none"
        aria-hidden
      >
        <AnimatedNumber value={days} format={(v) => String(Math.round(v))} />
      </motion.span>
      <span className="mt-0.5 text-[10px] font-semibold uppercase tracking-wider text-white/85" aria-hidden>
        {days === 1 ? "day left" : "days left"}
      </span>
    </div>
  );
}

interface BannerContent {
  title: string;
  body: string;
  urgent: boolean;
  action: "pay" | "subscribe" | null;
  payLabel?: string;
}

function describe(n: BillingNotice): BannerContent | null {
  const amount = n.amountDue !== null && n.currency ? formatStripeAmount(n.amountDue, n.currency) : "your subscription";
  const days = n.daysLeft === 1 ? "1 day" : `${n.daysLeft} days`;
  const pauseOn = formatDate(n.blockAt);

  if (n.state === "FinalAttempt") {
    return {
      title: "Final payment attempt in progress",
      body: `The grace period is over and we're charging your card for ${amount} one last time. If it doesn't go through, GrowDesk will be paused for everyone until it's paid.`,
      urgent: true,
      action: "pay",
      payLabel: `Pay ${amount}`,
    };
  }

  if (n.state === "PaymentDue" && n.reason === "PaymentFailed") {
    return {
      title: `Subscription payment overdue: ${days} left`,
      body: `We couldn't charge your card for ${amount} on ${formatDate(n.dueSince)}. We'll try your card again on ${pauseOn}. If that fails, GrowDesk will be paused for your whole team until it's paid.`,
      urgent: true,
      action: "pay",
      payLabel: `Pay ${amount} now`,
    };
  }

  if (n.state === "PaymentDue") {
    return {
      title: n.reason === "Ended" ? `Your subscription has ended: ${days} left` : `Subscription payment due: ${days} left`,
      body: `Subscribe by ${pauseOn} to keep GrowDesk running. After that it will be paused for everyone until payment is made.`,
      urgent: true,
      action: "subscribe",
    };
  }

  if (n.state === "NotSubscribed" && n.dueSince) {
    return {
      title: "Set up your GrowDesk subscription",
      body: `Your first payment is due on ${formatDate(n.dueSince)}. Add a card now and renewals are charged automatically.`,
      urgent: false,
      action: "subscribe",
    };
  }

  return null;
}
