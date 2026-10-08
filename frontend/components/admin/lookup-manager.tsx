"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { AnimatePresence, motion } from "framer-motion";
import { Check, CircleAlert, Lock, Pencil, Plus, RotateCcw } from "lucide-react";
import { useMemo, useState } from "react";
import { useForm, useWatch } from "react-hook-form";
import { toast } from "sonner";
import { z } from "zod";
import { PageHeader } from "@/components/layout/page-header";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Drawer } from "@/components/ui/drawer";
import { EmptyState } from "@/components/ui/empty-state";
import { Badge, Field, Input, Switch, Textarea } from "@/components/ui/form-controls";
import { SearchInput } from "@/components/ui/search-input";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError } from "@/lib/api/client";
import { toastError, useLookupAdmin, useLookupMutations, type LookupResource } from "@/lib/api/admin";
import { cn } from "@/lib/utils";
import type { LookupItem } from "@/types/admin";
import type { LucideIcon } from "lucide-react";
import { SortableList } from "./sortable-list";

export const STAGE_COLORS = [
  "#6366F1", "#8B5CF6", "#EC4899", "#EF4444", "#F97316", "#F59E0B",
  "#EAB308", "#22C55E", "#14B8A6", "#0EA5E9", "#3B82F6", "#64748B",
];

interface LookupManagerProps {
  resource: LookupResource;
  title: string;
  description: string;
  /** Lower-case singular for buttons and messages, e.g. "lead source". */
  singular: string;
  icon: LucideIcon;
  withDescription?: boolean;
  withColor?: boolean;
}

