"use client";

import { useCallback, useMemo } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { FileSpreadsheet, FileText } from "lucide-react";
import { StatementUploadForm } from "@/components/upload/statement-upload-form";
import { TransactionsUploadForm } from "@/components/upload/transactions-upload-form";
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from "@/components/ui/tabs";

type TabKey = "transactions" | "statement";
const VALID_TABS: TabKey[] = ["transactions", "statement"];

export default function UploadPage(): JSX.Element {
  const router = useRouter();
  const pathname = usePathname();
  const sp = useSearchParams();

  const activeTab = useMemo<TabKey>(() => {
    const t = sp?.get("tab") as TabKey | null;
    return t && VALID_TABS.includes(t) ? t : "transactions";
  }, [sp]);

  const setTab = useCallback(
    (next: string) => {
      const params = new URLSearchParams(sp?.toString() ?? "");
      if (next === "transactions") params.delete("tab");
      else params.set("tab", next);
      const qs = params.toString();
      router.replace(qs ? `${pathname}?${qs}` : pathname, { scroll: false });
    },
    [router, pathname, sp],
  );

  return (
    <div className="space-y-6">
      <header>
        <h1 className="text-2xl font-semibold tracking-tight">Cargar archivos</h1>
        <p className="text-sm text-muted-foreground">
          Subir transacciones internas o un extracto bancario para reconciliar.
        </p>
      </header>

      <Tabs value={activeTab} onValueChange={setTab}>
        <TabsList>
          <TabsTrigger value="transactions">
            <FileSpreadsheet className="h-4 w-4" />
            Transacciones
          </TabsTrigger>
          <TabsTrigger value="statement">
            <FileText className="h-4 w-4" />
            Extracto bancario
          </TabsTrigger>
        </TabsList>

        <TabsContent value="transactions" className="mt-6">
          <div className="rounded-md border bg-card p-6">
            <div className="mb-4">
              <h2 className="text-sm font-medium">Ingesta de transacciones</h2>
              <p className="mt-1 text-xs text-muted-foreground">
                Subí un CSV o XLSX con tus movimientos. Las transacciones ya existentes
                (mismo ExternalId) se detectan como duplicadas y no se duplican en la base.
              </p>
            </div>
            <TransactionsUploadForm />
          </div>
        </TabsContent>

        <TabsContent value="statement" className="mt-6">
          <div className="rounded-md border bg-card p-6">
            <div className="mb-4">
              <h2 className="text-sm font-medium">Statement bancario</h2>
              <p className="mt-1 text-xs text-muted-foreground">
                Subí un CSV con el extracto que te dio el banco para una cuenta y fecha
                específicas. El motor de reconciliación se ejecuta automáticamente y crea
                la reconciliación con sus discrepancias.
              </p>
            </div>
            <StatementUploadForm />
          </div>
        </TabsContent>
      </Tabs>
    </div>
  );
}
