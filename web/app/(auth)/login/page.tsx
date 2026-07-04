"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { ApiWakeGate } from "@/components/layout/api-wake-gate";
import { LoginForm } from "@/components/auth/login-form";
import { ThemeToggle } from "@/components/layout/theme-toggle";
import { useAuth } from "@/lib/auth/context";

export default function LoginPage(): JSX.Element {
  const router = useRouter();
  const { isAuthenticated, isLoading } = useAuth();

  useEffect(() => {
    if (!isLoading && isAuthenticated) {
      router.replace("/dashboard");
    }
  }, [isAuthenticated, isLoading, router]);

  return (
    <ApiWakeGate>
      <main className="relative flex min-h-screen items-center justify-center bg-muted/30 p-4">
        <div className="absolute right-4 top-4">
          <ThemeToggle />
        </div>
        <LoginForm />
      </main>
    </ApiWakeGate>
  );
}