const schema = z.object({
  name: z.string().trim().min(1, "Please enter a name.").max(100, "Keep it under 100 characters."),
  description: z.string().max(1000).optional(),
  color: z.string().regex(/^#[0-9A-Fa-f]{6}$/, "Choose a colour in #RRGGBB format.").optional(),
});
type FormValues = z.infer<typeof schema>;

/**
 * One screen for every simple admin list: search, add, edit in a drawer, activate/deactivate and
 * drag-to-reorder. Inactive items stay listed (muted) because they still appear on old records.
 */
export function LookupManager({ resource, title, description, singular, icon, withDescription, withColor }: LookupManagerProps) {
  const { data: items, isPending, isError, refetch } = useLookupAdmin(resource);
  const { setActive, reorder } = useLookupMutations(resource);
  const [search, setSearch] = useState("");
  const [editing, setEditing] = useState<LookupItem | "new" | null>(null);

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return term ? (items ?? []).filter((i) => i.name.toLowerCase().includes(term)) : (items ?? []);
  }, [items, search]);

  const Capitalised = singular.charAt(0).toUpperCase() + singular.slice(1);
  const activeCount = items?.filter((i) => i.isActive).length ?? 0;

  return (
    <>
      <PageHeader
        title={title}
        description={description}
        actions={
          <Button onClick={() => setEditing("new")}>
            <Plus className="size-4" /> Add {singular}
          </Button>
        }
      />

      <Card>
        <div className="flex flex-col gap-3 border-b border-line p-4 sm:flex-row sm:items-center sm:justify-between">
          <SearchInput value={search} onChange={setSearch} placeholder={`Search ${title.toLowerCase()}…`} className="sm:max-w-xs sm:flex-1" />
          {items && (
            <p className="text-xs text-muted">
              {activeCount} active{items.length > activeCount && ` · ${items.length - activeCount} inactive`}
              {!search && items.length > 1 && " · drag to reorder"}
            </p>
          )}
        </div>

        {isPending ? (
          <ListSkeleton />
        ) : isError ? (
          <EmptyState
            icon={CircleAlert}
            title={`Couldn't load ${title.toLowerCase()}`}
            description="Check your connection and try again."
            action={
              <Button variant="secondary" onClick={() => refetch()}>
                <RotateCcw className="size-4" /> Try again
              </Button>
            }
          />
        ) : filtered.length === 0 ? (
          <EmptyState
            icon={icon}
            title={search ? `No ${title.toLowerCase()} match "${search}"` : `No ${title.toLowerCase()} yet`}
            description={search ? "Try a different search." : `Add your first ${singular} to get started.`}
            action={
              !search && (
                <Button onClick={() => setEditing("new")}>
                  <Plus className="size-4" /> Add {singular}
                </Button>
              )
            }
          />
        ) : (
          <SortableList
            items={filtered}
            getId={(i) => i.id}
            disabled={!!search}
            onReorder={(next) => reorder.mutate(next.map((i) => i.id))}
            className="divide-y divide-line"
            renderItem={(item, handle) => (
              <div className={cn("flex items-center gap-2 bg-surface px-2 py-3 transition-colors sm:px-3", !item.isActive && "bg-surface-muted/40")}>
                {handle}
                {withColor && (
                  <span
                    className="size-3.5 shrink-0 rounded-full ring-4 ring-white"
                    style={{ backgroundColor: item.color ?? "#64748B", boxShadow: `0 0 0 1px ${item.color ?? "#64748B"}33` }}
                    aria-hidden
                  />
                )}
                <div className="min-w-0 flex-1 px-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <p className={cn("truncate text-sm font-medium", !item.isActive && "text-muted")}>{item.name}</p>
                    {item.systemKey && (
                      <Badge tone="brand">
                        <Lock className="size-3" /> Built-in
                      </Badge>
                    )}
                    {!item.isActive && <Badge tone="muted">Inactive</Badge>}
                  </div>
                  {item.description && <p className="mt-0.5 truncate text-xs text-muted">{item.description}</p>}
                </div>
                <button
                  type="button"
                  onClick={() => setEditing(item)}
                  aria-label={`Edit ${item.name}`}
                  className="flex size-8 shrink-0 items-center justify-center rounded-lg text-muted transition-colors hover:bg-surface-muted hover:text-foreground"
                >
                  <Pencil className="size-4" />
                </button>
                <span
                  title={item.systemKey ? "Built in, so it can't be deactivated. You can rename it." : undefined}
                  className="flex shrink-0 items-center"
                >
                  <Switch
                    checked={item.isActive}
                    disabled={!!item.systemKey && item.isActive}
                    onCheckedChange={(isActive) =>
                      setActive.mutate(
                        { id: item.id, isActive },
                        { onSuccess: () => toast.success(`${item.name} ${isActive ? "activated" : "deactivated"}`) },
                      )
                    }
                    label={item.isActive ? `Deactivate ${item.name}` : `Activate ${item.name}`}
                  />
                </span>
              </div>
            )}
          />
        )}
      </Card>

      <LookupDrawer
        key={editing === "new" ? "new" : editing?.id ?? "closed"}
        resource={resource}
        item={editing}
        singular={singular}
        Capitalised={Capitalised}
        withDescription={withDescription}
        withColor={withColor}
        onClose={() => setEditing(null)}
      />
    </>
  );
}

