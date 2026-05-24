"use client";

import { useEffect, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { reconciliationsApi } from "@/lib/api/reconciliations";
import type {
  ReconciliationDiscrepancyDto,
  ResolutionType,
} from "@/lib/api/types";
import { useAuth } from "@/lib/auth/context";

/** Opciones de resolución manual (excluye "Pending" que es el estado inicial). */
const RESOLUTION_OPTIONS: ResolutionType[] = [
  "MatchedManually",
  "AdjustmentCreated",
  "Ignored",
  "UnderInvestigation",
  "Escalated",
];

interface Props {
  reconciliationId: string;
  discrepancy: ReconciliationDiscrepancyDto | null;
  onClose: () => void;
}

export function ResolveDiscrepancyDialog({
  reconciliationId,
  discrepancy,
  onClose,
}: Props): JSX.Element {
  const { user } = useAuth();
  const queryClient = useQueryClient();
  const open = discrepancy !== null;

  const [resolution, setResolution] = useState<ResolutionType>("MatchedManually");
  const [notes, setNotes] = useState("");

  useEffect(() => {
    if (open) {
      setResolution("MatchedManually");
      setNotes("");
    }
  }, [open]);

  const resolvedBy = user?.displayName?.trim() || user?.email || "unknown";

  const mutation = useMutation({
    mutationFn: () =>
      reconciliationsApi.resolveDiscrepancy(reconciliationId, discrepancy!.id, {
        resolution,
        resolvedBy,
        notes: notes.trim() || undefined,
      }),
    onSuccess: () => {
      toast.success("Discrepancia resuelta");
      void queryClient.invalidateQueries({ queryKey: ["reconciliation", reconciliationId] });
      void queryClient.invalidateQueries({ queryKey: ["reconciliations"] });
      onClose();
    },
    onError: (err: Error) => {
      toast.error(`No se pudo resolver: ${err.message}`);
    },
  });

  return (
    <Dialog open={open} onOpenChange={(o) => !o && !mutation.isPending && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Resolver discrepancia</DialogTitle>
          <DialogDescription>
            {discrepancy
              ? `Tipo: ${discrepancy.discrepancyType}. Diferencia: ${
                  discrepancy.differenceAmount !== null
                    ? discrepancy.differenceAmount.toLocaleString("es-AR")
                    : "—"
                }.`
              : ""}
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="space-y-1">
            <Label className="text-xs text-muted-foreground">Tipo de resolución</Label>
            <Select
              value={resolution}
              onValueChange={(v) => setResolution(v as ResolutionType)}
            >
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {RESOLUTION_OPTIONS.map((r) => (
                  <SelectItem key={r} value={r}>
                    {r}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="space-y-1">
            <Label className="text-xs text-muted-foreground">
              Notas (opcional)
            </Label>
            <Textarea
              rows={3}
              placeholder="Justificación, referencia interna, etc."
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
            />
          </div>

          <div className="text-xs text-muted-foreground">
            Se registrará como resuelta por <span className="font-medium">{resolvedBy}</span>.
          </div>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={mutation.isPending}>
            Cancelar
          </Button>
          <Button
            onClick={() => mutation.mutate()}
            disabled={mutation.isPending}
          >
            {mutation.isPending && <Loader2 className="h-4 w-4 animate-spin" />}
            Resolver
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
