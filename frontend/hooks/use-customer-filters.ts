"use client";

import { usePathname, useRouter, useSearchParams, type ReadonlyURLSearchParams } from "next/navigation";
import { useCallback, useMemo } from "react";
import { CREATED_PRESETS, FOLLOW_UP_PRESETS, parseRangeParam } from "@/lib/dates";
import type { ConsultationState, CustomerFilters } from "@/types/customers";

const CONSULTATION_STATES: ConsultationState[] = ["booked", "rescheduled", "consulted", "missed", "cancelled", "none"];

/** URL parameter names: short, readable, shareable (spec §52). */
export type FilterParam = "q" | "stage" | "consultation" | "treatment" | "source" | "assigned" | "created" | "followup" | "owes" | "page";

const num = (v: string | null) => (v && /^\d+$/.test(v) ? Number(v) : undefined);

/** The list filters in a set of URL parameters (the list's own, or a profile's ?list=…). */
export function readCustomerFilters(params: URLSearchParams | ReadonlyURLSearchParams) {
  const values = {
    q: params.get("q") ?? "",
    stage: params.get("stage") ?? undefined,
    consultation: params.get("consultation") ?? undefined,
    treatment: params.get("treatment") ?? undefined,
    source: params.get("source") ?? undefined,
    assigned: params.get("assigned") ?? undefined,
    created: params.get("created") ?? undefined,
    followup: params.get("followup") ?? undefined,
    /** "yes": only customers who still owe money. */
    owes: params.get("owes") === "yes" ? "yes" : undefined,
    page: num(params.get("page")) ?? 1,
  };
  const created = parseRangeParam(values.created, CREATED_PRESETS).range;
  const followUp = parseRangeParam(values.followup, FOLLOW_UP_PRESETS).range;
  const apiFilters: CustomerFilters = {
    search: values.q.trim() || undefined,
    stageId: num(values.stage ?? null),
    consultation: CONSULTATION_STATES.find((s) => s === values.consultation),
    treatmentId: num(values.treatment ?? null),
    leadSourceId: num(values.source ?? null),
    assignedUserId: num(values.assigned ?? null),
    createdFrom: created.from,
    createdTo: created.to,
    followUpFrom: followUp.from,
    followUpTo: followUp.to,
    hasOutstanding: values.owes === "yes" ? true : undefined,
    page: values.page,
  };
  return { values, apiFilters };
}

/**
 * On a customer profile opened from the list: the list's URL parameters travel as ?list=…, so
 * the profile can step through the same filtered list and go back to it.
 */
export function useListContext() {
  const list = useSearchParams().get("list") ?? "";
  return useMemo(() => {
    const { apiFilters } = readCustomerFilters(new URLSearchParams(list));
    return {
      filters: { ...apiFilters, page: undefined },
      listHref: list ? `/customers?${list}` : "/customers",
      profileHref: (id: number) => (list ? `/customers/${id}?list=${encodeURIComponent(list)}` : `/customers/${id}`),
    };
  }, [list]);
}

/**
 * Customer list filters live in the URL, so a filtered view can be bookmarked or shared
 * (e.g. /customers?stage=1&consultation=missed). `stage` is the Status filter (its URL name is kept so old links work). Changing any filter resets to page 1.
 */
export function useCustomerFilters() {
  const params = useSearchParams();
  const router = useRouter();
  const pathname = usePathname();

  const { values, apiFilters } = useMemo(() => readCustomerFilters(params), [params]);

  const set = useCallback(
    (key: FilterParam, value: string | number | undefined) => {
      const next = new URLSearchParams(params.toString());
      if (value === undefined || value === "" || (key === "page" && value === 1)) next.delete(key);
      else next.set(key, String(value));
      if (key !== "page") next.delete("page");
      const qs = next.toString();
      router.replace(qs ? `${pathname}?${qs}` : pathname, { scroll: false });
    },
    [params, pathname, router],
  );

  const clearAll = useCallback(() => router.replace(pathname, { scroll: false }), [pathname, router]);

  const activeCount = ["stage", "consultation", "treatment", "source", "assigned", "created", "followup", "owes"].filter(
    (k) => values[k as keyof typeof values] !== undefined,
  ).length;

  return { values, apiFilters, set, clearAll, activeCount };
}
