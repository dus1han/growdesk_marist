/** Mirrors backend DTOs/FollowUpDtos.cs. */
import type { NamedRef, StageRef } from "./customers";

export type FollowUpOutcome = "CallBack" | "NoAnswer" | "NotInterested";

export interface FollowUpItem {
  id: number;
  name: string;
  whatsApp: string | null;
  instagram: string | null;
  stage: StageRef;
  treatments: string[];
  leadSource: string | null;
  assignedUser: NamedRef | null;
  /** yyyy-MM-dd; before today means overdue. */
  due: string;
  attempts: number;
  lastContactDate: string | null;
  createdAt: string;
  /** A new enquiry nobody has contacted yet. */
  isNew: boolean;
}

export interface FollowUpQueue {
  today: string;
  maxAttempts: number;
  items: FollowUpItem[];
  /** Next treatments set on completion but never booked. */
  nextTreatments: NextTreatmentItem[];
}

/** bookingId: the completed consultation; book with nextTreatmentOf = bookingId. */
export interface NextTreatmentItem {
  bookingId: number;
  customerId: number;
  customerName: string;
  whatsApp: string | null;
  date: string;
  treatment: NamedRef | null;
  doctor: NamedRef | null;
}

export interface NewEnquiry {
  id: number;
  name: string;
  createdAt: string;
  leadSource: string | null;
  treatment: string | null;
}

export interface FollowUpSummary {
  today: string;
  due: number;
  overdue: number;
  new: number;
  newEnquiries: NewEnquiry[];
  /** Clinic-local "HH:mm". */
  reminderTimes: string[];
}

export interface FollowUpSettings {
  intervalDays: number;
  maxAttempts: number;
  reminderTimes: string[];
}

export interface FollowUpLogged {
  nextFollowUpDate: string | null;
  attempts: number;
  suggestLost: boolean;
  stage: string;
}
