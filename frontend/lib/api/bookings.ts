"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api/client";
import type { BookingHours } from "@/types/admin";
import type { BookingDetail, BookingListItem, BookingQuery, CalendarBlock, CreateCalendarBlock, Locale } from "@/types/bookings";
import type { CustomerListItem, NamedRef, Paged } from "@/types/customers";

function qs(query: object) {
  const p = new URLSearchParams();
  for (const [k, v] of Object.entries(query)) if (v !== undefined && v !== null && v !== "") p.set(k, String(v));
  return p.toString();
}

export function useBookings(query: BookingQuery, enabled = true) {
  return useQuery({
    queryKey: ["bookings", "list", query],
    queryFn: ({ signal }) => api.get<Paged<BookingListItem>>(`/bookings?${qs(query)}`, { signal }),
    placeholderData: keepPreviousData,
    enabled,
  });
}

export function useBooking(id: number | null) {
  return useQuery({
    queryKey: ["bookings", "detail", id],
    queryFn: ({ signal }) => api.get<BookingDetail>(`/bookings/${id}`, { signal }),
    enabled: id !== null,
  });
}

export function useDoctorOptions() {
  return useQuery({
    queryKey: ["doctor-options"],
    queryFn: ({ signal }) => api.get<NamedRef[]>("/doctor-options", { signal }),
    staleTime: 5 * 60_000,
  });
}

export function useLocale() {
  return useQuery({
    queryKey: ["locale"],
    queryFn: ({ signal }) => api.get<Locale>("/settings/locale", { signal }),
    staleTime: 10 * 60_000,
  });
}

/** The clinic's weekly opening hours, for shading closed time on the calendar. */
export function useOpeningHours() {
  return useQuery({
    queryKey: ["opening-hours"],
    queryFn: ({ signal }) => api.get<BookingHours>("/calendar/opening-hours", { signal }),
    staleTime: 5 * 60_000,
  });
}

/** Time marked as not available between two dates (inclusive). */
export function useCalendarBlocks(from: string | undefined, to: string | undefined) {
  return useQuery({
    queryKey: ["calendar-blocks", from, to],
    queryFn: ({ signal }) => api.get<CalendarBlock[]>(`/calendar/blocks?${qs({ from, to })}`, { signal }),
    enabled: !!from && !!to,
    placeholderData: keepPreviousData,
  });
}

export function useCalendarBlockMutations() {
  const qc = useQueryClient();
  const refresh = () => void qc.invalidateQueries({ queryKey: ["calendar-blocks"] });
  return {
    create: useMutation({
      mutationFn: (input: CreateCalendarBlock) =>
        api.post<{ block: CalendarBlock; bookedConsultations: number }>("/calendar/blocks", input),
      onSuccess: refresh,
    }),
    remove: useMutation({ mutationFn: (id: number) => api.delete<null>(`/calendar/blocks/${id}`), onSuccess: refresh }),
  };
}

/** Customer autocomplete for the booking form. */
export function useCustomerSearch(term: string) {
  return useQuery({
    queryKey: ["customers", "search", term],
    queryFn: ({ signal }) =>
      api.get<Paged<CustomerListItem>>(`/customers?${qs({ search: term, pageSize: 8 })}`, { signal }),
    enabled: term.trim().length >= 2,
    placeholderData: keepPreviousData,
  });
}

export interface CreateBooking {
  customerId: number;
  doctorId: number | null;
  date: string;
  startTime: string;
  endTime: string;
  treatmentIds: number[];
  notes: string | null;
}

export interface CompleteBooking {
  consultationCharge: number;
  /** Paid now: 0 up to the charge. The rest is the balance. */
  paidAmount: number;
  paymentMethodId: number | null;
  nextTreatmentDate: string | null;
  nextTreatmentId: number | null;
  doctorNotes: string | null;
  /** Only when the booking has no treatment yet. */
  treatmentIds?: number[] | null;
}

/** Every booking action refreshes the calendar, lists and the affected customer. */
export function useBookingActions() {
  const qc = useQueryClient();
  const refresh = (b: BookingDetail) => {
    qc.setQueryData(["bookings", "detail", b.id], b);
    void qc.invalidateQueries({ queryKey: ["bookings"] });
    void qc.invalidateQueries({ queryKey: ["customers"] });
  };
  return {
    create: useMutation({ mutationFn: (input: CreateBooking) => api.post<BookingDetail>("/bookings", input), onSuccess: refresh }),
    update: useMutation({
      mutationFn: ({ id, ...input }: { id: number; doctorId: number | null; treatmentIds: number[]; notes: string | null }) =>
        api.put<BookingDetail>(`/bookings/${id}`, input),
      onSuccess: refresh,
    }),
    complete: useMutation({
      mutationFn: ({ id, ...input }: CompleteBooking & { id: number }) => api.post<BookingDetail>(`/bookings/${id}/complete`, input),
      onSuccess: refresh,
    }),
    reschedule: useMutation({
      mutationFn: ({ id, ...input }: { id: number; date: string; startTime: string; endTime: string; doctorId: number | null }) =>
        api.post<BookingDetail>(`/bookings/${id}/reschedule`, input),
      onSuccess: refresh,
    }),
    cancel: useMutation({
      mutationFn: ({ id, ...input }: { id: number; cancellationReasonId: number; note: string | null }) =>
        api.post<BookingDetail>(`/bookings/${id}/cancel`, input),
      onSuccess: refresh,
    }),
    noShow: useMutation({
      mutationFn: (id: number) => api.post<BookingDetail>(`/bookings/${id}/no-show`, {}),
      onSuccess: refresh,
    }),
  };
}
