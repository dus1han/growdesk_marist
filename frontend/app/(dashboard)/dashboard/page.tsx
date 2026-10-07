"use client";

import { CircleAlert, RotateCcw } from "lucide-react";
import { useState } from "react";
import { BookingDetailsDrawer } from "@/components/bookings/booking-details-drawer";
import { SubscriptionBanner } from "@/components/billing/subscription-banner";
import { BookingFormDrawer } from "@/components/bookings/booking-form-drawer";
import { FollowUps, RecentActivity, StageSummary, TodaysAppointments } from "@/components/dashboard/dashboard-sections";
import { StatCards } from "@/components/dashboard/stat-cards";
import { PageHeader } from "@/components/layout/page-header";
import { RequirePermission } from "@/components/layout/require-permission";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { useDashboard } from "@/lib/api/dashboard";
import { useSession } from "@/lib/auth/session";
import { can, Permission } from "@/lib/permissions";

function greeting() {
  const h = new Date().getHours();
  return h < 12 ? "Good morning" : h < 17 ? "Good afternoon" : "Good evening";
}

/** "What needs my attention today?" (spec §10). Every number comes from the API. */
export default function DashboardPage() {
  const { data: session } = useSession();
  const { data, isPending, isError, refetch } = useDashboard();
  const [openBooking, setOpenBooking] = useState<number | null>(null);
  const [booking, setBooking] = useState(false);
  const firstName = session?.user.fullName.split(" ")[0] ?? "";
  const canBook = can(session?.user, Permission.BookingsManage);

  // A section is shown while loading, then only if this user may see it (the API sends null otherwise).
  const show = (section: unknown) => isPending || section !== null;

  return (
    <RequirePermission permission={Permission.DashboardView}>
      <PageHeader
        title={`${greeting()}, ${firstName}`}
        description={new Date().toLocaleDateString(undefined, { weekday: "long", day: "numeric", month: "long" })}
      />

      <SubscriptionBanner />

      {isError ? (
        <Card>
          <EmptyState
            icon={CircleAlert}
            title="Couldn't load the dashboard"
            description="Check your connection and try again."
            action={
              <Button variant="secondary" onClick={() => refetch()}>
                <RotateCcw className="size-4" /> Try again
              </Button>
            }
          />
        </Card>
      ) : (
        <>
          <StatCards data={data} />

          {/* Two independent columns, so a long list on one side never leaves a gap on the other. */}
          <div className="mt-6 grid items-start gap-6 xl:grid-cols-3 [&>*]:min-w-0">
            <div className="space-y-6 xl:col-span-2">
              {show(data?.todaysAppointments) && (
                <TodaysAppointments
                  items={data?.todaysAppointments ?? undefined}
                  loading={isPending}
                  onOpen={setOpenBooking}
                  onBook={canBook ? () => setBooking(true) : undefined}
                />
              )}
              {show(data?.followUps) && <FollowUps items={data?.followUps ?? undefined} loading={isPending} />}
            </div>
            <div className="space-y-6">
              {show(data?.stages) && <StageSummary stages={data?.stages ?? undefined} loading={isPending} />}
              {show(data?.activity) && (
                <RecentActivity items={data?.activity ?? undefined} loading={isPending} onOpenBooking={setOpenBooking} />
              )}
            </div>
          </div>
        </>
      )}

      <BookingDetailsDrawer bookingId={openBooking} onClose={() => setOpenBooking(null)} onBookingChange={setOpenBooking} />
      {canBook && (
        <BookingFormDrawer
          open={booking}
          onClose={() => setBooking(false)}
          prefill={data ? { date: data.today } : undefined}
          onSaved={() => setBooking(false)}
        />
      )}
    </RequirePermission>
  );
}
