import type { Metadata } from "next";
import { Providers } from "@/lib/providers";
import { Toaster } from "@/components/ui/sonner";
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
        <Providers>{children}</Providers>
        <Toaster />
      </body>
    </html>
  );
}
