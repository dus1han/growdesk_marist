"use client";

import * as Popover from "@radix-ui/react-popover";
import { AnimatePresence, motion } from "framer-motion";
import { BellRing, ChevronsUpDown, KeyRound, LogOut, PlayCircle, Volume2, VolumeX } from "lucide-react";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { ChangePasswordDrawer } from "@/components/auth/change-password-drawer";
import { useLogout } from "@/lib/auth/session";
import { desktopAlertPermission, playChime, requestDesktopAlerts, setSoundEnabled, soundEnabled } from "@/lib/live-alerts";
import { cn, initials } from "@/lib/utils";
import type { CurrentUser } from "@/types/api";

/** The signed-in user at the foot of the sidebar; opens a menu with account actions. */
export function UserMenu({ user, collapsed }: { user: CurrentUser; collapsed: boolean }) {
  const logout = useLogout();
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [changingPassword, setChangingPassword] = useState(false);
  const listRef = useRef<HTMLDivElement>(null);
  const [sound, setSound] = useState(true);
  const [alerts, setAlerts] = useState<NotificationPermission | "unsupported">("unsupported");

  // Browser-only settings: read after mount.
  useEffect(() => {
    if (!open) return;
    setSound(soundEnabled()); // eslint-disable-line react-hooks/set-state-in-effect -- read on open from localStorage
    setAlerts(desktopAlertPermission());
  }, [open]);

  // Arrow keys move between items, like a native menu.
  const onKeyDown = (e: React.KeyboardEvent) => {
    if (e.key !== "ArrowDown" && e.key !== "ArrowUp") return;
    e.preventDefault();
    const items = Array.from(listRef.current?.querySelectorAll<HTMLButtonElement>("[role=menuitem]:not(:disabled)") ?? []);
    const i = items.indexOf(document.activeElement as HTMLButtonElement);
    items[(i + (e.key === "ArrowDown" ? 1 : -1) + items.length) % items.length]?.focus();
  };

  const item = "flex w-full items-center gap-2.5 rounded-lg px-2.5 py-2 text-sm font-medium transition-colors focus-visible:outline-none";

  return (
    <>
      <Popover.Root open={open} onOpenChange={setOpen}>
        <Popover.Trigger
          aria-label={`Account menu for ${user.fullName}`}
          aria-haspopup="menu"
          title={collapsed ? `${user.fullName} · ${user.roles.join(", ")}` : undefined}
          className={cn(
            "group flex w-full items-center gap-3 rounded-xl p-2 text-left transition-colors hover:bg-surface-muted data-[state=open]:bg-surface-muted",
            collapsed && "justify-center p-1",
          )}
        >
          <span className="flex size-9 shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-brand to-accent text-xs font-bold text-white transition-transform duration-200 group-hover:scale-[1.04]">
            {initials(user.fullName)}
          </span>
          {!collapsed && (
            <>
              <span className="min-w-0 flex-1">
                <span className="block truncate text-sm font-semibold">{user.fullName}</span>
                <span className="block truncate text-xs text-muted">{user.roles.join(", ")}</span>
              </span>
              <ChevronsUpDown className="size-4 shrink-0 text-muted transition-colors group-hover:text-foreground" />
            </>
          )}
        </Popover.Trigger>

        <AnimatePresence>
          {open && (
            <Popover.Portal forceMount>
              <Popover.Content
                asChild
                forceMount
                side={collapsed ? "right" : "top"}
                align={collapsed ? "end" : "start"}
                sideOffset={8}
                collisionPadding={12}
                onOpenAutoFocus={(e) => {
                  e.preventDefault();
                  listRef.current?.querySelector<HTMLButtonElement>("[role=menuitem]")?.focus();
                }}
              >
                <motion.div
                  initial={{ opacity: 0, scale: 0.96, y: collapsed ? 0 : 6 }}
                  animate={{ opacity: 1, scale: 1, y: 0 }}
                  exit={{ opacity: 0, scale: 0.96, y: collapsed ? 0 : 6 }}
                  transition={{ duration: 0.16, ease: [0.22, 1, 0.36, 1] }}
                  className="z-50 w-[232px] origin-[var(--radix-popover-content-transform-origin)] overflow-hidden rounded-xl border border-line bg-surface shadow-pop"
                >
                  <div className="border-b border-line px-3.5 py-3">
                    <p className="truncate text-sm font-semibold">{user.fullName}</p>
                    <p className="truncate text-xs text-muted">@{user.username}</p>
                  </div>
                  <div ref={listRef} role="menu" aria-label="Account" onKeyDown={onKeyDown} className="p-1">
                    <button
                      role="menuitem"
                      onClick={() => {
                        setOpen(false);
                        setChangingPassword(true);
                      }}
                      className={cn(item, "text-foreground hover:bg-surface-muted focus-visible:bg-surface-muted")}
                    >
                      <KeyRound className="size-4 text-muted" />
                      Change password
                    </button>
                    <button
                      role="menuitem"
                      onClick={() => {
                        setOpen(false);
                        router.push("/capture-guide");
                      }}
                      className={cn(item, "text-foreground hover:bg-surface-muted focus-visible:bg-surface-muted")}
                    >
                      <PlayCircle className="size-4 text-muted" />
                      GrowDesk Capture
                    </button>
                    <div className="mx-2 my-1 h-px bg-line" role="separator" />
                    <button
                      role="menuitemcheckbox"
                      aria-checked={sound}
                      onClick={() => {
                        setSoundEnabled(!sound);
                        setSound(!sound);
                        if (!sound) playChime();
                      }}
                      className={cn(item, "text-foreground hover:bg-surface-muted focus-visible:bg-surface-muted")}
                    >
                      {sound ? <Volume2 className="size-4 text-muted" /> : <VolumeX className="size-4 text-muted" />}
                      <span className="flex-1 text-left">Alert sound</span>
                      <span className={cn("text-xs font-semibold", sound ? "text-brand" : "text-muted")}>{sound ? "On" : "Off"}</span>
                    </button>
                    {alerts !== "unsupported" && (
                      <button
                        role="menuitem"
                        disabled={alerts !== "default"}
                        onClick={() => void requestDesktopAlerts().then(setAlerts)}
                        className={cn(item, "text-foreground hover:bg-surface-muted focus-visible:bg-surface-muted disabled:hover:bg-transparent")}
                      >
                        <BellRing className="size-4 text-muted" />
                        <span className="flex-1 text-left">Desktop alerts</span>
                        <span className={cn("text-xs font-semibold", alerts === "granted" ? "text-brand" : "text-muted")}>
                          {alerts === "granted" ? "On" : alerts === "denied" ? "Blocked" : "Turn on"}
                        </span>
                      </button>
                    )}
                    <div className="mx-2 my-1 h-px bg-line" role="separator" />
                    <button
                      role="menuitem"
                      onClick={() => logout.mutate()}
                      disabled={logout.isPending}
                      className={cn(item, "text-danger hover:bg-red-50 focus-visible:bg-red-50 disabled:opacity-60")}
                    >
                      <LogOut className="size-4" />
                      {logout.isPending ? "Signing out…" : "Sign out"}
                    </button>
                  </div>
                </motion.div>
              </Popover.Content>
            </Popover.Portal>
          )}
        </AnimatePresence>
      </Popover.Root>

      <ChangePasswordDrawer open={changingPassword} onClose={() => setChangingPassword(false)} />
    </>
  );
}
