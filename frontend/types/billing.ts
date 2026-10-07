/** Mirrors backend BillingRules.BillingState. */
export type BillingState = "Disabled" | "NotSubscribed" | "Active" | "PaymentDue" | "FinalAttempt" | "Blocked";

export type BillingDueReason = "PaymentFailed" | "NotSubscribed" | "Ended";

/** GET /api/billing/notice: for every signed-in user. Amounts only for users who can pay. */
export interface BillingNotice {
  state: BillingState;
  reason: BillingDueReason | null;
  blocked: boolean;
  daysLeft: number | null;
  dueSince: string | null;
  blockAt: string | null;
  /** In the currency's minor unit (fils, cents). */
  amountDue: number | null;
  currency: string | null;
  canManage: boolean;
}

export interface BillingPlan {
  name: string | null;
  amount: number;
  currency: string;
  interval: string;
  intervalCount: number;
}

export interface BillingOverview {
  notice: BillingNotice;
  subscriptionStatus: string | null;
  cancelAtPeriodEnd: boolean;
  currentPeriodEnd: string | null;
  plan: BillingPlan | null;
  cardBrand: string | null;
  cardLast4: string | null;
  finalRetryAt: string | null;
  finalRetryError: string | null;
  lastSyncedAt: string | null;
}

/** A Stripe invoice: one row of the subscription payment history. */
export interface BillingInvoice {
  id: string;
  number: string | null;
  date: string;
  periodStart: string | null;
  periodEnd: string | null;
  amount: number;
  amountPaid: number;
  currency: string;
  /** Stripe's invoice status: paid, open, uncollectible, void. */
  status: string;
  paidAt: string | null;
  attemptCount: number;
  hostedUrl: string | null;
  pdfUrl: string | null;
}

export interface PayNowResult {
  paid: boolean;
  redirectUrl: string | null;
  message: string | null;
}

/** Administration → Stripe Settings (platform owners). Secrets are never sent back. */
export interface BillingSettings {
  enabled: boolean;
  hasSecretKey: boolean;
  /** e.g. "sk_live_…a1B2". */
  secretKeyHint: string | null;
  mode: "live" | "test" | null;
  /** A key is saved but can't be decrypted (the server's Jwt:Key changed): enter it again. */
  secretKeyUnreadable: boolean;
  hasWebhookSecret: boolean;
  priceId: string | null;
  graceDays: number;
  /** yyyy-MM-dd */
  firstPaymentDue: string | null;
  plan: BillingPlan | null;
  planError: string | null;
  webhookUrl: string | null;
  webhookEvents: string[];
  updatedAt: string | null;
}

/** Leave a secret empty to keep the saved one. */
export interface SaveBillingSettings {
  enabled: boolean;
  secretKey: string | null;
  webhookSecret: string | null;
  removeWebhookSecret: boolean;
  priceId: string | null;
  graceDays: number;
  firstPaymentDue: string | null;
}
