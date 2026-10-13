"use client";

import { AnimatePresence, motion } from "framer-motion";
import { Menu } from "lucide-react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useState } from "react";
import { LogoMark } from "@/components/ui/logo";
import { useFollowUpSummary } from "@/lib/api/follow-ups";
import { useBranding } from "@/lib/auth/session";
import { can } from "@/lib/permissions";
import { cn } from "@/lib/utils";
import type { CurrentUser } from "@/types/api";
import { mainNav } from "./nav-items";
import { Sidebar } from "./sidebar";

/** Mobile top bar with a slide-in drawer, plus a bottom bar for the four primary screens. */
export function MobileNav({ user }: { user: CurrentUser }) {
  const [open, setOpen] = useState(false);
  const pathname = usePathname();
  const { data: branding } = useBranding();
  const primary = mainNav.filter((i) => i.mobilePrimary && can(user, i.permission));

  // Close on Escape.
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && setOpen(false);
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open]);

  return (
    <>
      <header className="sticky top-0 z-30 flex h-14 items-center gap-3 border-b border-line bg-surface/85 px-4 backdrop-blur-xl lg:hidden">
        <button
          onClick={() => setOpen(true)}
          aria-label="Open menu"
          aria-expanded={open}
          className="-ml-1 flex size-9 items-center justify-center rounded-lg text-muted hover:bg-surface-muted"
        >
          <Menu className="size-5" />
        </button>
        <LogoMark className="size-7" />
        <span className="font-display text-base font-bold tracking-tight">{branding?.crmName ?? "GrowDesk"}</span>
      </header>

      <AnimatePresence>
        {open && (
          <div className="fixed inset-0 z-50 lg:hidden" role="dialog" aria-modal="true" aria-label="Navigation">
            <motion.div
              className="absolute inset-0 bg-foreground/40 backdrop-blur-sm"
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              onClick={() => setOpen(false)}
            />
            <motion.div
              className="absolute inset-y-0 left-0 shadow-pop"
              initial={{ x: "-100%" }}
              animate={{ x: 0 }}
              exit={{ x: "-100%" }}
              transition={{ type: "spring", stiffness: 380, damping: 38 }}
            >
              <Sidebar user={user} collapsed={false} variant="drawer" onNavigate={() => setOpen(false)} />
            </motion.div>
          </div>
        )}
      </AnimatePresence>

      <nav
        aria-label="Primary"
        className="fixed inset-x-0 bottom-0 z-30 border-t border-line bg-surface/90 pb-[env(safe-area-inset-bottom)] backdrop-blur-xl lg:hidden"
      >
        <ul className="grid" style={{ gridTemplateColumns: `repeat(${Math.max(primary.length, 1)}, minmax(0, 1fr))` }}>
          {primary.map(({ href, label, icon: Icon, badge }) => {
            const active = pathname === href || pathname.startsWith(`${href}/`);
            return (
              <li key={href}>
                <Link
                  href={href}
                  aria-current={active ? "page" : undefined}
                  className={cn(
                    "relative flex h-16 flex-col items-center justify-center gap-1 text-[11px] font-medium transition-colors",
                    active ? "text-brand-strong" : "text-muted",
                  )}
                >
                  {active && (
                    <motion.span
                      layoutId="mobile-nav-active"
                      className="absolute top-0 h-0.5 w-10 rounded-full bg-brand"
                      transition={{ type: "spring", stiffness: 500, damping: 38 }}
                    />
                  )}
                  <span className="relative">
                    <Icon className="size-5" />
                    {badge === "followUps" && <FollowUpDot />}
                  </span>
                  {label}
                </Link>
              </li>
            );
          })}
        </ul>
      </nav>
    </>
  );
}

/** Follow-ups due now, on the bottom bar icon. */
function FollowUpDot() {
  const { data } = useFollowUpSummary();
  if (!data || data.due === 0) return null;
  return (
    <span
      aria-label={`${data.due} follow-ups due`}
      className={cn(
        "absolute -right-2.5 -top-1.5 flex min-w-4 items-center justify-center rounded-full px-1 text-[10px] font-bold leading-4 text-white",
        data.overdue > 0 ? "bg-danger" : "bg-brand",
      )}
    >
      {data.due > 99 ? "99+" : data.due}
    </span>
  );
}
