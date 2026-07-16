import type { AlertRuleType } from "@/lib/api/alert-rules";

const TYPE_LABELS: Record<AlertRuleType, string> = {
  MissingPayout: "Payout no llegó",
  DiscrepancyThreshold: "Discrepancia sobre umbral",
  LowBalance: "Saldo bajo",
};

export function alertRuleTypeLabel(type: AlertRuleType): string {
  return TYPE_LABELS[type] ?? type;
}

/** "Email, Webhook" → "Email + Webhook" para mostrar. */
export function channelsLabel(channels: string): string {
  return channels
    .split(",")
    .map((c) => c.trim())
    .join(" + ");
}
