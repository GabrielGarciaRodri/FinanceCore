"use client";

import { useEffect } from "react";
import Link from "next/link";
import {
  ArrowRight,
  FileSpreadsheet,
  Github,
  ScrollText,
  ShieldCheck,
} from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { LogoMark } from "@/components/layout/logo-mark";
import { ThemeToggle } from "@/components/layout/theme-toggle";
import { apiBaseUrl } from "@/lib/api/client";
import { useAuth } from "@/lib/auth/context";
import { demoMode } from "@/lib/demo";

const GITHUB_URL = "https://github.com/GabrielGarciaRodri/FinanceCore";

const FEATURES = [
  {
    icon: FileSpreadsheet,
    title: "Ingesta multi-fuente",
    description:
      "Transacciones desde API, CSV y Excel con garantías de idempotencia: reprocesar un archivo no duplica nada.",
  },
  {
    icon: ScrollText,
    title: "Conciliación con tolerancias",
    description:
      "Matching de movimientos internos contra extractos bancarios con tolerancias configurables de monto y fecha, detección de discrepancias y flujo de aprobación.",
  },
  {
    icon: ShieldCheck,
    title: "Cierre diario y auditoría",
    description:
      "Balances con cierre diario automatizado, transacciones posteadas inmutables y trazabilidad completa de cada operación.",
  },
] as const;

export default function LandingPage(): JSX.Element {
  const { isAuthenticated } = useAuth();

  // Warm-up del free tier: mientras el visitante lee el hero, la API de
  // Render se va despertando en background. Best-effort, resultado ignorado.
  useEffect(() => {
    void fetch(`${apiBaseUrl}/health`, { cache: "no-store" }).catch(() => {
      /* la landing no depende de la API */
    });
  }, []);

  return (
    <div className="flex min-h-screen flex-col">
      <header className="flex h-14 items-center justify-between px-4 lg:px-8">
        <div className="flex items-center gap-2">
          <LogoMark className="h-6 w-6 text-primary" />
          <span className="font-semibold">FinanceCore</span>
        </div>
        <div className="flex items-center gap-1">
          <Button asChild variant="ghost" size="icon" aria-label="Ver código en GitHub">
            <a href={GITHUB_URL} target="_blank" rel="noopener noreferrer">
              <Github className="h-4 w-4" />
            </a>
          </Button>
          <ThemeToggle />
        </div>
      </header>

      <main className="flex flex-1 flex-col items-center justify-center px-4 py-12">
        <div className="mx-auto max-w-3xl space-y-6 text-center">
          {demoMode && (
            <Badge variant="secondary" className="font-normal">
              Demo pública — datos de ejemplo, usuario de sólo lectura
            </Badge>
          )}
          <h1 className="text-4xl font-bold tracking-tight sm:text-5xl">
            Conciliación financiera
            <br />
            <span className="text-muted-foreground">
              multi-cuenta · multi-moneda
            </span>
          </h1>
          <p className="mx-auto max-w-xl text-muted-foreground">
            FinanceCore ingesta transacciones de múltiples fuentes, las cruza
            contra extractos bancarios con tolerancias configurables y lleva
            los balances con cierre diario y auditoría completa.
          </p>
          <div className="flex flex-wrap items-center justify-center gap-3">
            {isAuthenticated ? (
              <Button asChild size="lg">
                <Link href="/dashboard">
                  Ir al dashboard
                  <ArrowRight className="h-4 w-4" />
                </Link>
              </Button>
            ) : (
              <Button asChild size="lg">
                <Link href="/login">
                  Probar la demo
                  <ArrowRight className="h-4 w-4" />
                </Link>
              </Button>
            )}
            <Button asChild variant="outline" size="lg">
              <a href={GITHUB_URL} target="_blank" rel="noopener noreferrer">
                <Github className="h-4 w-4" />
                Ver código
              </a>
            </Button>
          </div>
        </div>

        {/* TODO(SCRUM-20): screenshot/GIF del flujo de conciliación acá. */}

        <div className="mx-auto mt-16 grid max-w-4xl gap-4 sm:grid-cols-3">
          {FEATURES.map((feature) => {
            const Icon = feature.icon;
            return (
              <Card key={feature.title}>
                <CardHeader className="pb-2">
                  <Icon className="mb-1 h-5 w-5 text-primary" />
                  <CardTitle className="text-base">{feature.title}</CardTitle>
                </CardHeader>
                <CardContent className="text-sm text-muted-foreground">
                  {feature.description}
                </CardContent>
              </Card>
            );
          })}
        </div>
      </main>

      <footer className="flex flex-wrap items-center justify-center gap-x-2 gap-y-1 border-t px-4 py-4 text-xs text-muted-foreground">
        <span>.NET 10 · Next.js 14 · PostgreSQL · Hangfire</span>
        <span aria-hidden>·</span>
        <a
          href={GITHUB_URL}
          target="_blank"
          rel="noopener noreferrer"
          className="underline-offset-2 hover:underline"
        >
          Código abierto en GitHub
        </a>
      </footer>
    </div>
  );
}
