import type { NamedRef } from "./customers";

/** One audit entry (backend AuditLogDto). */
export interface AuditLogEntry {
  id: number;
  createdAt: string;
  userId: number | null;
  /** Null for entries no signed-in user made (failed sign-ins, the WhatsApp BOT, the Capture toolbar). */
  userName: string | null;
  action: string;
  entityType: string;
  entityId: string | null;
  /** The customer's or user's name, where the record still exists. */
  subject: string | null;
  /** Set on customer and booking entries: links to the customer's profile. */
  customerId: number | null;
  details: Record<string, unknown> | unknown[] | string | number | boolean | null;
}

export interface AuditQuery {
  search?: string;
  userId?: number;
  action?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}

export interface AuditFilters {
  actions: string[];
  users: NamedRef[];
}
