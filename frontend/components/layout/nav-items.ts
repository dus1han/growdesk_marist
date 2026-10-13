import { BellRing, CalendarDays, CreditCard, LayoutDashboard, Settings2, Users, type LucideIcon } from "lucide-react";
import { Permission, type PermissionKey } from "@/lib/permissions";

export interface NavItem {
  href: string;
  label: string;
  icon: LucideIcon;
  permission: PermissionKey;
  /** Shown in the mobile bottom bar (max four). */
  mobilePrimary?: boolean;
  /** A live count beside the label. */
  badge?: "followUps";
}

export const mainNav: NavItem[] = [
  { href: "/dashboard", label: "Dashboard", icon: LayoutDashboard, permission: Permission.DashboardView, mobilePrimary: true },
  { href: "/customers", label: "Customers", icon: Users, permission: Permission.CustomersView, mobilePrimary: true },
  { href: "/follow-ups", label: "Follow-ups", icon: BellRing, permission: Permission.CustomersView, mobilePrimary: true, badge: "followUps" },
  { href: "/bookings", label: "Bookings", icon: CalendarDays, permission: Permission.BookingsView, mobilePrimary: true },
  { href: "/payments", label: "Payments", icon: CreditCard, permission: Permission.PaymentsView },
];

export const managementNav: NavItem[] = [
  { href: "/administration", label: "Administration", icon: Settings2, permission: Permission.AdminAccess },
];
