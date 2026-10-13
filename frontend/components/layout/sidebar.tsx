"use client";

import { AnimatePresence, motion } from "framer-motion";
import { PanelLeftClose, PanelLeftOpen } from "lucide-react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { LogoMark } from "@/components/ui/logo";
import { useFollowUpSummary } from "@/lib/api/follow-ups";
import { useBranding } from "@/lib/auth/session";
import { can } from "@/lib/permissions";
import { cn } from "@/lib/utils";
import type { CurrentUser } from "@/types/api";
import { mainNav, managementNav, type NavItem } from "./nav-items";
import { UserMenu } from "./user-menu";

const spring = { type: "spring", stiffness: 380, damping: 34 } as const;

interface SidebarProps {
  user: CurrentUser;
  collapsed: boolean;
  onToggle?: () => void;
  /** Rendered inside the mobile drawer: always expanded, no collapse control. */
  variant?: "desktop" | "drawer";
  onNavigate?: () => void;
}

export function Sidebar({ user, collapsed, onToggle, variant = "desktop", onNavigate }: SidebarProps) {
  const { data: branding } = useBranding();
  const isCollapsed = variant === "desktop" && collapsed;
  const main = mainNav.filter((i) => can(user, i.permission));
  const management = managementNav.filter((i) => can(user, i.permission));

  return (
    <motion.aside
      initial={false}
      animate={{ width: variant === "drawer" ? 280 : isCollapsed ? 76 : 260 }}
      transition={spring}
      className="flex h-full flex-col border-r border-line bg-surface"
    >
      {/* Brand + collapse control */}
      <div className={cn("flex h-16 items-center gap-3 pl-5 pr-3", isCollapsed && "justify-center px-0")}>
        {isCollapsed && onToggle ? (
          // Collapsed: the logo doubles as the expand button and shows the panel icon on hover.
          <button
            onClick={onToggle}
            aria-label="Expand sidebar"
            aria-expanded={false}
            title="Expand sidebar"
            className="group/expand relative flex size-9 items-center justify-center rounded-xl"
          >
            <LogoMark className="size-9 transition-opacity duration-150 group-hover/expand:opacity-0 group-focus-visible/expand:opacity-0" />
            <span className="absolute inset-0 flex items-center justify-center rounded-xl bg-surface-muted text-foreground opacity-0 transition-opacity duration-150 group-hover/expand:opacity-100 group-focus-visible/expand:opacity-100">
              <PanelLeftOpen className="size-[18px]" />
            </span>
          </button>
        ) : (
          <LogoMark className="size-9 shrink-0" />
        )}
        <AnimatePresence initial={false}>
          {!isCollapsed && (
            <motion.div
              initial={{ opacity: 0, x: -6 }}
              animate={{ opacity: 1, x: 0 }}
              exit={{ opacity: 0, x: -6 }}
              transition={{ duration: 0.15 }}
              className="flex min-w-0 flex-1 items-center justify-between gap-2"
            >
              <p className="truncate font-display text-[17px] font-bold tracking-tight">{branding?.crmName ?? "GrowDesk"}</p>
              {variant === "desktop" && onToggle && (
                <button
                  onClick={onToggle}
                  aria-label="Collapse sidebar"
                  aria-expanded
                  title="Collapse sidebar"
                  className="flex size-8 shrink-0 items-center justify-center rounded-lg text-muted transition-colors hover:bg-surface-muted hover:text-foreground"
                >
                  <PanelLeftClose className="size-[18px]" />
                </button>
              )}
            </motion.div>
          )}
        </AnimatePresence>
      </div>

      {/* Navigation */}
      {/* Collapsed: overflow must stay visible or the hover tooltips are clipped. */}
      <nav
        className={cn("flex-1 space-y-6 px-3 py-4", isCollapsed ? "overflow-visible" : "overflow-y-auto overflow-x-hidden")}
        aria-label="Main"
      >
        <NavGroup items={main} collapsed={isCollapsed} onNavigate={onNavigate} layoutGroup={variant} />
        {management.length > 0 && (
          <div>
            <div className="mb-2 h-4 px-3">
              {!isCollapsed ? (
                <p className="text-[11px] font-semibold uppercase tracking-[0.08em] text-muted/70">Management</p>
              ) : (
                <div className="mx-auto h-px w-6 translate-y-2 bg-line" />
              )}
            </div>
            <NavGroup items={management} collapsed={isCollapsed} onNavigate={onNavigate} layoutGroup={variant} />
          </div>
        )}
      </nav>

      {/* User */}
      <div className="border-t border-line p-3">
        <UserMenu user={user} collapsed={isCollapsed} />
      </div>
    </motion.aside>
  );
}

function NavGroup({
  items,
  collapsed,
  onNavigate,
  layoutGroup,
}: {
  items: NavItem[];
  collapsed: boolean;
  onNavigate?: () => void;
  layoutGroup: string;
}) {
  const pathname = usePathname();

  return (
    <ul className="space-y-1">
      {items.map(({ href, label, icon: Icon, badge }) => {
        const active = pathname === href || pathname.startsWith(`${href}/`);
        return (
          <li key={href} className="group relative">
            <Link
              href={href}
              onClick={onNavigate}
              aria-current={active ? "page" : undefined}
              aria-label={collapsed ? label : undefined}
              className={cn(
                "relative flex h-10 items-center gap-3 rounded-xl px-3 text-sm font-medium transition-colors",
                active ? "text-brand-strong" : "text-muted hover:bg-surface-muted hover:text-foreground",
                collapsed && "justify-center px-0",
              )}
            >
              {active && (
                <motion.span
                  layoutId={`nav-active-${layoutGroup}`}
                  className="absolute inset-0 rounded-xl bg-brand-soft"
                  transition={{ type: "spring", stiffness: 500, damping: 38 }}
                />
              )}
              <Icon className="relative size-[18px] shrink-0" />
              {!collapsed && <span className="relative truncate">{label}</span>}
              {badge === "followUps" && <FollowUpCount collapsed={collapsed} />}
            </Link>

            {/* Tooltip when collapsed */}
            {collapsed && (
              <span
                role="tooltip"
                className="pointer-events-none absolute left-full top-1/2 z-50 ml-3 -translate-x-1 -translate-y-1/2 whitespace-nowrap rounded-lg bg-foreground px-2.5 py-1.5 text-xs font-medium text-white opacity-0 shadow-pop transition-all duration-150 group-hover:translate-x-0 group-hover:opacity-100 group-focus-within:translate-x-0 group-focus-within:opacity-100"
              >
                {label}
              </span>
            )}
          </li>
        );
      })}
    </ul>
  );
}

/** Follow-ups due now (overdue in red). Hidden when there are none. */
function FollowUpCount({ collapsed }: { collapsed: boolean }) {
  const { data } = useFollowUpSummary();
  if (!data || data.due === 0) return null;
  return (
    <motion.span
      key={data.due}
      initial={{ scale: 0.6, opacity: 0 }}
      animate={{ scale: 1, opacity: 1 }}
      transition={{ type: "spring", stiffness: 500, damping: 25 }}
      aria-label={`${data.due} follow-ups due${data.overdue ? `, ${data.overdue} overdue` : ""}`}
      className={cn(
        "relative ml-auto flex min-w-5 items-center justify-center rounded-full px-1.5 text-[11px] font-bold leading-5 text-white",
        data.overdue > 0 ? "bg-danger" : "bg-brand",
        collapsed && "absolute right-1 top-1 ml-0 min-w-4 px-1 text-[10px] leading-4",
      )}
    >
      {data.due > 99 ? "99+" : data.due}
    </motion.span>
  );
}
