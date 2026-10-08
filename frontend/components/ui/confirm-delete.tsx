"use client";

import { AnimatePresence, motion } from "framer-motion";
import { Loader2, Trash2 } from "lucide-react";
import { useState } from "react";
import { cn } from "@/lib/utils";

/**
 * A delete control that asks first, in place: the trash icon (or a labelled button) turns into
 * "Delete? Yes / No". Deleted records go to the recycle bin, so the question stays light.
 */
export function ConfirmDelete({
  onConfirm,
  label,
  what,
  pending,
  className,
}: {
  onConfirm: () => void;
  /** Shown on the button; omitted: an icon button. */
  label?: string;
  /** For screen readers and the question, e.g. "this payment". */
  what: string;
  pending?: boolean;
  className?: string;
}) {
  const [asking, setAsking] = useState(false);

  return (
    <span className={cn("inline-flex items-center", className)}>
      <AnimatePresence initial={false} mode="wait">
        {asking ? (
          <motion.span
            key="ask"
            initial={{ opacity: 0, x: 6 }}
            animate={{ opacity: 1, x: 0 }}
            exit={{ opacity: 0, x: 6 }}
            transition={{ duration: 0.15 }}
            className="inline-flex items-center gap-1.5 whitespace-nowrap text-xs"
            role="group"
            aria-label={`Delete ${what}?`}
          >
            <span className="font-medium text-danger">Delete?</span>
            <button
              type="button"
              disabled={pending}
              onClick={(e) => {
                e.stopPropagation();
                // The result (done, or why not yet) arrives as a toast.
                setAsking(false);
                onConfirm();
              }}
              className="inline-flex h-7 items-center gap-1 rounded-lg bg-danger px-2.5 font-semibold text-white transition-colors hover:bg-danger/90 disabled:opacity-60"
            >
              {pending && <Loader2 className="size-3 animate-spin" />}
              Yes
            </button>
            <button
              type="button"
              onClick={(e) => {
                e.stopPropagation();
                setAsking(false);
              }}
              className="h-7 rounded-lg px-2 font-semibold text-muted hover:bg-surface-muted hover:text-foreground"
            >
              No
            </button>
          </motion.span>
        ) : label ? (
          <motion.button
            key="label"
            type="button"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            onClick={(e) => {
              e.stopPropagation();
              setAsking(true);
            }}
            className="inline-flex h-8 items-center gap-1.5 rounded-lg px-2.5 text-xs font-semibold text-muted transition-colors hover:bg-red-50 hover:text-danger"
          >
            <Trash2 className="size-3.5" />
            {label}
          </motion.button>
        ) : (
          <motion.button
            key="icon"
            type="button"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            onClick={(e) => {
              e.stopPropagation();
              setAsking(true);
            }}
            aria-label={`Delete ${what}`}
            title="Delete (to the recycle bin)"
            className="flex size-8 items-center justify-center rounded-lg text-muted transition-colors hover:bg-red-50 hover:text-danger"
          >
            <Trash2 className="size-4" />
          </motion.button>
        )}
      </AnimatePresence>
    </span>
  );
}
