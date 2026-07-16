"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { BellPlus } from "lucide-react";
import { AlertRuleDialog } from "@/components/alerts/alert-rule-dialog";
import { AlertRulesTable } from "@/components/alerts/alert-rules-table";
import { ReadOnlyNotice } from "@/components/auth/read-only-notice";
import { Button } from "@/components/ui/button";
import { accountsApi } from "@/lib/api/accounts";
import { alertRulesApi, sourceProfilesApi, type AlertRuleDto } from "@/lib/api/alert-rules";
import { useAuth } from "@/lib/auth/context";

export default function AlertsPage(): JSX.Element {
  const { canWrite } = useAuth();
  const [dialogOpen, setDialogOpen] = useState(false);
  const [editing, setEditing] = useState<AlertRuleDto | null>(null);

  const { data: rules, isLoading } = useQuery({
    queryKey: ["alert-rules"],
    queryFn: () => alertRulesApi.list(),
    enabled: canWrite,
  });

  const { data: accounts } = useQuery({
    queryKey: ["accounts", { includeInactive: false }],
    queryFn: () => accountsApi.list(false),
    staleTime: 60_000,
    enabled: canWrite,
  });

  const { data: profiles } = useQuery({
    queryKey: ["source-profiles"],
    queryFn: () => sourceProfilesApi.list(),
    staleTime: 60_000,
    enabled: canWrite,
  });

  if (!canWrite) {
    return (
      <div className="space-y-6">
        <header>
          <h1 className="text-2xl font-semibold tracking-tight">Alertas</h1>
        </header>
        <ReadOnlyNotice />
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Alertas</h1>
          <p className="text-sm text-muted-foreground">
            Reglas de negocio que avisan por email o webhook: payouts que no
            llegan, discrepancias sobre umbral y saldos bajos.
          </p>
        </div>
        <Button
          onClick={() => {
            setEditing(null);
            setDialogOpen(true);
          }}
        >
          <BellPlus className="h-4 w-4" />
          Nueva regla
        </Button>
      </header>

      <AlertRulesTable
        rules={rules}
        accounts={accounts}
        profiles={profiles}
        isLoading={isLoading}
        onEdit={(rule) => {
          setEditing(rule);
          setDialogOpen(true);
        }}
      />

      <AlertRuleDialog
        open={dialogOpen}
        rule={editing}
        onClose={() => setDialogOpen(false)}
      />
    </div>
  );
}
