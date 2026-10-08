/** Mirrors backend DTOs/CustomerDtos.cs. */

export interface NamedRef {
  id: number;
  name: string;
}

export interface StageRef {
  id: number;
  name: string;
  color: string;
  systemKey: string | null;
}

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export type ConsultationState = "booked" | "rescheduled" | "consulted" | "missed" | "cancelled" | "none";

/** Where the customer is with consultations, worked out from their bookings (backend ConsultationDto). */
export interface Consultation {
  state: ConsultationState;
  bookingId: number | null;
  date: string | null;
  startTime: string | null;
  nextTreatmentDate: string | null;
}

export interface CustomerListItem {
  id: number;
  name: string;
  whatsApp: string | null;
  instagram: string | null;
  stage: StageRef;
  consultation: Consultation;
  treatments: NamedRef[];
  leadSource: string | null;
  assignedUser: string | null;
  nextFollowUpDate: string | null;
  nextBooking: { id: number; date: string; startTime: string } | null;
  createdAt: string;
  /** Still owed on completed consultations; null for users who can't see payments. */
  outstanding: number | null;
}

/** string | number | boolean | number[], depending on the field type. */
export type CustomFieldValue = string | number | boolean | number[] | null;

export interface CustomerCustomField {
  fieldId: number;
  key: string;
  label: string;
  fieldType: string;
  value: CustomFieldValue;
  display: string;
}

export interface CustomerDetail {
  id: number;
  name: string;
  whatsApp: string | null;
  secondaryPhone: string | null;
  instagram: string | null;
  email: string | null;
  stage: StageRef;
  consultation: Consultation;
  leadSource: NamedRef | null;
  assignedUser: NamedRef | null;
  lastContactDate: string | null;
  nextFollowUpDate: string | null;
  notes: string | null;
  isActive: boolean;
  treatments: NamedRef[];
  customFields: CustomerCustomField[];
  createdAt: string;
  updatedAt: string;
  /** Still owed on completed consultations; null for users who can't see payments. */
  outstanding: number | null;
}

export interface SaveCustomer {
  name: string;
  whatsApp: string | null;
  secondaryPhone: string | null;
  instagram: string | null;
  email: string | null;
  stageId: number | null;
  leadSourceId: number | null;
  assignedUserId: number | null;
  treatmentIds: number[];
  lastContactDate: string | null;
  nextFollowUpDate: string | null;
  notes: string | null;
  customFields: Record<string, CustomFieldValue>;
}

export interface Activity {
  id: number;
  action: string;
  userName: string | null;
  createdAt: string;
  details: Record<string, unknown> | null;
}

export interface DuplicateCustomer {
  existingCustomerId: number;
  existingCustomerName: string;
  matchedOn: "whatsApp" | "instagram";
}

export interface CustomerFilters {
  search?: string;
  stageId?: number;
  consultation?: ConsultationState;
  treatmentId?: number;
  leadSourceId?: number;
  assignedUserId?: number;
  createdFrom?: string;
  createdTo?: string;
  followUpFrom?: string;
  followUpTo?: string;
  /** true: only customers who still owe money. */
  hasOutstanding?: boolean;
  page?: number;
}
