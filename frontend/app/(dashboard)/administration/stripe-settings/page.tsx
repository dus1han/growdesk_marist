"use client";

import { AnimatePresence, motion } from "framer-motion";
import { Check, ChevronDown, CircleAlert, Copy, ExternalLink, Loader2, Save, ShieldCheck } from "lucide-react";
import { useEffect, useState } from "react";
import { toast } from "sonner";
import { PageHeader } from "@/components/layout/page-header";
import { RequirePermission } from "@/components/layout/require-permission";
import { Button } from "@/components/ui/button";
import { Card, CardHeader } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { Badge, Field, Input, Switch } from "@/components/ui/form-controls";
import { Skeleton } from "@/components/ui/skeleton";
import { toastError } from "@/lib/api/admin";
import { formatStripeAmount, useBillingSettings, useSaveBillingSettings } from "@/lib/api/billing";
import { ApiError } from "@/lib/api/client";
import { copyText } from "@/lib/clipboard";
import { Permission } from "@/lib/permissions";
import { cn } from "@/lib/utils";
import type { BillingSettings, SaveBillingSettings } from "@/types/billing";

/** Administration → Stripe Settings: platform owners connect Stripe and set the plan and grace period. */
export default function StripeSettingsPage() {
  const { data: settings, isPending, isError, refetch } = useBillingSettings();

  return (
    <RequirePermission permission={Permission.PlatformBilling}>
      <PageHeader
        title="Stripe Settings"
        description="Connect Stripe to charge this clinic's GrowDesk subscription automatically. Only platform owners can see this page."
      />

      {isPending ? (
        <div className="space-y-6" aria-busy="true" aria-label="Loading">
          <Skeleton className="h-24 rounded-2xl" />
          <Skeleton className="h-96 rounded-2xl" />
        </div>
      ) : isError || !settings ? (
        <Card>
          <EmptyState
            icon={CircleAlert}
            title="Couldn't load the Stripe settings"
            description="Please try again."
            action={<Button variant="secondary" onClick={() => void refetch()}>Try again</Button>}
          />
        </Card>
      ) : (
        <>
          <ConnectionStatus settings={settings} />
          <SettingsForm key={settings.updatedAt ?? "new"} settings={settings} />
          <SetupGuide settings={settings} />
        </>
      )}
    </RequirePermission>
  );
}

// ---- Status --------------------------------------------------------------------------------------

function ConnectionStatus({ settings: s }: { settings: BillingSettings }) {
  const plan = s.plan;
  const ok = s.enabled && plan !== null;
  return (
    <motion.div initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.3 }}>
      <Card className="mb-6 flex flex-wrap items-center gap-4 p-5">
        <span
          className={cn(
            "flex size-11 shrink-0 items-center justify-center rounded-xl",
            ok ? "bg-emerald-50 text-emerald-600" : s.planError || s.secretKeyUnreadable ? "bg-red-50 text-red-600" : "bg-surface-muted text-muted",
          )}
        >
          {ok ? <ShieldCheck className="size-5" /> : <CircleAlert className="size-5" />}
        </span>
        <div className="min-w-0 flex-1">
          <p className="flex flex-wrap items-center gap-2 font-display text-[15px] font-semibold">
            {s.enabled ? "Billing is on" : "Billing is off"}
            {s.mode && <Badge tone={s.mode === "live" ? "success" : "warning"}>{s.mode === "live" ? "Live mode" : "Test mode"}</Badge>}
          </p>
          <p className="mt-0.5 text-sm text-muted">
            {s.secretKeyUnreadable
              ? "The saved secret key can't be read on this server any more. Enter it again."
              : s.planError
                ? s.planError
                : plan
                  ? `${plan.name ?? "Plan"} · ${formatStripeAmount(plan.amount, plan.currency)} every ${plan.intervalCount === 1 ? plan.interval : `${plan.intervalCount} ${plan.interval}s`}`
                  : "Not connected yet. Follow the guide below, then paste your keys here."}
            {!s.enabled && s.hasSecretKey && !s.planError && " Nobody is reminded or blocked while billing is off."}
          </p>
        </div>
      </Card>
    </motion.div>
  );
}

// ---- Form ----------------------------------------------------------------------------------------

