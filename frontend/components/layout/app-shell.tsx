"use client";

import { useQueryClient } from "@tanstack/react-query";
import { motion } from "framer-motion";
import { usePathname, useRouter } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
import { toast } from "sonner";
import { Skeleton } from "@/components/ui/skeleton";
import { billingKeys, useBillingNotice } from "@/lib/api/billing";
import { PASSWORD_CHANGE_EVENT, SUBSCRIPTION_BLOCKED_EVENT, UNAUTHORIZED_EVENT } from "@/lib/api/client";
import { sessionQueryKey, useSession } from "@/lib/auth/session";
import { can, Permission } from "@/lib/permissions";
import { LiveBookings } from "./live-bookings";
import { MobileNav } from "./mobile-nav";
import { Sidebar } from "./sidebar";

const COLLAPSE_KEY = "growdesk.sidebar.collapsed";

/**
 * Authenticated layout. Owns the session lifecycle for every app page: shows a skeleton while
 * the session loads, sends signed-out users to /login, and ends the session when the API
 * answers 401 or the session's expiry time passes. While the subscription is unpaid past its grace
 * period, everyone is sent to the payment page instead.
 */
export function AppShell({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();
  const queryClient = useQueryClient();
  const { data: session, isPending, isError } = useSession();
  const [collapsed, setCollapsed] = useState(false);

  // Restore the remembered sidebar state after mount (localStorage is not available on the server).
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- one-time sync from localStorage
    setCollapsed(localStorage.getItem(COLLAPSE_KEY) === "1");
  }, []);

  const toggleCollapsed = () =>
    setCollapsed((c) => {
      localStorage.setItem(COLLAPSE_KEY, c ? "0" : "1");
      return !c;
    });

  const endSession = useCallback(
    (message: string) => {
      queryClient.clear();
      toast.info(message);
      router.replace(`/login?next=${encodeURIComponent(pathname)}`);
    },
    [queryClient, router, pathname],
  );

  // No valid session (expired cookie, deactivated user): back to login.
  useEffect(() => {
    if (!isPending && !isError && session === null) {
      router.replace(`/login?next=${encodeURIComponent(pathname)}`);
    }
  }, [isPending, isError, session, router, pathname]);

  // A temporary password (new account or admin reset) must be replaced before using the app.
  const mustChangePassword = !!session?.user.mustChangePassword;
  useEffect(() => {
    if (mustChangePassword) router.replace(`/change-password?next=${encodeURIComponent(pathname)}`);
  }, [mustChangePassword, router, pathname]);

  // The API says so mid-session (an admin just reset the password): refresh the session to redirect.
  useEffect(() => {
    const onPasswordChange = () => void queryClient.invalidateQueries({ queryKey: sessionQueryKey });
    window.addEventListener(PASSWORD_CHANGE_EVENT, onPasswordChange);
    return () => window.removeEventListener(PASSWORD_CHANGE_EVENT, onPasswordChange);
  }, [queryClient]);

  // Unpaid past the grace period: the app is paused, only the payment page works. Checked on
  // every app load (so signing in lands there) and whenever the API answers 402 mid-session.
  const { data: billing } = useBillingNotice(!!session);
  const blocked = !!billing?.blocked && !mustChangePassword;
  useEffect(() => {
    if (blocked) router.replace("/subscription-required");
  }, [blocked, router]);

  useEffect(() => {
    const onBlocked = () => void queryClient.invalidateQueries({ queryKey: billingKeys.notice });
    window.addEventListener(SUBSCRIPTION_BLOCKED_EVENT, onBlocked);
    return () => window.removeEventListener(SUBSCRIPTION_BLOCKED_EVENT, onBlocked);
  }, [queryClient]);

  // Any API call answering 401 ends the session.
  useEffect(() => {
    const onUnauthorized = () => endSession("Your session has expired. Please sign in again.");
    window.addEventListener(UNAUTHORIZED_EVENT, onUnauthorized);
    return () => window.removeEventListener(UNAUTHORIZED_EVENT, onUnauthorized);
  }, [endSession]);

  // Sign out on the dot when the session expires, even if the user is idle.
  useEffect(() => {
    if (!session) return;
    const ms = new Date(session.expiresAt).getTime() - Date.now();
    const timer = setTimeout(() => endSession("Your session has expired. Please sign in again."), Math.max(ms, 0));
    return () => clearTimeout(timer);
  }, [session, endSession]);

  if (isPending || !session || mustChangePassword || blocked) return <ShellSkeleton />;

  return (
    <div className="flex min-h-dvh">
      <div className="sticky top-0 hidden h-dvh shrink-0 lg:block">
        <Sidebar user={session.user} collapsed={collapsed} onToggle={toggleCollapsed} />
      </div>

      <div className="flex min-w-0 flex-1 flex-col">
        <MobileNav user={session.user} />
        <motion.main
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          transition={{ duration: 0.4 }}
          className="mx-auto w-full max-w-7xl flex-1 px-4 pb-24 pt-6 sm:px-6 lg:px-10 lg:pb-10 lg:pt-10"
        >
          {children}
        </motion.main>
      </div>
      {can(session.user, Permission.BookingsView) && <LiveBookings />}
    </div>
  );
}

function ShellSkeleton() {
  return (
    <div className="flex min-h-dvh" aria-busy="true" aria-label="Loading">
      <div className="hidden w-[260px] shrink-0 border-r border-line bg-surface p-5 lg:block">
        <div className="flex items-center gap-3">
          <Skeleton className="size-9 rounded-xl" />
          <Skeleton className="h-5 w-28" />
        </div>
        <div className="mt-8 space-y-3">
          {Array.from({ length: 5 }, (_, i) => (
            <Skeleton key={i} className="h-9 w-full rounded-xl" />
          ))}
        </div>
      </div>
      <div className="flex-1 px-4 pt-6 sm:px-6 lg:px-10 lg:pt-10">
        <Skeleton className="h-8 w-56" />
        <Skeleton className="mt-2 h-4 w-80 max-w-full" />
        <div className="mt-8 grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          {Array.from({ length: 4 }, (_, i) => (
            <Skeleton key={i} className="h-32 rounded-2xl" />
          ))}
        </div>
      </div>
    </div>
  );
}
