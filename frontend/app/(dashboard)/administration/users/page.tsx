"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { CircleAlert, Eye, EyeOff, KeyRound, Lock, Pencil, Plus, RotateCcw, ShieldCheck, UserCog } from "lucide-react";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { toast } from "sonner";
import { z } from "zod";
import { PageHeader } from "@/components/layout/page-header";
import { RequirePermission } from "@/components/layout/require-permission";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Drawer } from "@/components/ui/drawer";
import { EmptyState } from "@/components/ui/empty-state";
import { Badge, Field, Input, Select, Switch } from "@/components/ui/form-controls";
import { SearchInput } from "@/components/ui/search-input";
import { Skeleton } from "@/components/ui/skeleton";
import { useDebouncedValue } from "@/hooks/use-debounced-value";
import { toastError, useRoles, useUserMutations, useUsers } from "@/lib/api/admin";
import { ApiError } from "@/lib/api/client";
import { isStrongPassword, PASSWORD_MESSAGE } from "@/lib/auth/password";
import { useSession } from "@/lib/auth/session";
import { formatDateTime, formatRelative } from "@/lib/format";
import { can, Permission } from "@/lib/permissions";
import { cn, initials } from "@/lib/utils";
import type { AdminUser } from "@/types/admin";

// Mirrors backend Validators/AdminValidators.cs.
const USERNAME = /^[A-Za-z0-9._-]{3,50}$/;
const usernameMessage = "Use 3–50 letters, numbers, dots, dashes or underscores (no spaces).";
const passwordMessage = PASSWORD_MESSAGE;
const password = z.string().refine(isStrongPassword, passwordMessage);

/** One schema for add and edit; the password is only checked when adding. */
const userSchema = (isNew: boolean) =>
  z
    .object({
      fullName: z.string().trim().min(1, "Please enter the full name.").max(150),
      username: z.string().trim().regex(USERNAME, usernameMessage),
      email: z.union([z.literal(""), z.string().trim().email("Please enter a valid email address.")]),
      roleId: z.number({ error: "Please choose a role." }).int().positive("Please choose a role."),
      password: z.string(),
    })
    .superRefine((v, ctx) => {
      if (isNew && !isStrongPassword(v.password)) ctx.addIssue({ code: "custom", path: ["password"], message: passwordMessage });
    });
type UserForm = z.infer<ReturnType<typeof userSchema>>;

const ROLE_TONES: Record<string, "brand" | "success" | "warning" | "neutral"> = {
  Admin: "brand",
  Doctor: "success",
  Receptionist: "warning",
};

