import type { CurrentUser } from "@/types/api";

/**
 * Permission keys, mirrored from backend Authorization/Permissions.cs.
 * UI decisions check permissions, never role names, so roles can be reshaped without code changes.
 */
export const Permission = {
  DashboardView: "dashboard.view",
  CustomersView: "customers.view",
  CustomersManage: "customers.manage",
  BookingsView: "bookings.view",
  BookingsManage: "bookings.manage",
  BookingsComplete: "bookings.complete",
  PaymentsView: "payments.view",
  PaymentsManage: "payments.manage",
  AdminAccess: "admin.access",
  UsersManage: "admin.users",
  SettingsManage: "admin.settings",
  AuditView: "admin.audit",
  BillingManage: "admin.billing",
  RecordsDelete: "admin.delete",
  /** Platform owners only (never part of a role): Stripe Settings. */
  PlatformBilling: "platform.billing",
} as const;

export type PermissionKey = (typeof Permission)[keyof typeof Permission];

export function can(user: CurrentUser | null | undefined, permission: PermissionKey) {
  return !!user?.permissions.includes(permission);
}
