import { Suspense } from "react";
import { LoginScreen } from "@/components/auth/login-screen";

export default function LoginPage() {
  return (
    // useSearchParams (for ?next=) needs a Suspense boundary.
    <Suspense fallback={<div className="min-h-dvh bg-[#07071a]" />}>
      <LoginScreen />
    </Suspense>
  );
}