export default function UsersPage() {
  const [search, setSearch] = useState("");
  const debounced = useDebouncedValue(search);
  const { data: users, isPending, isError, refetch, isFetching } = useUsers(debounced.trim());
  const { data: session } = useSession();
  const { setActive } = useUserMutations();
  const [editing, setEditing] = useState<AdminUser | "new" | null>(null);
  const [resetting, setResetting] = useState<AdminUser | null>(null);

  return (
    <RequirePermission permission={Permission.UsersManage}>
      <PageHeader
        title="Users"
        description="Who can sign in, and what they can do."
        actions={
          <Button onClick={() => setEditing("new")}>
            <Plus className="size-4" /> Add user
          </Button>
        }
      />

      <Card>
        <div className="flex items-center gap-3 border-b border-line p-4">
          <SearchInput value={search} onChange={setSearch} placeholder="Search by name, username or email…" className="flex-1 sm:max-w-sm" />
          {isFetching && !isPending && <span className="size-2 animate-pulse rounded-full bg-brand" aria-label="Searching" />}
        </div>

        {isPending ? (
          <div className="divide-y divide-line" aria-busy="true" aria-label="Loading">
            {Array.from({ length: 4 }, (_, i) => (
              <div key={i} className="flex items-center gap-3 p-4">
                <Skeleton className="size-10 rounded-full" />
                <div className="flex-1 space-y-2">
                  <Skeleton className="h-4 w-40" />
                  <Skeleton className="h-3 w-24" />
                </div>
              </div>
            ))}
          </div>
        ) : isError ? (
          <EmptyState
            icon={CircleAlert}
            title="Couldn't load users"
            description="Check your connection and try again."
            action={
              <Button variant="secondary" onClick={() => refetch()}>
                <RotateCcw className="size-4" /> Try again
              </Button>
            }
          />
        ) : users.length === 0 ? (
          <EmptyState
            icon={UserCog}
            title={debounced ? `No users match "${debounced}"` : "No users yet"}
            description={debounced ? "Try a different search." : "Add the people who will use GrowDesk."}
          />
        ) : (
          <ul className="divide-y divide-line">
            {users.map((u) => {
              const isSelf = u.id === session?.user.id;
              // Mirrors the API: only a platform owner can change a platform owner's account.
              const locked = u.isPlatformOwner && !isSelf && !can(session?.user, Permission.PlatformBilling);
              return (
                <li key={u.id} className={cn("flex flex-wrap items-center gap-x-4 gap-y-2 p-4 transition-colors hover:bg-surface-muted/40", !u.isActive && "bg-surface-muted/40")}>
                  <div className="flex min-w-0 flex-1 basis-60 items-center gap-3">
                    <span
                      className={cn(
                        "flex size-10 shrink-0 items-center justify-center rounded-full text-xs font-bold text-white",
                        u.isActive ? "bg-gradient-to-br from-brand to-accent" : "bg-slate-300",
                      )}
                    >
                      {initials(u.fullName)}
                    </span>
                    <div className="min-w-0">
                      <p className={cn("flex items-center gap-2 truncate text-sm font-semibold", !u.isActive && "text-muted")}>
                        {u.fullName}
                        {isSelf && <Badge tone="muted">You</Badge>}
                      </p>
                      <p className="truncate text-xs text-muted">
                        {u.username}
                        {u.email && ` · ${u.email}`}
                      </p>
                    </div>
                  </div>

                  <div className="flex items-center gap-2">
                    {u.roleName && <Badge tone={ROLE_TONES[u.roleName] ?? "neutral"}>{u.roleName}</Badge>}
                    {u.isPlatformOwner && (
                      <Badge tone="brand">
                        <ShieldCheck className="size-3" /> Platform owner
                      </Badge>
                    )}
                    {!u.isActive && <Badge tone="muted">Inactive</Badge>}
                    {u.isActive && u.mustChangePassword && (
                      <Badge tone="warning" className="hidden sm:inline-flex">
                        Temporary password
                      </Badge>
                    )}
                  </div>

                  <p className="w-32 text-xs text-muted" title={u.lastLoginAt ? formatDateTime(u.lastLoginAt) : undefined}>
                    <span className="sm:hidden">Last login: </span>
                    {formatRelative(u.lastLoginAt, "Never signed in")}
                  </p>

                  {locked ? (
                    <p className="ml-auto flex items-center gap-1.5 text-xs text-muted" title="Only a platform owner can change this account.">
                      <Lock className="size-3.5" /> Managed by GrowDesk
                    </p>
                  ) : (
                    <div className="ml-auto flex items-center gap-1">
                      <button
                        type="button"
                        onClick={() => setResetting(u)}
                        aria-label={`Reset password for ${u.fullName}`}
                        title="Reset password"
                        className="flex size-8 items-center justify-center rounded-lg text-muted transition-colors hover:bg-surface-muted hover:text-foreground"
                      >
                        <KeyRound className="size-4" />
                      </button>
                      <button
                        type="button"
                        onClick={() => setEditing(u)}
                        aria-label={`Edit ${u.fullName}`}
                        title="Edit"
                        className="flex size-8 items-center justify-center rounded-lg text-muted transition-colors hover:bg-surface-muted hover:text-foreground"
                      >
                        <Pencil className="size-4" />
                      </button>
                      <span className="ml-1 flex" title={isSelf ? "You can't deactivate your own account." : undefined}>
                        <Switch
                          checked={u.isActive}
                          disabled={isSelf || setActive.isPending}
                          onCheckedChange={(isActive) =>
                            setActive.mutate(
                              { id: u.id, isActive },
                              { onSuccess: () => toast.success(`${u.fullName} ${isActive ? "can sign in again" : "can no longer sign in"}`) },
                            )
                          }
                          label={u.isActive ? `Deactivate ${u.fullName}` : `Activate ${u.fullName}`}
                        />
                      </span>
                    </div>
                  )}
                </li>
              );
            })}
          </ul>
        )}
      </Card>

      <UserDrawer key={editing === "new" ? "new" : editing?.id ?? "edit-closed"} user={editing} onClose={() => setEditing(null)} />
      <ResetPasswordDrawer key={resetting?.id ?? "reset-closed"} user={resetting} onClose={() => setResetting(null)} />
    </RequirePermission>
  );
}