function LookupDrawer({
  resource,
  item,
  singular,
  Capitalised,
  withDescription,
  withColor,
  onClose,
}: {
  resource: LookupResource;
  item: LookupItem | "new" | null;
  singular: string;
  Capitalised: string;
  withDescription?: boolean;
  withColor?: boolean;
  onClose: () => void;
}) {
  const { create, update } = useLookupMutations(resource);
  const existing = item !== "new" ? item : null;

  const {
    register,
    handleSubmit,
    setError,
    control,
    setValue,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: existing?.name ?? "",
      description: existing?.description ?? "",
      color: withColor ? (existing?.color ?? STAGE_COLORS[0]) : undefined,
    },
  });

  const color = useWatch({ control, name: "color" });
  const name = useWatch({ control, name: "name" });

  const onSubmit = handleSubmit(async (values) => {
    const payload = {
      name: values.name,
      description: withDescription ? values.description || null : null,
      color: withColor ? values.color : null,
    };
    try {
      if (existing) await update.mutateAsync({ id: existing.id, ...payload });
      else await create.mutateAsync(payload);
      toast.success(existing ? `${values.name} saved` : `${values.name} added`);
      onClose();
    } catch (error) {
      if (error instanceof ApiError) {
        for (const fe of error.fieldErrors) {
          if (fe.field === "name" || fe.field === "description" || fe.field === "color") setError(fe.field, { message: fe.message });
        }
      }
      toastError(error);
    }
  });

  return (
    <Drawer
      open={item !== null}
      onOpenChange={(open) => !open && onClose()}
      title={existing ? `Edit ${singular}` : `Add ${singular}`}
      description={
        existing?.systemKey
          ? existing.color !== null
            ? "A built-in status. You can rename it and change its colour."
            : `A built-in ${singular.toLowerCase()}. You can rename it.`
          : undefined
      }
      footer={
        <>
          <Button type="button" variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="lookup-form" disabled={isSubmitting}>
            {isSubmitting ? "Saving…" : existing ? "Save changes" : `Add ${singular}`}
          </Button>
        </>
      }
    >
      <form id="lookup-form" onSubmit={onSubmit} className="space-y-5" noValidate>
        <Field label={`${Capitalised} name`} required error={errors.name?.message}>
          {(p) => <Input {...p} autoFocus autoComplete="off" {...register("name")} />}
        </Field>

        {withDescription && (
          <Field label="Description" optional error={errors.description?.message}>
            {(p) => <Textarea {...p} rows={3} {...register("description")} />}
          </Field>
        )}

        {withColor && (
          <Field label="Colour" required hint="Used for this status's badges everywhere in the app." error={errors.color?.message}>
            {(p) => (
              <div>
                <div className="grid grid-cols-6 gap-2" role="radiogroup" aria-label="Colour presets">
                  {STAGE_COLORS.map((c) => (
                    <button
                      key={c}
                      type="button"
                      role="radio"
                      aria-checked={color?.toUpperCase() === c}
                      aria-label={c}
                      onClick={() => setValue("color", c, { shouldValidate: true, shouldDirty: true })}
                      className="relative flex aspect-square items-center justify-center rounded-xl transition-transform hover:scale-105"
                      style={{ backgroundColor: c }}
                    >
                      <AnimatePresence>
                        {color?.toUpperCase() === c && (
                          <motion.span initial={{ scale: 0 }} animate={{ scale: 1 }} exit={{ scale: 0 }}>
                            <Check className="size-4 text-white" strokeWidth={3} />
                          </motion.span>
                        )}
                      </AnimatePresence>
                    </button>
                  ))}
                </div>
                <div className="mt-3 flex items-center gap-2">
                  <span className="size-10 shrink-0 rounded-xl border border-line" style={{ backgroundColor: color }} aria-hidden />
                  <Input {...p} placeholder="#6366F1" className="font-mono uppercase" {...register("color")} />
                </div>
                {color && /^#[0-9A-Fa-f]{6}$/.test(color) && (
                  <div className="mt-3 flex items-center gap-2 text-xs text-muted">
                    Preview:
                    <span
                      className="inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-semibold"
                      style={{ backgroundColor: `${color}1A`, color }}
                    >
                      <span className="size-1.5 rounded-full" style={{ backgroundColor: color }} />
                      {name || "Status name"}
                    </span>
                  </div>
                )}
              </div>
            )}
          </Field>
        )}
      </form>
    </Drawer>
  );
}

function ListSkeleton() {
  return (
    <div className="divide-y divide-line" aria-busy="true" aria-label="Loading">
      {Array.from({ length: 5 }, (_, i) => (
        <div key={i} className="flex items-center gap-3 px-4 py-4">
          <Skeleton className="size-5 rounded-md" />
          <Skeleton className="h-4 flex-1 max-w-[220px]" />
          <Skeleton className="ml-auto h-6 w-11 rounded-full" />
        </div>
      ))}
    </div>
  );
}
