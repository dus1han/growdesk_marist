"use client";

import { motion } from "framer-motion";
import { CircleAlert, Loader2, RotateCcw, Trash2, Undo2 } from "lucide-react";
import { useState } from "react";
import { PageHeader } from "@/components/layout/page-header";
import { RequirePermission } from "@/components/layout/require-permission";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { FilterMenu } from "@/components/ui/filter-menu";
import { Badge } from "@/components/ui/form-controls";
import { Pagination } from "@/components/ui/pagination";
import { SearchInput } from "@/components/ui/search-input";
import { Skeleton } from "@/components/ui/skeleton";
import { useDebouncedValue } from "@/hooks/use-debounced-value";
import { useRecycleBin, useRestoreRecord, type RecycleBinItem } from "@/lib/api/recycle-bin";
import { formatDateTime, formatRelative } from "@/lib/format";
import { Permission } from "@/lib/permissions";
import { cn } from "@/lib/utils";

const PAGE_SIZE = 25;

const TYPES = [
  { value: "customer", label: "Customers" },
  { value: "booking", label: "Bookings" },
  { value: "payment", label: "Payments" },
  { value: "treatment", label: "Treatments" },
  { value: "stage", label: "Statuses" },
  { value: "lead-source", label: "Lead sources" },
  { value: "payment-method", label: "Payment methods" },
  { value: "cancellation-reason", label: "Cancellation reasons" },
];

const TYPE_TONE: Record<string, string> = {
  customer: "bg-brand-soft text-brand-strong",
  booking: "bg-sky-50 text-sky-700",
  payment: "bg-emerald-50 text-emerald-700",
};

export default function RecycleBinPage() {
  const [search, setSearch] = useState("");
  const [type, setType] = useState<string | undefined>();
  const [page, setPage] = useState(1);
  const debounced = useDebouncedValue(search, 300);
  const { data, isPending, isError, isFetching, refetch } = useRecycleBin({
    type,
    search: debounced.trim() || undefined,
    page,
    pageSize: PAGE_SIZE,
  });
  const filtered = !!(type || debounced);

  return (
    <RequirePermission permission={Permission.RecordsDelete}>
      <PageHeader
        title="Recycle Bin"
        description="Deleted records stay here until you restore them. Restore the customer before their bookings, and a booking before its payments."
      />

      <Card className="overflow-hidden">
        <div className="flex flex-wrap items-center gap-2 border-b border-line p-4">
          <SearchInput value={search} onChange={(v) => { setSearch(v); setPage(1); }} placeholder="Search deleted records…" className="flex-1 sm:max-w-sm" />
          <FilterMenu label="Type" value={type} onChange={(v) => { setType(v); setPage(1); }} options={TYPES} />
          {isFetching && !isPending && <span className="size-2 animate-pulse rounded-full bg-brand" aria-label="Loading" />}
        </div>

        {isPending ? (
          <div className="divide-y divide-line" aria-busy="true" aria-label="Loading">
            {Array.from({ length: 5 }, (_, i) => (
              <div key={i} className="flex items-center gap-4 px-4 py-4">
                <Skeleton className="h-5 w-20 rounded-full" />
                <Skeleton className="h-4 w-48" />
                <Skeleton className="ml-auto h-8 w-24 rounded-lg" />
              </div>
            ))}
          </div>
        ) : isError ? (
          <EmptyState
            icon={CircleAlert}
            title="Couldn't load the recycle bin"
            description="Check your connection and try again."
            action={
              <Button variant="secondary" onClick={() => void refetch()}>
                <RotateCcw className="size-4" /> Try again
              </Button>
            }
          />
        ) : data.items.length === 0 ? (
          <EmptyState
            icon={Trash2}
            title={filtered ? "Nothing deleted matches" : "The recycle bin is empty"}
            description={filtered ? "Try another search or type." : "Deleted customers, bookings, payments and list items appear here."}
            className="py-16"
          />
        ) : (
          <div className={cn("transition-opacity", isFetching && "opacity-70")}>
            <ul className="divide-y divide-line">
              {data.items.map((item, i) => (
                <BinRow key={`${item.type}-${item.id}`} item={item} index={i} />
              ))}
            </ul>
            <Pagination page={data.page} pageSize={PAGE_SIZE} totalCount={data.totalCount} onPageChange={setPage} noun="records" />
          </div>
        )}
      </Card>
    </RequirePermission>
  );
}

function BinRow({ item, index }: { item: RecycleBinItem; index: number }) {
  const restore = useRestoreRecord();
  return (
    <motion.li
      layout
      initial={{ opacity: 0, y: 4 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay: Math.min(index, 10) * 0.02 }}
      className="flex flex-wrap items-center gap-x-4 gap-y-2 px-4 py-3.5"
    >
      <span className={cn("w-36 shrink-0", !TYPE_TONE[item.type] && "opacity-90")}>
        <Badge className={TYPE_TONE[item.type]}>{item.typeLabel}</Badge>
      </span>
      <div className="min-w-0 flex-1">
        <p className="truncate font-semibold">{item.title}</p>
        {item.detail && <p className="truncate text-xs text-muted">{item.detail}</p>}
      </div>
      <p className="w-44 text-xs text-muted" title={formatDateTime(item.deletedAt)}>
        Deleted {formatRelative(item.deletedAt)}
        {item.deletedBy && <span className="block truncate">by {item.deletedBy}</span>}
      </p>
      <Button size="sm" variant="secondary" onClick={() => restore.mutate(item)} disabled={restore.isPending}>
        {restore.isPending ? <Loader2 className="size-3.5 animate-spin" /> : <Undo2 className="size-3.5" />}
        Restore
      </Button>
    </motion.li>
  );
}
