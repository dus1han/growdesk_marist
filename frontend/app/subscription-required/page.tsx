import type { Metadata } from "next";
import { BlockedScreen } from "@/components/billing/blocked-screen";

export const metadata: Metadata = { title: "Subscription payment required" };

/** Outside the app shell on purpose: while blocked, the app's own pages can't load any data. */
export default function SubscriptionRequiredPage() {
  return <BlockedScreen />;
}
