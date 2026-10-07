import {
  Ban,
  Bot,
  ClipboardType,
  Layers,
  Megaphone,
  PanelTop,
  ScrollText,
  Sparkles,
  UserCog,
  Wallet,
  type LucideIcon,
} from "lucide-react";
import { Permission, type PermissionKey } from "@/lib/permissions";

export interface AdminSection {
  href: string;
  title: string;
  description: string;
  icon: LucideIcon;
  permission: PermissionKey;
}

export const ADMIN_SECTIONS: AdminSection[] = [
  { href: "/administration/users", title: "Users", description: "Accounts, roles, activation and password resets", icon: UserCog, permission: Permission.UsersManage },
  { href: "/administration/treatments", title: "Treatments", description: "The treatments customers can be interested in", icon: Sparkles, permission: Permission.SettingsManage },
  { href: "/administration/stages", title: "Statuses", description: "Customer statuses, colours and order", icon: Layers, permission: Permission.SettingsManage },
  { href: "/administration/lead-sources", title: "Lead Sources", description: "Where customers come from", icon: Megaphone, permission: Permission.SettingsManage },
  { href: "/administration/custom-fields", title: "Custom Fields", description: "Extra customer fields of any type", icon: ClipboardType, permission: Permission.SettingsManage },
  { href: "/administration/capture-tool", title: "Capture Tool", description: "Fields on the CRM Capture toolbar for WhatsApp and Instagram", icon: PanelTop, permission: Permission.SettingsManage },
  { href: "/administration/whatsapp-bot", title: "WhatsApp BOT", description: "Opening hours and the bot's connection for leads and bookings", icon: Bot, permission: Permission.SettingsManage },
  { href: "/administration/cancellation-reasons", title: "Cancellation Reasons", description: "Reasons offered when cancelling a booking", icon: Ban, permission: Permission.SettingsManage },
  { href: "/administration/payment-methods", title: "Payment Methods", description: "Cash, card, bank transfer and others", icon: Wallet, permission: Permission.SettingsManage },
  { href: "/administration/audit-log", title: "Audit Log", description: "Who did what and when: sign-ins, changes and settings", icon: ScrollText, permission: Permission.AuditView },
];
