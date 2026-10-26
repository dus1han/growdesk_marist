"use client";

import { keepPreviousData, useIsMutating, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api/client";
import type { CustomField, LookupItem } from "@/types/admin";
import type { Activity, CustomerDetail, CustomerFilters, CustomerListItem, NamedRef, Paged, SaveCustomer } from "@/types/customers";

export const CUSTOMER_PAGE_SIZE = 20;

// ---- Active lookups for forms and filters ----------------------------------------------------------

type ActiveLookup = "treatments" | "stages" | "lead-sources" | "payment-methods" | "cancellation-reasons";

export function useActiveLookup(resource: ActiveLookup) {
  return useQuery({
    queryKey: ["lookup", resource, "active"],
    queryFn: ({ signal }) => api.get<LookupItem[]>(`/${resource}`, { signal }),
    staleTime: 5 * 60_000,
  });
}

export function useActiveCustomFields() {
  return useQuery({
    queryKey: ["custom-fields", "active"],
    queryFn: ({ signal }) => api.get<CustomField[]>("/custom-fields", { signal }),
    staleTime: 5 * 60_000,
  });
}

export function useUserOptions() {
  return useQuery({
    queryKey: ["user-options"],
    queryFn: ({ signal }) => api.get<NamedRef[]>("/user-options", { signal }),
    staleTime: 5 * 60_000,
  });
}

// ---- Customers --------------------------------------------------------------------------------------

export function toQueryString(f: CustomerFilters) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(f)) {
    if (value !== undefined && value !== null && value !== "") params.set(key, String(value));
  }
  params.set("pageSize", String(CUSTOMER_PAGE_SIZE));
  return params.toString();
}

export function useCustomers(filters: CustomerFilters) {
  return useQuery({
    queryKey: ["customers", "list", filters],
    queryFn: ({ signal }) => api.get<Paged<CustomerListItem>>(`/customers?${toQueryString(filters)}`, { signal }),
    // Keep the current page on screen while the next filter/page loads.
    placeholderData: keepPreviousData,
  });
}

export function useCustomer(id: number) {
  return useQuery({
    queryKey: ["customers", "detail", id],
    queryFn: ({ signal }) => api.get<CustomerDetail>(`/customers/${id}`, { signal }),
    enabled: Number.isFinite(id),
  });
}

export function useCustomerActivity(id: number) {
  return useQuery({
    queryKey: ["customers", "activity", id],
    queryFn: ({ signal }) => api.get<Activity[]>(`/customers/${id}/activity`, { signal }),
    enabled: Number.isFinite(id),
  });
}

const SAVE_CUSTOMER_KEY = ["save-customer"];

/** True while any customer save is in flight (for buttons outside the form). */
export const useIsSavingCustomer = () => useIsMutating({ mutationKey: SAVE_CUSTOMER_KEY }) > 0;

export function useSaveCustomer() {
  const qc = useQueryClient();
  return useMutation({
    mutationKey: SAVE_CUSTOMER_KEY,
    mutationFn: ({ id, ...input }: SaveCustomer & { id?: number }) =>
      id ? api.put<CustomerDetail>(`/customers/${id}`, input) : api.post<CustomerDetail>("/customers", input),
    onSuccess: (customer) => {
      qc.setQueryData(["customers", "detail", customer.id], customer);
      void qc.invalidateQueries({ queryKey: ["customers", "list"] });
      void qc.invalidateQueries({ queryKey: ["customers", "activity", customer.id] });
    },
  });
}

/** The save payload for an existing customer, for quick edits such as changing the stage. */
export function toSavePayload(c: CustomerDetail): SaveCustomer {
  return {
    name: c.name,
    whatsApp: c.whatsApp,
    secondaryPhone: c.secondaryPhone,
    instagram: c.instagram,
    email: c.email,
    stageId: c.stage.id,
    leadSourceId: c.leadSource?.id ?? null,
    assignedUserId: c.assignedUser?.id ?? null,
    treatmentIds: c.treatments.map((t) => t.id),
    lastContactDate: c.lastContactDate,
    nextFollowUpDate: c.nextFollowUpDate,
    notes: c.notes,
    customFields: Object.fromEntries(c.customFields.map((f) => [f.key, f.value])),
  };
}
