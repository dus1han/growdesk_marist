"use client";

import { motion } from "framer-motion";
import { CreditCard, Loader2, Lock, LogOut, RotateCcw } from "lucide-react";
import { useRouter } from "next/navigation";
import { useCallback, useEffect } from "react";
import { toast } from "sonner";
import { LoginBackdrop } from "@/components/auth/login-backdrop";
import { PayNowButton } from "@/components/billing/pay-now-button";
import { LogoMark } from "@/components/ui/logo";
import { Skeleton } from "@/components/ui/skeleton";
import { toastError } from "@/lib/api/admin";
import { formatDate, formatStripeAmount, useBillingNotice, useBillingOverview, useBillingPortal, useSyncBilling } from "@/lib/api/billing";
import { useBranding, useLogout, useSession } from "@/lib/auth/session";

const ease = [0.22, 1, 0.36, 1] as const;

/**
 * Where everyone lands while the subscription is unpaid past its grace period. Admins pay here;
 * everyone else is asked to contact one. Leaves for the dashboard as soon as it's paid.
 */
export function BlockedScreen() {
  const router = useRouter();
  const { data: session, isPending: sessionPending } = useSession();
  const { data: branding } = useBranding();
  const { data: notice, refetch, isFetching } = useBillingNotice(!!session);
  const canManage = !!notice?.canManage;
  const { data: overview } = useBillingOverview(canManage);
  const sync = useSyncBilling();
  const portal = useBillingPortal();
  const logout = useLogout();
  const name = branding?.crmName ?? "GrowDesk";

  // Signed out: sign in first. Paid (or never blocked): back into the app.
  useEffect(() => {
    if (!sessionPending && session === null) router.replace("/login?next=/subscription-required");
  }, [sessionPending, session, router]);
  useEffect(() => {
    if (notice && !notice.blocked) {
      router.replace("/dashboard");
    }
  }, [notice, router]);

  // Coming back from Stripe's payment page, or from another tab: check whether it went through.
  const { mutate: syncNow } = sync;
  const recheck = useCallback(() => {
    if (canManage) syncNow(undefined, { onError: () => void refetch() });
    else void refetch();
  }, [canManage, syncNow, refetch]);
  useEffect(() => {
    window.addEventListener("focus", recheck);
    const timer = setInterval(recheck, 60_000);
    return () => {
      window.removeEventListener("focus", recheck);
      clearInterval(timer);
    };
  }, [recheck]);

  const checking = sync.isPending || isFetching;
  const amount = notice?.amountDue != null && notice.currency ? formatStripeAmount(notice.amountDue, notice.currency) : null;

  return (
    <main className="relative flex min-h-dvh items-center justify-center overflow-hidden px-4 py-10 text-white">
      <LoginBackdrop />

      <motion.div
        initial={{ opacity: 0, y: 24, scale: 0.97 }}
        animate={{ opacity: 1, y: 0, scale: 1 }}
        transition={{ duration: 0.7, ease }}
        className="relative w-full max-w-[460px]"
      >
        <div className="mb-7 flex flex-col items-center text-center">
          <LogoMark className="size-12" />
          <p className="mt-3 font-display text-xl font-bold tracking-tight">{name}</p>
        </div>

        <div className="relative rounded-3xl border border-white/10 bg-white/[0.045] p-7 shadow-[0_30px_80px_-20px_rgb(0_0_0/0.6)] backdrop-blur-2xl sm:p-9">
          <div className="absolute inset-x-8 -top-px h-px bg-gradient-to-r from-transparent via-white/40 to-transparent" aria-hidden />

          <motion.span
            initial={{ scale: 0.6, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            transition={{ type: "spring", stiffness: 300, damping: 20, delay: 0.25 }}
            className="mx-auto flex size-14 items-center justify-center rounded-2xl bg-gradient-to-br from-amber-400 to-orange-600 shadow-[0_12px_32px_-10px_rgb(234_88_12/0.8)]"
          >
            <Lock className="size-6" />
          </motion.span>

          <h1 className="mt-5 text-center font-display text-[26px] font-bold tracking-tight">{name} is paused</h1>

          {!notice ? (
            <div className="mt-5 space-y-3" aria-busy="true" aria-label="Loading">
              <Skeleton className="h-4 w-full bg-white/10" />
              <Skeleton className="h-4 w-4/5 bg-white/10" />
              <Skeleton className="mt-6 h-12 w-full rounded-xl bg-white/10" />
            </div>
          ) : canManage ? (
            <>
              <p className="mt-2 text-center text-sm text-white/60">
                The subscription payment{amount ? ` of ${amount}` : ""} has been overdue since {formatDate(notice.dueSince)}
                {notice.reason === "PaymentFailed" ? ", and the last automatic charge didn't go through" : ""}. Pay now to unlock {name} for your whole team.
              </p>

              {overview?.finalRetryError && (
                <p className="mt-4 rounded-xl border border-white/10 bg-white/[0.04] px-3.5 py-2.5 text-center text-xs text-white/60">
                  Card {overview.cardBrand ? `(${capitalize(overview.cardBrand)} •••• ${overview.cardLast4})` : "on file"}: {overview.finalRetryError}
                </p>
              )}

              <PayNowButton
                tone="dark"
                size="lg"
                className="mt-6"
                label={amount ? `Pay ${amount} now` : notice.reason === "PaymentFailed" ? "Pay now" : "Subscribe"}
              />

              {overview?.cardLast4 && (
                <button
                  type="button"
                  onClick={() =>
                    portal.mutate(undefined, { onSuccess: ({ url }) => window.location.assign(url), onError: toastError })
                  }
                  disabled={portal.isPending}
                  className="mx-auto mt-3 flex items-center gap-1.5 text-sm font-medium text-white/70 hover:text-white disabled:opacity-60"
                >
                  {portal.isPending ? <Loader2 className="size-4 animate-spin" /> : <CreditCard className="size-4" />}
                  Use a different card for renewals
                </button>
              )}
            </>
          ) : (
            <p className="mt-2 text-center text-sm text-white/60">
              The {name} subscription payment is overdue. Please ask your administrator to sign in and pay. Everything will be
              exactly as you left it.
            </p>
          )}

          {notice && (
            <button
              type="button"
              onClick={() => {
                recheck();
                toast.info("Checking the payment…");
              }}
              disabled={checking}
              className="mx-auto mt-6 flex items-center gap-1.5 text-xs font-medium text-white/50 hover:text-white disabled:opacity-60"
            >
              <RotateCcw className={checking ? "size-3.5 animate-spin" : "size-3.5"} />
              Already paid? Check again
            </button>
          )}
        </div>

        <button
          type="button"
          onClick={() => logout.mutate()}
          className="mx-auto mt-6 flex items-center gap-1.5 text-xs text-white/40 hover:text-white/80"
        >
          <LogOut className="size-3.5" />
          Sign out{session ? ` (${session.user.fullName})` : ""}
        </button>
      </motion.div>
    </main>
  );
}

function capitalize(s: string) {
  return s.charAt(0).toUpperCase() + s.slice(1);
}
