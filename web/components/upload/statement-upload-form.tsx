"use client";

import { useState } from "react";
import Link from "next/link";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowRight, CalendarIcon, CheckCircle2, Loader2, Upload, X } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Calendar } from "@/components/ui/calendar";
import { Label } from "@/components/ui/label";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
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
  type StatementUploadResponse,
} from "@/lib/api/uploads";
import { fromIsoDate, toIsoDate } from "@/lib/format";
import { cn } from "@/lib/utils";
import { FileDropzone } from "./file-dropzone";

export function StatementUploadForm(): JSX.Element {
  const queryClient = useQueryClient();
  const [accountId, setAccountId] = useState<string | undefined>();
  const [date, setDate] = useState<string | undefined>();
  const [file, setFile] = useState<File | null>(null);
  const [progress, setProgress] = useState(0);
  const [result, setResult] = useState<StatementUploadResponse | null>(null);

  const { data: accounts } = useQuery({
    queryKey: ["accounts", { activeOnly: true }],
    queryFn: () => accountsApi.list(false),
    staleTime: 5 * 60_000,
  });

  const canSubmit = file && accountId && date;

  const mutation = useMutation({
    mutationFn: () =>
      uploadsApi.uploadStatement(file!, accountId!, date!, (loaded, total) =>
        setProgress(Math.round((loaded / total) * 100)),
      ),
    onSuccess: (res) => {
      setResult(res);
      toast.success(`Reconciliación creada · ${res.matched} matched, ${res.discrepancyCount} discrepancias`);
      void queryClient.invalidateQueries({ queryKey: ["reconciliations"] });
      void queryClient.invalidateQueries({ queryKey: ["dashboard"] });
    },
    onError: (err: Error) => {
      toast.error(`Error en upload: ${err.message}`);
    },
    onSettled: () => setProgress(0),
  });

  const handleReset = () => {
    setFile(null);
    setResult(null);
  };

  const selectedDate = fromIsoDate(date);

  return (
    <div className="space-y-6">
      <div className="grid gap-4 md:grid-cols-2">
        <div className="space-y-1">
          <Label className="text-xs text-muted-foreground">
            Cuenta a reconciliar <span className="text-destructive">*</span>
          </Label>
          <Select
            value={accountId ?? ""}
            onValueChange={(v) => setAccountId(v)}
            disabled={mutation.isPending}
          >
            <SelectTrigger>
              <SelectValue placeholder="Seleccionar cuenta" />
            </SelectTrigger>
            <SelectContent>
              {accounts?.map((a) => (
                <SelectItem key={a.id} value={a.id}>
                  {a.accountName} · {a.accountNumber} ({a.currencyCode})
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <div className="space-y-1">
          <Label className="text-xs text-muted-foreground">
            Fecha de reconciliación <span className="text-destructive">*</span>
          </Label>
          <Popover>
            <PopoverTrigger asChild>
              <Button
                variant="outline"
                className="w-full justify-start text-left font-normal"
                disabled={mutation.isPending}
              >
                <CalendarIcon className="h-4 w-4" />
                {selectedDate
                  ? new Intl.DateTimeFormat("es-AR", {
                      day: "2-digit",
                      month: "short",
                      year: "numeric",
                    }).format(selectedDate)
                  : <span className="text-muted-foreground">Seleccionar fecha</span>}
                {date && (
                  <button
                    type="button"
                    onClick={(e) => {
                      e.preventDefault();
                      e.stopPropagation();
                      setDate(undefined);
                    }}
                    className="ml-auto rounded-sm opacity-60 hover:opacity-100"
                  >
                    <X className="h-3 w-3" />
                  </button>
                )}
              </Button>
            </PopoverTrigger>
            <PopoverContent className="w-auto p-0" align="start">
              <Calendar
                mode="single"
                selected={selectedDate}
                onSelect={(d) => setDate(d ? toIsoDate(d) : undefined)}
                autoFocus
              />
            </PopoverContent>
          </Popover>
        </div>
      </div>

      <FileDropzone
        accept=".csv"
        file={file}
        onFileSelect={(f) => {
          setFile(f);
          setResult(null);
        }}
        disabled={mutation.isPending}
        helperText="Columnas requeridas: ExternalReference, Amount, CurrencyCode, ValueDate. Description opcional."
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
          disabled={!canSubmit || mutation.isPending}
        >
          {mutation.isPending ? (
            <Loader2 className="h-4 w-4 animate-spin" />
          ) : (
            <Upload className="h-4 w-4" />
          )}
          Subir y reconciliar
        </Button>
      </div>

      {result && <StatementResultSummary result={result} />}
    </div>
  );
}

function StatementResultSummary({
  result,
}: {
  result: StatementUploadResponse;
}): JSX.Element {
  const totalUnmatched = result.unmatchedInternal + result.unmatchedExternal;
  return (
    <div className="space-y-3 rounded-md border bg-card p-4">
      <div className="flex items-center gap-2">
        <CheckCircle2 className="h-5 w-5 text-emerald-600 dark:text-emerald-400" />
        <h3 className="text-sm font-medium">Reconciliación completada</h3>
        <span className="ml-auto text-xs text-muted-foreground">
          Estado: <span className="font-medium">{result.status}</span>
        </span>
      </div>

      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <Stat label="Líneas leídas" value={result.linesParsed} />
        <Stat label="Matched" value={result.matched} tone={result.matched > 0 ? "good" : undefined} />
        <Stat
          label="Unmatched"
          value={totalUnmatched}
          tone={totalUnmatched > 0 ? "warn" : undefined}
        />
        <Stat
          label="Discrepancias"
          value={result.discrepancyCount}
          tone={result.discrepancyCount > 0 ? "bad" : undefined}
        />
      </div>

      <div className="flex justify-end">
        <Button asChild size="sm">
          <Link href={`/reconciliations/${result.reconciliationId}`}>
            Ver detalle
            <ArrowRight className="h-4 w-4" />
          </Link>
        </Button>
      </div>
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
