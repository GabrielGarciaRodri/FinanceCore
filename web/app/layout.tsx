import type { Metadata } from "next";
import { Providers } from "@/lib/providers";
import { Toaster } from "@/components/ui/sonner";
import { DemoBanner } from "@/components/layout/demo-banner";
import "./globals.css";

export const metadata: Metadata = {
  title: "FinanceCore",
  description: "FinanceCore — financial reconciliation and analysis.",
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}): JSX.Element {
  return (
    <html lang="en" suppressHydrationWarning>
      <body className="min-h-screen bg-background antialiased">
        {/* Fuera de Providers a propósito: visible incluso durante el splash
            del cold start y en el login. */}
        <DemoBanner />
        <Providers>{children}</Providers>
        <Toaster />
      </body>
    </html>
  );
}
