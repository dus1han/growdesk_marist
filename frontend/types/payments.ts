/** Mirrors backend DTOs/PaymentDtos.cs. */
import type { PaymentStatus } from "./bookings";
import type { NamedRef } from "./customers";

export interface PaymentListItem {
  id: number;
  customer: NamedRef;
  booking: { id: number; date: string; startTime: string; treatments: string[] };
  amount: number;
  status: PaymentStatus;
  method: NamedRef | null;
  paymentDate: string | null;
  createdAt: string;
  recordedBy: string | null;
  /** What the consultation still owes now. */
  bookingBalance: number;
}

/** A completed consultation that still has a balance. */
export interface OutstandingItem {
  bookingId: number;
  customer: NamedRef;
  date: string;
  startTime: string;
  treatments: string[];
  charge: number;
  paid: number;
  balance: number;
}

export interface PaymentSummary {
  collected: number;
  collectedCount: number;
  outstanding: number;
  outstandingCount: number;
  waived: number;
  waivedCount: number;
  currency: string;
}

export interface PaymentQuery {
  from?: string;
  to?: string;
  status?: string;
  paymentMethodId?: number;
  customerId?: number;
  search?: string;
  page?: number;
  pageSize?: number;
}
