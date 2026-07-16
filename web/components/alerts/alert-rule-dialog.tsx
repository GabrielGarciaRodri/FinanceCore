"use client";

import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
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
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { accountsApi } from "@/lib/api/accounts";
import {
  alertRulesApi,
  sourceProfilesApi,
  type AlertRuleDto,
  type AlertRuleType,
  type SaveAlertRuleRequest,
} from "@/lib/api/alert-rules";
import { alertRuleTypeLabel } from "@/components/alerts/labels";

const ALL_ACCOUNTS = "__all__";

const TYPE_OPTIONS: AlertRuleType[] = [
  "MissingPayout",
  "DiscrepancyThreshold",
  "LowBalance",
];

interface Props {
  open: boolean;
  /** Regla a editar; null = crear una nueva. */
  rule: AlertRuleDto | null;
  onClose: () => void;
}

export function AlertRuleDialog({ open, rule, onClose }: Props): JSX.Element {
  const queryClient = useQueryClient();
  const editing = rule !== null;

  const [type, setType] = useState<AlertRuleType>("MissingPayout");
  const [name, setName] = useState("");
  const [accountId, setAccountId] = useState<string>(ALL_ACCOUNTS);
  const [sourceProfileId, setSourceProfileId] = useState<string>("");
  const [thresholdAmount, setThresholdAmount] = useState("");
  const [thresholdPercent, setThresholdPercent] = useState("");
  const [lookbackDays, setLookbackDays] = useState("7");
  const [channelEmail, setChannelEmail] = useState(true);
  const [channelWebhook, setChannelWebhook] = useState(false);
  const [emailTo, setEmailTo] = useState("");
  const [cooldownHours, setCooldownHours] = useState("24");
  const [isEnabled, setIsEnabled] = useState(true);

  useEffect(() => {
    if (!open) return;
    setType(rule?.type ?? "MissingPayout");
    setName(rule?.name ?? "");
    setAccountId(rule?.accountId ?? ALL_ACCOUNTS);
    setSourceProfileId(rule?.sourceProfileId ?? "");
    setThresholdAmount(rule?.thresholdAmount != null ? String(rule.thresholdAmount) : "");
    setThresholdPercent(
      rule?.thresholdPercent != null ? String(rule.thresholdPercent * 100) : "",
    );
    setLookbackDays(rule?.lookbackDays != null ? String(rule.lookbackDays) : "7");
    setChannelEmail(rule ? rule.channels.includes("Email") : true);
    setChannelWebhook(rule ? rule.channels.includes("Webhook") : false);
    setEmailTo(rule?.emailTo ?? "");
    setCooldownHours(rule ? String(rule.cooldownHours) : "24");
    setIsEnabled(rule?.isEnabled ?? true);
  }, [open, rule]);

  const { data: accounts } = useQuery({
    queryKey: ["accounts", { includeInactive: false }],
    queryFn: () => accountsApi.list(false),
    staleTime: 60_000,
    enabled: open,
  });

  const { data: profiles } = useQuery({
    queryKey: ["source-profiles"],
    queryFn: () => sourceProfilesApi.list(),
    staleTime: 60_000,
    enabled: open && type === "MissingPayout",
  });

  const mutation = useMutation({
    mutationFn: (body: SaveAlertRuleRequest) =>
      editing ? alertRulesApi.update(rule.id, body) : alertRulesApi.create(body),
    onSuccess: () => {
      toast.success(editing ? "Regla actualizada" : "Regla creada");
      void queryClient.invalidateQueries({ queryKey: ["alert-rules"] });
      onClose();
    },
    onError: (err: unknown) => {
      const detail =
        (err as { response?: { data?: { detail?: string } } })?.response?.data
          ?.detail ?? (err as Error).message;
      toast.error(`No se pudo guardar: ${detail}`);
    },
  });

  function submit(): void {
    const channels = [
      ...(channelEmail ? ["Email"] : []),
      ...(channelWebhook ? ["Webhook"] : []),
    ].join(", ");

    if (!channels) {
      toast.error("Elegí al menos un canal de entrega.");
      return;
    }

    const body: SaveAlertRuleRequest = {
      name: name.trim(),
      ...(editing ? {} : { type }),
      accountId:
        type === "MissingPayout" || accountId === ALL_ACCOUNTS ? null : accountId,
      sourceProfileId: type === "MissingPayout" ? sourceProfileId || null : null,
      thresholdAmount:
        type !== "MissingPayout" && thresholdAmount !== ""
          ? Number(thresholdAmount)
          : null,
      thresholdPercent:
        type === "DiscrepancyThreshold" && thresholdPercent !== ""
          ? Number(thresholdPercent) / 100
          : null,
      lookbackDays: type === "MissingPayout" ? Number(lookbackDays) : null,
      channels,
      emailTo: emailTo.trim() || null,
      cooldownHours: Number(cooldownHours),
      ...(editing ? { isEnabled } : {}),
    };

    mutation.mutate(body);
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && !mutation.isPending && onClose()}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{editing ? "Editar regla" : "Nueva regla de alerta"}</DialogTitle>
          <DialogDescription>
            La plataforma avisa por email o webhook cuando la condición se cumple.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          {!editing && (
            <div className="space-y-1">
              <Label className="text-xs text-muted-foreground">Tipo de alerta</Label>
              <Select value={type} onValueChange={(v) => setType(v as AlertRuleType)}>
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {TYPE_OPTIONS.map((t) => (
                    <SelectItem key={t} value={t}>
                      {alertRuleTypeLabel(t)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          )}

          <div className="space-y-1">
            <Label className="text-xs text-muted-foreground">Nombre</Label>
            <Input
              placeholder='Ej: "El payout de PayU no llegó"'
              value={name}
              onChange={(e) => setName(e.target.value)}
              maxLength={100}
            />
          </div>

          {type === "MissingPayout" && (
            <>
              <div className="space-y-1">
                <Label className="text-xs text-muted-foreground">Fuente vigilada</Label>
                <Select value={sourceProfileId} onValueChange={setSourceProfileId}>
                  <SelectTrigger>
                    <SelectValue placeholder="Elegir perfil de fuente…" />
                  </SelectTrigger>
                  <SelectContent>
                    {(profiles ?? []).map((p) => (
                      <SelectItem key={p.id} value={p.id}>
                        {p.displayName} ({p.sourceKey})
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                {profiles?.length === 0 && (
                  <p className="text-xs text-muted-foreground">
                    No hay perfiles de fuente configurados todavía.
                  </p>
                )}
              </div>
              <div className="space-y-1">
                <Label className="text-xs text-muted-foreground">
                  Días sin payout antes de avisar
                </Label>
                <Input
                  type="number"
                  min={1}
                  max={62}
                  value={lookbackDays}
                  onChange={(e) => setLookbackDays(e.target.value)}
                />
              </div>
            </>
          )}

          {type !== "MissingPayout" && (
            <div className="space-y-1">
              <Label className="text-xs text-muted-foreground">
                {type === "LowBalance" ? "Cuenta vigilada" : "Cuenta (opcional)"}
              </Label>
              <Select value={accountId} onValueChange={setAccountId}>
                <SelectTrigger>
                  <SelectValue placeholder="Elegir cuenta…" />
                </SelectTrigger>
                <SelectContent>
                  {type === "DiscrepancyThreshold" && (
                    <SelectItem value={ALL_ACCOUNTS}>Todas las cuentas</SelectItem>
                  )}
                  {(accounts ?? []).map((a) => (
                    <SelectItem key={a.id} value={a.id}>
                      {a.accountName} ({a.currencyCode})
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          )}

          {type === "DiscrepancyThreshold" && (
            <div className="grid grid-cols-2 gap-3">
              <div className="space-y-1">
                <Label className="text-xs text-muted-foreground">Umbral en monto</Label>
                <Input
                  type="number"
                  min={0}
                  placeholder="100000"
                  value={thresholdAmount}
                  onChange={(e) => setThresholdAmount(e.target.value)}
                />
              </div>
              <div className="space-y-1">
                <Label className="text-xs text-muted-foreground">
                  Umbral en % (opcional)
                </Label>
                <Input
                  type="number"
                  min={0}
                  max={99}
                  step="0.1"
                  placeholder="2"
                  value={thresholdPercent}
                  onChange={(e) => setThresholdPercent(e.target.value)}
                />
              </div>
            </div>
          )}

          {type === "LowBalance" && (
            <div className="space-y-1">
              <Label className="text-xs text-muted-foreground">Saldo mínimo</Label>
              <Input
                type="number"
                placeholder="1000000"
                value={thresholdAmount}
                onChange={(e) => setThresholdAmount(e.target.value)}
              />
            </div>
          )}

          <div className="space-y-1">
            <Label className="text-xs text-muted-foreground">Canales de entrega</Label>
            <div className="flex gap-4 pt-1">
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  className="h-4 w-4 accent-primary"
                  checked={channelEmail}
                  onChange={(e) => setChannelEmail(e.target.checked)}
                />
                Email
              </label>
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  className="h-4 w-4 accent-primary"
                  checked={channelWebhook}
                  onChange={(e) => setChannelWebhook(e.target.checked)}
                />
                Webhook
              </label>
            </div>
          </div>

          {channelEmail && (
            <div className="space-y-1">
              <Label className="text-xs text-muted-foreground">
                Email destinatario (vacío = el configurado en el servidor)
              </Label>
              <Input
                type="email"
                placeholder="ops@miempresa.co"
                value={emailTo}
                onChange={(e) => setEmailTo(e.target.value)}
              />
            </div>
          )}

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1">
              <Label className="text-xs text-muted-foreground">
                Cooldown entre avisos (horas)
              </Label>
              <Input
                type="number"
                min={1}
                max={168}
                value={cooldownHours}
                onChange={(e) => setCooldownHours(e.target.value)}
              />
            </div>
            {editing && (
              <div className="space-y-1">
                <Label className="text-xs text-muted-foreground">Estado</Label>
                <label className="flex h-9 items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    className="h-4 w-4 accent-primary"
                    checked={isEnabled}
                    onChange={(e) => setIsEnabled(e.target.checked)}
                  />
                  Regla habilitada
                </label>
              </div>
            )}
          </div>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={mutation.isPending}>
            Cancelar
          </Button>
          <Button
            onClick={submit}
            disabled={
              mutation.isPending ||
              !name.trim() ||
              (type === "MissingPayout" && !sourceProfileId) ||
              (type === "LowBalance" &&
                (accountId === ALL_ACCOUNTS || thresholdAmount === ""))
            }
          >
            {mutation.isPending && <Loader2 className="h-4 w-4 animate-spin" />}
            {editing ? "Guardar" : "Crear regla"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
