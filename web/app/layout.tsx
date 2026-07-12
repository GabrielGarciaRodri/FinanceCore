import type { Metadata } from "next";
import { Analytics } from "@vercel/analytics/react";
import { SpeedInsights } from "@vercel/speed-insights/next";
import { Providers } from "@/lib/providers";
import { Toaster } from "@/components/ui/sonner";
import { DemoBanner } from "@/components/layout/demo-banner";
import "./globals.css";

// Base para URLs absolutas de OG images. En Vercel, setear NEXT_PUBLIC_SITE_URL
// con el dominio público; VERCEL_URL cubre los deploys de preview.
const siteUrl =
  process.env.NEXT_PUBLIC_SITE_URL ??
  (process.env.VERCEL_URL ? `https://${process.env.VERCEL_URL}` : "http://localhost:3000");

const description =
  "Sistema de conciliación financiera multi-cuenta y multi-moneda: ingesta de " +
  "transacciones, matching con tolerancias configurables y cierre diario. " +
  "Demo pública con usuario de sólo lectura.";

export const metadata: Metadata = {
  metadataBase: new URL(siteUrl),
  title: {
    default: "FinanceCore — Conciliación financiera",
    template: "%s · FinanceCore",
  },
  description,
  openGraph: {
    type: "website",
    siteName: "FinanceCore",
    title: "FinanceCore — Conciliación financiera multi-cuenta",
    description,
    locale: "es",
  },
  twitter: {
    card: "summary_large_image",
    title: "FinanceCore — Conciliación financiera multi-cuenta",
    description,
  },
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
        {/* Sólo emiten en deployments de Vercel; en dev local son no-op. */}
        <Analytics />
        <SpeedInsights />
      </body>
    </html>
  );
}
