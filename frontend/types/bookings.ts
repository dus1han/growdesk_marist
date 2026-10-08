/** Mirrors backend DTOs/BookingDtos.cs. */
import type { NamedRef, StageRef } from "./customers";

export type BookingStatus = "Booked" | "Completed" | "Rescheduled" | "Cancelled" | "NoShow";
/** A payment entry: money received, or a balance written off. ("Pending" only on old records.) */
export type PaymentStatus = "Paid" | "Pending" | "Waived";

/** Where a completed consultation's money stands (backend BookingMoney). */
export type BookingPaymentState = "Paid" | "PartlyPaid" | "Unpaid" | "Waived" | "NoCharge";

export interface BookingListItem {
  id: number;
  customer: NamedRef;
  doctor: NamedRef | null;
  date: string; // yyyy-MM-dd
  startTime: string; // HH:mm:ss
  endTime: string;
  status: BookingStatus;
  treatments: NamedRef[];
  consultationCharge: number | null;
  amountPaid: number;
  /** Still owed; 0 until completed. */
  balance: number;
  paymentStatus: BookingPaymentState | null;
}

export interface Payment {
  id: number;
  amount: number;
  status: PaymentStatus;
  method: NamedRef | null;
  paymentDate: string | null;
  recordedBy: string | null;
  createdAt: string;
}

export interface BookingLink {
  id: number;
  date: string;
  startTime: string;
  status: BookingStatus;
}

export interface BookingDetail {
  id: number;
  customer: NamedRef;
  customerWhatsApp: string | null;
  customerStage: StageRef;
  doctor: NamedRef | null;
  date: string;
  startTime: string;
  endTime: string;
  status: BookingStatus;
  treatments: NamedRef[];
  notes: string | null;
  consultationCharge: number | null;
  amountPaid: number;
  balance: number;
  paymentStatus: BookingPaymentState | null;
  doctorNotes: string | null;
  nextTreatmentDate: string | null;
  nextTreatment: NamedRef | null;
  cancellationReason: NamedRef | null;
  cancellationNote: string | null;
  rescheduledFrom: BookingLink | null;
  rescheduledTo: BookingLink | null;
  payments: Payment[];
  completedAt: string | null;
  cancelledAt: string | null;
  rescheduledAt: string | null;
  noShowAt: string | null;
  createdAt: string;
  /** "WhatsApp BOT" when the bot made the booking; null when made in GrowDesk. */
  source: string | null;
}

export interface BookingQuery {
  from?: string;
  to?: string;
  customerId?: number;
  doctorId?: number;
  status?: string;
  treatmentId?: number;
  paymentStatus?: string;
  search?: string;
  page?: number;
  pageSize?: number;
  sort?: "asc" | "desc";
}

export interface BookingConflict {
  bookingId: number;
  customerName: string;
  startTime: string;
  endTime: string;
}

export interface Locale {
  currency: string;
  timeZone: string;
  today: string;
}

/** Time marked as not available: every day from startDate to endDate, all day when the times are null. */
export interface CalendarBlock {
  id: number;
  startDate: string;
  endDate: string;
  startTime: string | null;
  endTime: string | null;
  reason: string | null;
  createdBy: string | null;
  createdAt: string;
}

export interface CreateCalendarBlock {
  startDate: string;
  endDate: string | null;
  startTime: string | null;
  endTime: string | null;
  reason: string | null;
}
