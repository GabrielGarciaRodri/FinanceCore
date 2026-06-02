"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, CheckCircle2, ChevronDown, Loader2, Upload } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Progress } from "@/components/ui/progress";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { accountsApi } from "@/lib/api/accounts";
import {
  uploadsApi,
  type UploadTransactionsResponse,
} from "@/lib/api/uploads";
import { cn } from "@/lib/utils";
import { FileDropzone } from "./file-dropzone";

const ALL_VALUE = "__none__";

export function TransactionsUploadForm(): JSX.Element {
  const queryClient = useQueryClient();
  const [accountId, setAccountId] = useState<string | undefined>();
  const [file, setFile] = useState<File | null>(null);
  const [progress, setProgress] = useState(0);
  const [result, setResult] = useState<UploadTransactionsResponse | null>(null);
  const [errorsOpen, setErrorsOpen] = useState(false);

  const { data: accounts } = useQuery({
    queryKey: ["accounts", { activeOnly: true }],
    queryFn: () => accountsApi.list(false),
    staleTime: 5 * 60_000,
  });

  const mutation = useMutation({
    mutationFn: () =>
      uploadsApi.uploadTransactions(file!, accountId, (loaded, total) =>
        setProgress(Math.round((loaded / total) * 100)),
      ),
    onSuccess: (res) => {
      setResult(res);
      const succeeded = res.ingestion?.succeeded ?? 0;
      const failed = (res.ingestion?.failed ?? 0) + res.parseErrors.length;
      if (succeeded > 0) {
        toast.success(`${succeeded} transacciones insertadas`);
        void queryClient.invalidateQueries({ queryKey: ["transactions"] });
        void queryClient.invalidateQueries({ queryKey: ["dashboard"] });
      }
      if (failed > 0) {
        toast.warning(`${failed} filas con errores`);
      }
    },
    onError: (err: Error) => {
      toast.error(`Error en upload: ${err.message}`);
    },
    onSettled: () => setProgress(0),
  });

  const handleReset = () => {
    setFile(null);
    setResult(null);
    setErrorsOpen(false);
  };

  return (
    <div className="space-y-6">
      <div className="grid gap-4 md:grid-cols-2">
        <div className="space-y-1">
          <Label className="text-xs text-muted-foreground">
            Cuenta de destino (opcional)
          </Label>
          <Select
            value={accountId ?? ALL_VALUE}
            onValueChange={(v) => setAccountId(v === ALL_VALUE ? undefined : v)}
            disabled={mutation.isPending}
          >
            <SelectTrigger>
              <SelectValue placeholder="Usar AccountId del archivo" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL_VALUE}>Usar AccountId del archivo</SelectItem>
              {accounts?.map((a) => (
                <SelectItem key={a.id} value={a.id}>
                  {a.accountName} · {a.accountNumber} ({a.currencyCode})
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <p className="text-xs text-muted-foreground">
            Si seleccionás una cuenta, sobreescribe el AccountId de todas las filas.
          </p>
        </div>
      </div>

      <FileDropzone
        accept=".csv,.xlsx"
        file={file}
        onFileSelect={(f) => {
          setFile(f);
          setResult(null);
        }}
        disabled={mutation.isPending}
        helperText="Columnas requeridas: ExternalId, TransactionType, Amount, CurrencyCode, ValueDate. AccountId requerido si no se selecciona cuenta arriba."
      />

      {mutation.isPending && progress > 0 && (
        <div className="space-y-1">
          <Progress value={progress} />
          <p className="text-xs text-muted-foreground">Subiendo… {progress}%</p>
        </div>
      )}

      <div className="flex justify-end gap-2">
        {result && (
          <Button variant="outline" onClick={handleReset} disabled={mutation.isPending}>
            Limpiar
          </Button>
        )}
        <Button
          onClick={() => mutation.mutate()}
          disabled={!file || mutation.isPending}
        >
          {mutation.isPending ? (
            <Loader2 className="h-4 w-4 animate-spin" />
          ) : (
            <Upload className="h-4 w-4" />
          )}
          Subir
        </Button>
      </div>

      {result && (
        <ResultSummary
          result={result}
          errorsOpen={errorsOpen}
          onToggleErrors={() => setErrorsOpen((v) => !v)}
        />
      )}
    </div>
  );
}

function ResultSummary({
  result,
  errorsOpen,
  onToggleErrors,
}: {
  result: UploadTransactionsResponse;
  errorsOpen: boolean;
  onToggleErrors: () => void;
}): JSX.Element {
  const succeeded = result.ingestion?.succeeded ?? 0;
  const duplicates = result.ingestion?.duplicates ?? 0;
  const failedIngestion = result.ingestion?.failed ?? 0;
  const parseErrors = result.parseErrors.length;
  const totalErrors = failedIngestion + parseErrors;

  return (
    <div className="space-y-3 rounded-md border bg-card p-4">
      <div className="flex items-center gap-2">
        <CheckCircle2 className="h-5 w-5 text-emerald-600 dark:text-emerald-400" />
        <h3 className="text-sm font-medium">Resultado del upload</h3>
        <span className="text-xs text-muted-foreground">· {result.fileName}</span>
      </div>

      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <Stat label="Filas leídas" value={result.totalRowsParsed} />
        <Stat label="Insertadas" value={succeeded} tone={succeeded > 0 ? "good" : undefined} />
        <Stat label="Duplicadas" value={duplicates} tone={duplicates > 0 ? "warn" : undefined} />
        <Stat label="Fallidas" value={totalErrors} tone={totalErrors > 0 ? "bad" : undefined} />
      </div>

      {totalErrors > 0 && (
        <div className="rounded-md border bg-background">
          <button
            type="button"
            onClick={onToggleErrors}
            className="flex w-full items-center justify-between px-3 py-2 text-sm hover:bg-muted/50"
          >
            <span className="flex items-center gap-2">
              <AlertTriangle className="h-4 w-4 text-amber-600 dark:text-amber-400" />
              Ver detalle de errores ({totalErrors})
            </span>
            <ChevronDown
              className={cn("h-4 w-4 transition-transform", errorsOpen && "rotate-180")}
            />
          </button>
          {errorsOpen && (
            <div className="max-h-72 overflow-y-auto border-t">
              <table className="w-full text-xs">
                <thead className="bg-muted/30 text-left text-muted-foreground">
                  <tr>
                    <th className="px-3 py-2 w-20">Fila</th>
                    <th className="px-3 py-2 w-40">Origen</th>
                    <th className="px-3 py-2">Error</th>
                  </tr>
                </thead>
                <tbody className="divide-y">
                  {result.parseErrors.map((e) => (
                    <tr key={`parse-${e.row}`}>
                      <td className="px-3 py-1.5 tabular-nums">{e.row}</td>
                      <td className="px-3 py-1.5 text-muted-foreground">Parser</td>
                      <td className="px-3 py-1.5">{e.error}</td>
                    </tr>
                  ))}
                  {result.ingestion?.results
                    .filter((r) => !r.success && !r.isDuplicate)
                    .map((r) => (
                      <tr key={`ing-${r.externalId}`}>
                        <td className="px-3 py-1.5 font-mono text-muted-foreground">
                          {r.externalId.slice(0, 12)}…
                        </td>
                        <td className="px-3 py-1.5 text-muted-foreground">Ingestion</td>
                        <td className="px-3 py-1.5">{r.errorMessage ?? "—"}</td>
                      </tr>
                    ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

function Stat({
  label,
  value,
  tone,
}: {
  label: string;
  value: number;
  tone?: "good" | "warn" | "bad";
}): JSX.Element {
  const color =
    tone === "bad"
      ? "text-rose-600 dark:text-rose-400"
      : tone === "warn"
      ? "text-amber-600 dark:text-amber-400"
      : tone === "good"
      ? "text-emerald-600 dark:text-emerald-400"
      : "";
  return (
    <div>
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className={cn("mt-0.5 text-xl font-semibold tabular-nums", color)}>
        {value}
      </div>
    </div>
  );
}