function UserDrawer({ user, onClose }: { user: AdminUser | "new" | null; onClose: () => void }) {
  const existing = user !== "new" ? user : null;
  const { data: roles } = useRoles();
  const { create, update } = useUserMutations();
  const [showPassword, setShowPassword] = useState(false);

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<UserForm>({
    resolver: zodResolver(userSchema(!existing)),
    defaultValues: {
      fullName: existing?.fullName ?? "",
      username: existing?.username ?? "",
      email: existing?.email ?? "",
      roleId: existing?.roleId ?? 0,
      password: "",
    },
  });

  const onSubmit = handleSubmit(async (values) => {
    const payload = { fullName: values.fullName, username: values.username, email: values.email || null, roleId: values.roleId };
    try {
      if (existing) await update.mutateAsync({ id: existing.id, ...payload });
      else await create.mutateAsync({ ...payload, password: values.password });
      toast.success(existing ? `${values.fullName} saved` : `${values.fullName} can now sign in as ${values.username}`);
      onClose();
    } catch (error) {
      if (error instanceof ApiError) {
        for (const fe of error.fieldErrors) {
          if (fe.field && ["fullName", "username", "email", "roleId", "password"].includes(fe.field))
            setError(fe.field as keyof UserForm, { message: fe.message });
        }
      }
      toastError(error);
    }
  });

  return (
    <Drawer
      open={user !== null}
      onOpenChange={(open) => !open && onClose()}
      title={existing ? "Edit user" : "Add user"}
      description={existing ? undefined : "They'll sign in with this username and temporary password, then choose their own password."}
      footer={
        <>
          <Button type="button" variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="user-form" disabled={isSubmitting}>
            {isSubmitting ? "Saving…" : existing ? "Save changes" : "Add user"}
          </Button>
        </>
      }
    >
      <form id="user-form" onSubmit={onSubmit} className="space-y-5" noValidate>
        <Field label="Full name" required error={errors.fullName?.message}>
          {(p) => <Input {...p} autoFocus autoComplete="off" {...register("fullName")} />}
        </Field>
        <Field label="Username" required hint="Used to sign in. Not case-sensitive." error={errors.username?.message}>
          {(p) => <Input {...p} autoComplete="off" autoCapitalize="none" spellCheck={false} {...register("username")} />}
        </Field>
        <Field label="Email" optional error={errors.email?.message}>
          {(p) => <Input {...p} type="email" autoComplete="off" {...register("email")} />}
        </Field>
        <Field label="Role" required hint="Decides what this person can see and do." error={errors.roleId?.message}>
          {(p) => (
            <Select {...p} {...register("roleId", { valueAsNumber: true })}>
              <option value={0} disabled>
                Choose a role
              </option>
              {roles?.map((r) => (
                <option key={r.id} value={r.id}>
                  {r.name}
                </option>
              ))}
            </Select>
          )}
        </Field>
        {!existing && (
          <Field label="Password" required hint="At least 8 characters, including a letter and a number." error={errors.password?.message}>
            {(p) => (
              <div className="relative">
                <Input {...p} type={showPassword ? "text" : "password"} autoComplete="new-password" className="pr-11" {...register("password")} />
                <button
                  type="button"
                  onClick={() => setShowPassword((v) => !v)}
                  aria-label={showPassword ? "Hide password" : "Show password"}
                  className="absolute right-1.5 top-1/2 flex size-8 -translate-y-1/2 items-center justify-center rounded-lg text-muted hover:bg-surface-muted"
                >
                  {showPassword ? <EyeOff className="size-4" /> : <Eye className="size-4" />}
                </button>
              </div>
            )}
          </Field>
        )}
      </form>
    </Drawer>
  );
}

const resetSchema = z
  .object({ newPassword: password, confirm: z.string() })
  .refine((v) => v.newPassword === v.confirm, { path: ["confirm"], message: "The passwords don't match." });

function ResetPasswordDrawer({ user, onClose }: { user: AdminUser | null; onClose: () => void }) {
  const { resetPassword } = useUserMutations();
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<z.infer<typeof resetSchema>>({ resolver: zodResolver(resetSchema), defaultValues: { newPassword: "", confirm: "" } });

  const onSubmit = handleSubmit(async ({ newPassword }) => {
    if (!user) return;
    try {
      await resetPassword.mutateAsync({ id: user.id, newPassword });
      toast.success(`Password reset for ${user.fullName}. They'll choose a new one when they sign in.`);
      onClose();
    } catch (error) {
      toastError(error);
    }
  });

  return (
    <Drawer
      open={user !== null}
      onOpenChange={(open) => !open && onClose()}
      title="Reset password"
      description={user ? `Set a temporary password for ${user.fullName} (${user.username}) and tell them in person or over a secure channel. They'll choose their own when they next sign in.` : undefined}
      footer={
        <>
          <Button type="button" variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="reset-form" disabled={isSubmitting}>
            {isSubmitting ? "Resetting…" : "Reset password"}
          </Button>
        </>
      }
    >
      <form id="reset-form" onSubmit={onSubmit} className="space-y-5" noValidate>
        <Field label="New password" required hint="At least 8 characters, including a letter and a number." error={errors.newPassword?.message}>
          {(p) => <Input {...p} type="password" autoFocus autoComplete="new-password" {...register("newPassword")} />}
        </Field>
        <Field label="Confirm new password" required error={errors.confirm?.message}>
          {(p) => <Input {...p} type="password" autoComplete="new-password" {...register("confirm")} />}
        </Field>
      </form>
    </Drawer>
  );
}
