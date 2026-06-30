/**
 * Etiquetas en español (es-LA) para los enums que el backend serializa como
 * `string` en inglés. El string inglés es el CONTRATO con el API (valor de
 * respuesta y valor de filtro): NO se traduce nunca. La traducción ocurre
 * solo acá, en la capa de presentación.
 *
 * Patrón a futuro (export multi-idioma): estos `Record` están pensados para
 * migrar a `t("status.transaction.Pending")` de next-intl/react-i18next sin
 * tocar los componentes que consumen los `*Label()`. Por eso el acceso al
 * texto va siempre a través de las funciones, no del map directo.
 */

import type {
  AccountType,
  DiscrepancyType,
  ReconciliationStatus,
  ResolutionType,
  TransactionStatus,
  TransactionType,
} from "@/lib/api/types";

export const transactionTypeLabels: Record<TransactionType, string> = {
  Debit: "Débito",
  Credit: "Crédito",
  TransferOut: "Transferencia saliente",
  TransferIn: "Transferencia entrante",
  Fee: "Comisión",
  Interest: "Intereses",
  Adjustment: "Ajuste",
};

export const transactionStatusLabels: Record<TransactionStatus, string> = {
  Pending: "Pendiente",
  Processing: "Procesando",
  Validated: "Validada",
  Posted: "Contabilizada",
  Reconciled: "Conciliada",
  Rejected: "Rechazada",
  Reversed: "Reversada",
};

export const reconciliationStatusLabels: Record<ReconciliationStatus, string> = {
  Pending: "Pendiente",
  InProgress: "En curso",
  Completed: "Completada",
  CompletedWithDiscrepancies: "Con discrepancias",
  Failed: "Falló",
};

export const discrepancyTypeLabels: Record<DiscrepancyType, string> = {
  MissingExternal: "Falta en externo",
  MissingInternal: "Falta en interno",
  AmountMismatch: "Monto no coincide",
  DateMismatch: "Fecha no coincide",
  PossibleDuplicate: "Posible duplicado",
  ReferenceMismatch: "Referencia no coincide",
};

export const resolutionTypeLabels: Record<ResolutionType, string> = {
  Pending: "Pendiente",
  MatchedManually: "Conciliada manualmente",
  AdjustmentCreated: "Ajuste creado",
  Ignored: "Ignorada",
  UnderInvestigation: "En investigación",
  Escalated: "Escalada",
};

export const accountTypeLabels: Record<AccountType, string> = {
  Checking: "Cuenta corriente",
  Savings: "Cuenta de ahorro",
  Investment: "Inversión",
  Credit: "Línea de crédito",
  Loan: "Préstamo",
  Treasury: "Tesorería",
};

/**
 * Crea un traductor con fallback: si el backend devuelve un valor que no está
 * en el map (enum nuevo todavía no mapeado), muestra el string crudo en vez de
 * `undefined`. Acepta `string` para tolerar los DTOs que tipan el enum como
 * string genérico.
 */
function makeLabeler<T extends string>(map: Record<T, string>) {
  return (value: T | string | null | undefined): string =>
    value == null ? "" : map[value as T] ?? value;
}

export const transactionTypeLabel = makeLabeler(transactionTypeLabels);
export const transactionStatusLabel = makeLabeler(transactionStatusLabels);
export const reconciliationStatusLabel = makeLabeler(reconciliationStatusLabels);
export const discrepancyTypeLabel = makeLabeler(discrepancyTypeLabels);
export const resolutionTypeLabel = makeLabeler(resolutionTypeLabels);
export const accountTypeLabel = makeLabeler(accountTypeLabels);
