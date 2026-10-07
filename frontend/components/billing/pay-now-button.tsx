"use client";

import { AnimatePresence, motion } from "framer-motion";
import { CreditCard, ExternalLink, Loader2 } from "lucide-react";
import { useState } from "react";
import { toast } from "sonner";
import { Button, type ButtonProps } from "@/components/ui/button";
import { toastError } from "@/lib/api/admin";
import { usePayNow } from "@/lib/api/billing";
import { cn } from "@/lib/utils";

/**
 * Pay now: the API charges the card on file. If it is declined, the admin is offered Stripe's
 * secure invoice page to pay with another card; with no running subscription the API answers
 * with a Checkout page instead, and the browser goes straight there.
 */
export function PayNowButton({
  label = "Pay now",
  className,
  tone = "light",
  ...props
}: { label?: string; tone?: "light" | "dark" } & Omit<ButtonProps, "onClick" | "children">) {
  const pay = usePayNow();
  const [declined, setDeclined] = useState<{ message: string; url: string } | null>(null);
  const [leaving, setLeaving] = useState(false);
  const busy = pay.isPending || leaving;

  const onClick = () => {
    setDeclined(null);
    pay.mutate(undefined, {
      onSuccess: (result) => {
        if (result.paid) {
          toast.success(result.message ?? "Payment received. Thank you!");
        } else if (result.redirectUrl && result.message) {
          setDeclined({ message: result.message, url: result.redirectUrl });
        } else if (result.redirectUrl) {
          setLeaving(true);
          window.location.assign(result.redirectUrl);
        } else {
          toast.error(result.message ?? "The payment did not go through.");
        }
      },
      onError: toastError,
    });
  };

  return (
    <div className={cn("flex flex-col items-stretch gap-3", className)}>
      <Button onClick={onClick} disabled={busy} aria-busy={busy} {...props}>
        {busy ? <Loader2 className="size-4 animate-spin" /> : <CreditCard className="size-4" />}
        {pay.isPending ? "Charging your card…" : leaving ? "Opening secure payment…" : label}
      </Button>

      <AnimatePresence>
        {declined && (
          <motion.div
            initial={{ opacity: 0, y: -6, height: 0 }}
            animate={{ opacity: 1, y: 0, height: "auto" }}
            exit={{ opacity: 0, height: 0 }}
            transition={{ duration: 0.2 }}
            role="alert"
            className={cn(
              "overflow-hidden rounded-xl border px-3.5 py-3 text-left text-sm",
              tone === "dark" ? "border-red-400/30 bg-red-500/10 text-red-100" : "border-red-200 bg-red-50 text-red-800",
            )}
          >
            <p>{declined.message}</p>
            <a
              href={declined.url}
              className={cn(
                "mt-2 inline-flex items-center gap-1.5 font-semibold underline-offset-4 hover:underline",
                tone === "dark" ? "text-white" : "text-red-900",
              )}
            >
              Pay with another card <ExternalLink className="size-3.5" />
            </a>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}
