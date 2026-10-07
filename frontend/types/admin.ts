/** Mirrors backend DTOs/AdminDtos.cs. */

export interface LookupItem {
  id: number;
  name: string;
  description: string | null;
  color: string | null;
  systemKey: string | null;
  isActive: boolean;
  displayOrder: number;
}

export interface SaveLookupItem {
  name: string;
  description?: string | null;
  color?: string | null;
}

export interface Role {
  id: number;
  name: string;
  description: string | null;
}

export interface AdminUser {
  id: number;
  fullName: string;
  username: string;
  email: string | null;
  roleId: number | null;
  roleName: string | null;
  isActive: boolean;
  mustChangePassword: boolean;
  lastLoginAt: string | null;
  createdAt: string;
  /** Runs the GrowDesk platform: may configure Stripe. Only another owner can change this account. */
  isPlatformOwner: boolean;
}

export interface CreateUser {
  fullName: string;
  username: string;
  email: string | null;
  roleId: number;
  password: string;
}

export type UpdateUser = Omit<CreateUser, "password">;

export const CUSTOM_FIELD_TYPES = [
  "Text",
  "Textarea",
  "Number",
  "Phone",
  "Email",
  "Date",
  "Dropdown",
  "MultiSelect",
  "Boolean",
] as const;
export type CustomFieldType = (typeof CUSTOM_FIELD_TYPES)[number];

export interface CustomFieldOption {
  id: number;
  label: string;
  displayOrder: number;
  isActive: boolean;
}

export interface CustomField {
  id: number;
  key: string;
  label: string;
  fieldType: CustomFieldType;
  isRequired: boolean;
  isActive: boolean;
  displayOrder: number;
  options: CustomFieldOption[];
}

export interface SaveCustomField {
  label: string;
  fieldType: CustomFieldType;
  isRequired: boolean;
  options: { id: number | null; label: string }[] | null;
}

export interface CaptureField {
  key: string;
  label: string;
  type: string;
  isCustom: boolean;
  isEnabled: boolean;
  isRequired: boolean;
  displayOrder: number;
}

export type ConnectionKind = "Toolbar" | "Bot";

/** A connection the Capture toolbar or the WhatsApp BOT signs in with (backend DTOs/CaptureDtos.cs). */
export interface CaptureClient {
  id: number;
  name: string;
  clientId: string;
  isActive: boolean;
  createdAt: string;
  createdBy: string | null;
  lastUsedAt: string | null;
  revokedAt: string | null;
  /** The toolbar version this PC last connected with. */
  extensionVersion: string | null;
  kind: ConnectionKind;
}

/** One weekday's opening hours ("HH:mm"), backend DTOs/BotDtos.cs. */
export interface OpeningDay {
  day: string;
  isOpen: boolean;
  from: string | null;
  to: string | null;
}

/** When the WhatsApp BOT may book, and how long its bookings are. */
export interface BookingHours {
  botBookingMinutes: number;
  days: OpeningDay[];
}

/** Returned once on creation: the secret can't be read again. */
export interface CaptureClientCreated {
  client: CaptureClient;
  clientSecret: string;
}