function SettingsForm({ settings }: { settings: BillingSettings }) {
  const save = useSaveBillingSettings();
  const [draft, setDraft] = useState<SaveBillingSettings>(() => ({
    enabled: settings.enabled,
    secretKey: "",
    webhookSecret: "",
    removeWebhookSecret: false,
    priceId: settings.priceId ?? "",
    graceDays: settings.graceDays,
    firstPaymentDue: settings.firstPaymentDue,
  }));
  const [errors, setErrors] = useState<Record<string, string>>({});
  useEffect(() => setErrors({}), [draft]); // eslint-disable-line react-hooks/set-state-in-effect -- clear on edit

  const set = (change: Partial<SaveBillingSettings>) => setDraft((d) => ({ ...d, ...change }));

  const onSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    save.mutate(
      {
        ...draft,
        secretKey: draft.secretKey?.trim() || null,
        webhookSecret: draft.webhookSecret?.trim() || null,
        priceId: draft.priceId?.trim() || null,
        firstPaymentDue: draft.firstPaymentDue || null,
      },
      {
        onSuccess: (saved) =>
          toast.success(saved.enabled ? "Stripe settings saved. Billing is on." : "Stripe settings saved. Billing is off."),
        onError: (error) => {
          if (error instanceof ApiError && error.status === 400) {
            const byField: Record<string, string> = {};
            for (const f of error.fieldErrors) if (f.field) byField[f.field] = f.message;
            if (Object.keys(byField).length > 0) return setErrors(byField);
          }
          toastError(error);
        },
      },
    );
  };

  return (
    <form onSubmit={onSubmit} noValidate>
      <Card className="overflow-hidden">
        <CardHeader title="Connection" description="Keys are checked with Stripe before saving, stored encrypted, and never shown again." />
        <div className="space-y-5 p-5">
          <div className="flex items-start justify-between gap-4 rounded-xl border border-line bg-surface-muted/50 px-4 py-3">
            <div>
              <p className="text-sm font-semibold">Charge for GrowDesk</p>
              <p className="mt-0.5 text-xs text-muted">
                When on, renewals are charged automatically, admins are reminded of missed payments, and GrowDesk is blocked{" "}
                {draft.graceDays} {draft.graceDays === 1 ? "day" : "days"} after a missed payment until it is paid.
              </p>
            </div>
            <Switch checked={draft.enabled} onCheckedChange={(enabled) => set({ enabled })} label="Charge for GrowDesk" />
          </div>

          <div className="grid gap-5 md:grid-cols-2">
            <Field
              label="Secret key"
              required={!settings.hasSecretKey}
              error={errors.secretKey}
              hint={
                settings.hasSecretKey
                  ? `Saved: ${settings.secretKeyHint}. Leave empty to keep it.`
                  : "Developers → API keys in Stripe. Starts with sk_live_ or sk_test_."
              }
            >
              {(p) => (
                <Input
                  {...p}
                  type="password"
                  autoComplete="off"
                  spellCheck={false}
                  placeholder={settings.hasSecretKey ? "••••••••••••••••" : "sk_live_…"}
                  value={draft.secretKey ?? ""}
                  onChange={(e) => set({ secretKey: e.target.value })}
                />
              )}
            </Field>

            <Field label="Price ID" required error={errors.priceId} hint="Your plan's recurring price in Stripe. Starts with price_.">
              {(p) => (
                <Input
                  {...p}
                  spellCheck={false}
                  placeholder="price_…"
                  value={draft.priceId ?? ""}
                  onChange={(e) => set({ priceId: e.target.value })}
                />
              )}
            </Field>

            <Field
              label="Webhook signing secret"
              optional
              error={errors.webhookSecret}
              hint={
                settings.hasWebhookSecret && !draft.removeWebhookSecret
                  ? "Saved. Leave empty to keep it."
                  : "Needs an HTTPS address. Without it, GrowDesk checks Stripe every 30 minutes instead."
              }
            >
              {(p) => (
                <div className="flex gap-2">
                  <Input
                    {...p}
                    type="password"
                    autoComplete="off"
                    spellCheck={false}
                    placeholder={settings.hasWebhookSecret && !draft.removeWebhookSecret ? "••••••••••••••••" : "whsec_…"}
                    value={draft.webhookSecret ?? ""}
                    disabled={draft.removeWebhookSecret}
                    onChange={(e) => set({ webhookSecret: e.target.value })}
                  />
                  {settings.hasWebhookSecret && (
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      className="h-10"
                      onClick={() => set({ removeWebhookSecret: !draft.removeWebhookSecret, webhookSecret: "" })}
                    >
                      {draft.removeWebhookSecret ? "Keep" : "Remove"}
                    </Button>
                  )}
                </div>
              )}
            </Field>

            <Field label="Webhook URL" hint="Paste this into Stripe when adding the webhook endpoint.">
              {(p) => <CopyField {...p} value={settings.webhookUrl ?? "Set APP_URL on the server to show it"} copyable={!!settings.webhookUrl} />}
            </Field>
          </div>
        </div>

        <div className="border-t border-line">
          <CardHeader title="Payment rules" description="What happens when a renewal can't be charged." />
        </div>
        <div className="grid gap-5 p-5 md:grid-cols-2">
          <Field
            label="Grace period (days)"
            error={errors.graceDays}
            hint="Admins see a countdown for this many days. Then the card is charged one last time; if that fails, GrowDesk is blocked until paid."
          >
            {(p) => (
              <Input
                {...p}
                type="number"
                min={0}
                max={30}
                value={draft.graceDays}
                onChange={(e) => set({ graceDays: Number(e.target.value) })}
                className="max-w-32"
              />
            )}
          </Field>

          <Field
            label="First payment due"
            optional
            error={errors.firstPaymentDue}
            hint="Only for a clinic that hasn't subscribed yet: from this date the countdown starts. Empty: reminded, never blocked."
          >
            {(p) => (
              <Input
                {...p}
                type="date"
                value={draft.firstPaymentDue ?? ""}
                onChange={(e) => set({ firstPaymentDue: e.target.value || null })}
                className="max-w-48"
              />
            )}
          </Field>
        </div>

        <div className="flex items-center justify-end gap-3 border-t border-line bg-surface-muted/40 px-5 py-4">
          {save.isPending && <span className="text-xs text-muted">Checking with Stripe…</span>}
          <Button type="submit" disabled={save.isPending}>
            {save.isPending ? <Loader2 className="size-4 animate-spin" /> : <Save className="size-4" />}
            Save settings
          </Button>
        </div>
      </Card>
    </form>
  );
}

