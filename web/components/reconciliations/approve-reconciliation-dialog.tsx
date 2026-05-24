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
import { Textarea } from "@/components/ui/textarea";
import { reconciliationsApi } from "@/lib/api/reconciliations";
import { useAuth } from "@/lib/auth/context";

interface Props {
  reconciliationId: string;
  open: boolean;
  onClose: () => void;
  unresolvedCount: number;
}

export function ApproveReconciliationDialog({
  reconciliationId,
  open,
  onClose,
  unresolvedCount,
}: Props): JSX.Element {
  const { user } = useAuth();
  const queryClient = useQueryClient();

  const [notes, setNotes] = useState("");

  useEffect(() => {
    if (open) setNotes("");
  }, [open]);

  const approvedBy = user?.displayName?.trim() || user?.email || "unknown";

  const mutation = useMutation({
    mutationFn: () =>
      reconciliationsApi.approve(reconciliationId, {
        approvedBy,
        resolutionNotes: notes.trim() || undefined,
      }),
    onSuccess: () => {
      toast.success("Reconciliación aprobada");
      void queryClient.invalidateQueries({ queryKey: ["reconciliation", reconciliationId] });
      void queryClient.invalidateQueries({ queryKey: ["reconciliations"] });
      onClose();
    },
    onError: (err: Error) => {
      toast.error(`No se pudo aprobar: ${err.message}`);
    },
  });

  return (
    <Dialog open={open} onOpenChange={(o) => !o && !mutation.isPending && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Aprobar reconciliación</DialogTitle>
          <DialogDescription>
            Esta acción es terminal y no se puede deshacer desde la UI.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          {unresolvedCount > 0 && (
            <div className="rounded-md border border-amber-500/30 bg-amber-500/10 p-3 text-sm">
              ⚠️ Quedan <span className="font-medium">{unresolvedCount}</span> discrepancia
              {unresolvedCount === 1 ? "" : "s"} sin resolver. El backend puede rechazar la
              aprobación según las reglas de negocio.
            </div>
          )}

          <div className="space-y-1">
            <Label className="text-xs text-muted-foreground">
              Notas de aprobación (opcional)
            </Label>
            <Textarea
              rows={3}
              placeholder="Comentario para el registro de auditoría…"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
            />
          </div>

          <div className="text-xs text-muted-foreground">
            Se registrará como aprobada por <span className="font-medium">{approvedBy}</span>.
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
            Aprobar
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
