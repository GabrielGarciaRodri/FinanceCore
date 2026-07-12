"use client";

import { useState } from "react";
import { Download, FileSpreadsheet, FileText, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { trackExportDownloaded } from "@/lib/analytics";
import { transactionsApi } from "@/lib/api/transactions";
import type { SearchTransactionsRequest } from "@/lib/api/types";

interface Props {
  params: SearchTransactionsRequest;
  disabled?: boolean;
}

export function ExportMenu({ params, disabled }: Props): JSX.Element {
  const [busy, setBusy] = useState<"csv" | "xlsx" | null>(null);

  const handleExport = async (format: "csv" | "xlsx") => {
    setBusy(format);
    try {
      const { blob, filename } = await transactionsApi.downloadExport(format, params);
      triggerDownload(blob, filename);
      trackExportDownloaded(format, "transactions");
      toast.success(`Export ${format.toUpperCase()} listo`);
    } catch (err) {
      const message = (err as Error)?.message ?? "Error desconocido";
      toast.error(`Error en export ${format.toUpperCase()}: ${message}`);
    } finally {
      setBusy(null);
    }
  };

  const isBusy = busy !== null;

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="outline" size="sm" disabled={disabled || isBusy}>
          {isBusy ? (
            <Loader2 className="h-4 w-4 animate-spin" />
          ) : (
            <Download className="h-4 w-4" />
          )}
          Exportar
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuItem
          onClick={() => void handleExport("csv")}
          disabled={isBusy}
        >
          <FileText className="h-4 w-4" />
          CSV
        </DropdownMenuItem>
        <DropdownMenuItem
          onClick={() => void handleExport("xlsx")}
          disabled={isBusy}
        >
          <FileSpreadsheet className="h-4 w-4" />
          Excel (XLSX)
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

function triggerDownload(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  // Liberar el objectURL en el siguiente tick para que el browser termine la descarga.
  setTimeout(() => URL.revokeObjectURL(url), 0);
}