function CopyField({ value, copyable, ...props }: { value: string; copyable: boolean; id: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <div className="flex gap-2">
      <Input {...props} readOnly value={value} className={cn("font-mono text-xs", !copyable && "text-muted")} onFocus={(e) => e.target.select()} />
      {copyable && (
        <Button
          type="button"
          variant="secondary"
          size="icon"
          className="size-10 shrink-0"
          aria-label="Copy webhook URL"
          onClick={async (e) => {
            if (await copyText(value, e.currentTarget)) {
              setCopied(true);
              setTimeout(() => setCopied(false), 1500);
            }
          }}
        >
          {copied ? <Check className="size-4 text-success" /> : <Copy className="size-4" />}
        </Button>
      )}
    </div>
  );
}

// ---- Guide ---------------------------------------------------------------------------------------

function StripeLink({ path, children }: { path: string; children: React.ReactNode }) {
  return (
    <a
      href={`https://dashboard.stripe.com/${path}`}
      target="_blank"
      rel="noreferrer"
      className="inline-flex items-center gap-1 font-semibold text-brand-strong underline-offset-4 hover:underline"
    >
      {children}
      <ExternalLink className="size-3" />
    </a>
  );
}

function SetupGuide({ settings }: { settings: BillingSettings }) {
  const [open, setOpen] = useState(!settings.hasSecretKey);

  const steps: { title: string; body: React.ReactNode }[] = [
    {
      title: "Start in test mode",
      body: (
        <>
          Sign in to Stripe and turn on <b>Test mode</b> (top right). Everything below works the same in test mode, with test keys
          (sk_test_…) and test cards, so you can try the whole flow before charging anyone.
        </>
      ),
    },
    {
      title: "Create the plan",
      body: (
        <>
          <StripeLink path="products">Product catalog</StripeLink> → Add product. Name it e.g. &ldquo;GrowDesk subscription&rdquo;,
          choose <b>Recurring</b>, enter the amount and currency (e.g. AED) and the billing period (Monthly), then save. Open the product
          and copy the <b>price ID</b> (price_…) from the Pricing section. Not the product ID (prod_…).
        </>
      ),
    },
    {
      title: "Copy the secret key",
      body: (
        <>
          <StripeLink path="apikeys">Developers → API keys</StripeLink> → Secret key → Reveal, and copy it (sk_test_… or sk_live_…). Never
          use the publishable key (pk_…).
        </>
      ),
    },
    {
      title: "Turn on the customer portal",
      body: (
        <>
          <StripeLink path="settings/billing/portal">Settings → Billing → Customer portal</StripeLink> → Activate. Allow customers to{" "}
          <b>update payment methods</b> and <b>view invoice history</b>. Turn <b>off</b> &ldquo;Cancel subscriptions&rdquo; so the clinic can
          only change its card here, not stop the subscription. Save.
        </>
      ),
    },
    {
      title: "Never cancel on failed payments",
      body: (
        <>
          <StripeLink path="settings/billing/automatic">Settings → Billing → Subscriptions and emails</StripeLink> (shown as Revenue
          recovery → Retries on newer accounts): keep Smart Retries on, and set <b>&ldquo;If all retries for a payment fail&rdquo;</b> to{" "}
          <b>leave the subscription past-due</b> and the invoice <b>past-due</b>. GrowDesk then stays blocked until the invoice is paid,
          and once it is, the subscription carries on with its original billing date. (If Stripe does cancel, Pay now subscribes again on
          the original date.)
        </>
      ),
    },
    {
      title: "Add the webhook (optional, needs HTTPS)",
      body: (
        <>
          <StripeLink path="webhooks">Developers → Webhooks</StripeLink> → Add endpoint. Endpoint URL:{" "}
          <code className="rounded bg-surface-muted px-1.5 py-0.5 text-xs">{settings.webhookUrl ?? "https://your-domain/api/billing/webhook"}</code>.
          Select these events: <span className="text-xs">{settings.webhookEvents.join(", ")}</span>. Save, then copy the{" "}
          <b>Signing secret</b> (whsec_…). Stripe only sends live webhooks to HTTPS addresses; without one, GrowDesk checks Stripe every
          30 minutes, and immediately whenever an admin opens the Subscription page or pays.
        </>
      ),
    },
    {
      title: "Paste, switch on, save",
      body: (
        <>
          Paste the secret key, price ID and (optional) webhook secret above, switch <b>Charge for GrowDesk</b> on, and save. GrowDesk checks
          the key and price with Stripe first. The clinic&apos;s admin then subscribes in <b>Administration → Subscription</b> by entering
          their card on Stripe&apos;s checkout page.
        </>
      ),
    },
    {
      title: "Test, then go live",
      body: (
        <>
          Subscribe with test card <b>4242 4242 4242 4242</b> (any future date, any CVC). To try a failed renewal, use{" "}
          <b>4000 0000 0000 0341</b> (it saves, but every charge is declined) and a{" "}
          test clock (Billing → Subscriptions → Test clocks). When happy, repeat steps 2–6 with Test mode
          off and paste the live keys. Switching from test to live keys starts the clinic&apos;s billing afresh in live mode.
        </>
      ),
    },
  ];

  return (
    <Card className="mt-6 overflow-hidden">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
        className="flex w-full items-center justify-between gap-4 px-5 py-4 text-left transition-colors hover:bg-surface-muted/40"
      >
        <div>
          <h2 className="font-display text-[15px] font-semibold">How to set up Stripe</h2>
          <p className="mt-0.5 text-xs text-muted">Eight steps, about 15 minutes. You need a Stripe account for your business.</p>
        </div>
        <ChevronDown className={cn("size-4 shrink-0 text-muted transition-transform duration-200", open && "rotate-180")} />
      </button>
      <AnimatePresence initial={false}>
        {open && (
          <motion.ol
            initial={{ height: 0, opacity: 0 }}
            animate={{ height: "auto", opacity: 1 }}
            exit={{ height: 0, opacity: 0 }}
            transition={{ duration: 0.25, ease: [0.22, 1, 0.36, 1] }}
            className="overflow-hidden border-t border-line"
          >
            {steps.map((step, i) => (
              <li key={step.title} className="flex gap-4 border-b border-line px-5 py-4 last:border-b-0">
                <span className="flex size-7 shrink-0 items-center justify-center rounded-full bg-brand-soft text-xs font-bold text-brand-strong">
                  {i + 1}
                </span>
                <div className="min-w-0 text-sm">
                  <p className="font-semibold">{step.title}</p>
                  <p className="mt-1 leading-relaxed text-muted">{step.body}</p>
                </div>
              </li>
            ))}
          </motion.ol>
        )}
      </AnimatePresence>
    </Card>
  );
}
