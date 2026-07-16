"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Pencil, Power, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  alertRulesApi,
  type AlertRuleDto,
  type SaveAlertRuleRequest,
  type SourceProfileDto,
} from "@/lib/api/alert-rules";
import type { AccountListItemDto } from "@/lib/api/types";
import { formatDateTime, formatMoney } from "@/lib/format";
import { alertRuleTypeLabel, channelsLabel } from "@/components/alerts/labels";

interface Props {
  rules: AlertRuleDto[] | undefined;
  accounts: AccountListItemDto[] | undefined;
  profiles: SourceProfileDto[] | undefined;
  isLoading: boolean;
  onEdit: (rule: AlertRuleDto) => void;
}

/** DTO → request para mutaciones rápidas (toggle) sin abrir el formulario. */
function toRequest(rule: AlertRuleDto, isEnabled: boolean): SaveAlertRuleRequest {
  return {
    name: rule.name,
    accountId: rule.accountId,
    sourceProfileId: rule.sourceProfileId,
    thresholdAmount: rule.thresholdAmount,
    thresholdPercent: rule.thresholdPercent,
    lookbackDays: rule.lookbackDays,
    channels: rule.channels,
    emailTo: rule.emailTo,
    cooldownHours: rule.cooldownHours,
    isEnabled,
  };
}

export function AlertRulesTable({
  rules,
  accounts,
  profiles,
  isLoading,
  onEdit,
}: Props): JSX.Element {
  const queryClient = useQueryClient();

  const toggle = useMutation({
    mutationFn: (rule: AlertRuleDto) =>
      alertRulesApi.update(rule.id, toRequest(rule, !rule.isEnabled)),
    onSuccess: (updated) => {
      toast.success(updated.isEnabled ? "Regla habilitada" : "Regla deshabilitada");
      void queryClient.invalidateQueries({ queryKey: ["alert-rules"] });
    },
    onError: (err: Error) => toast.error(`No se pudo cambiar el estado: ${err.message}`),
  });

  const remove = useMutation({
    mutationFn: (id: string) => alertRulesApi.remove(id),
    onSuccess: () => {
      toast.success("Regla eliminada");
      void queryClient.invalidateQueries({ queryKey: ["alert-rules"] });
    },
    onError: (err: Error) => toast.error(`No se pudo eliminar: ${err.message}`),
  });

  function accountLabel(accountId: string | null): string {
    if (!accountId) return "todas las cuentas";
    const account = accounts?.find((a) => a.id === accountId);
    return account ? account.accountName : "cuenta";
  }

  function accountCurrency(accountId: string | null): string | null {
    return accounts?.find((a) => a.id === accountId)?.currencyCode ?? null;
  }

  function condition(rule: AlertRuleDto): string {
    switch (rule.type) {
      case "MissingPayout": {
        const profile = profiles?.find((p) => p.id === rule.sourceProfileId);
        const source = profile ? profile.displayName : "la fuente";
        return `Sin payout de ${source} por más de ${rule.lookbackDays} días`;
      }
      case "DiscrepancyThreshold": {
        const parts: string[] = [];
        if (rule.thresholdAmount != null) {
          const currency = accountCurrency(rule.accountId);
          parts.push(
            currency
              ? formatMoney(rule.thresholdAmount, currency)
              : rule.thresholdAmount.toLocaleString("es-AR"),
          );
        }
        if (rule.thresholdPercent != null)
          parts.push(`${(rule.thresholdPercent * 100).toLocaleString("es-AR")}%`);
        return `Discrepancia > ${parts.join(" o ")} en ${accountLabel(rule.accountId)}`;
      }
      case "LowBalance": {
        const currency = accountCurrency(rule.accountId);
        const amount =
          rule.thresholdAmount != null
            ? currency
              ? formatMoney(rule.thresholdAmount, currency)
              : rule.thresholdAmount.toLocaleString("es-AR")
            : "—";
        return `Saldo de ${accountLabel(rule.accountId)} < ${amount}`;
      }
    }
  }

  if (isLoading) {
    return (
      <div className="space-y-2">
        {Array.from({ length: 3 }).map((_, i) => (
          <Skeleton key={i} className="h-12 w-full" />
        ))}
      </div>
    );
  }

  if (!rules || rules.length === 0) {
    return (
      <div className="rounded-lg border border-dashed p-10 text-center text-sm text-muted-foreground">
        Sin reglas configuradas. Creá la primera — el caso estrella:{" "}
        <span className="font-medium text-foreground">
          &ldquo;avisame si el payout de PayU no llega&rdquo;
        </span>
        .
      </div>
    );
  }

  return (
    <div className="rounded-lg border">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Regla</TableHead>
            <TableHead className="hidden md:table-cell">Condición</TableHead>
            <TableHead className="hidden sm:table-cell">Canales</TableHead>
            <TableHead>Estado</TableHead>
            <TableHead className="hidden lg:table-cell">Último aviso</TableHead>
            <TableHead className="w-[120px] text-right">Acciones</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {rules.map((rule) => (
            <TableRow key={rule.id} className={rule.isEnabled ? "" : "opacity-60"}>
              <TableCell>
                <div className="font-medium">{rule.name}</div>
                <div className="text-xs text-muted-foreground">
                  {alertRuleTypeLabel(rule.type)}
                </div>
              </TableCell>
              <TableCell className="hidden max-w-[320px] text-sm text-muted-foreground md:table-cell">
                {condition(rule)}
              </TableCell>
              <TableCell className="hidden text-sm sm:table-cell">
                {channelsLabel(rule.channels)}
              </TableCell>
              <TableCell>
                <Badge variant={rule.isEnabled ? "default" : "outline"}>
                  {rule.isEnabled ? "Activa" : "Pausada"}
                </Badge>
              </TableCell>
              <TableCell className="hidden text-sm text-muted-foreground lg:table-cell">
                {formatDateTime(rule.lastTriggeredAt)}
              </TableCell>
              <TableCell className="text-right">
                <div className="flex justify-end gap-1">
                  <Button
                    variant="ghost"
                    size="icon"
                    aria-label={rule.isEnabled ? "Pausar regla" : "Habilitar regla"}
                    title={rule.isEnabled ? "Pausar" : "Habilitar"}
                    disabled={toggle.isPending}
                    onClick={() => toggle.mutate(rule)}
                  >
                    <Power className="h-4 w-4" />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    aria-label="Editar regla"
                    title="Editar"
                    onClick={() => onEdit(rule)}
                  >
                    <Pencil className="h-4 w-4" />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    aria-label="Eliminar regla"
                    title="Eliminar"
                    disabled={remove.isPending}
                    onClick={() => {
                      if (window.confirm(`¿Eliminar la regla "${rule.name}"?`))
                        remove.mutate(rule.id);
                    }}
                  >
                    <Trash2 className="h-4 w-4 text-destructive" />
                  </Button>
                </div>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}
