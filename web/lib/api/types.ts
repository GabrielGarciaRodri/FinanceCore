/**
 * Tipos compartidos con el backend FinanceCore.API.
 *
 * Estrategia mixta:
 * - Los DTOs y enums que vienen del backend se re-exportan desde
 *   `./generated.ts` (autogenerado vía `npm run gen:api`). Cambios en el
 *   backend → corré `npm run gen:api` con el API local levantado → tsc te
 *   marca dónde refactorear.
 * - Los tipos frontend-only (helpers genéricos, string-literal unions de
 *   enums que el backend serializa como `string`, request shapes de query
 *   params) viven en este archivo a mano.
 *
 * Regenerar: con el API local corriendo en :5000, ejecutar
 *     npm run gen:api
 */

import type { components } from "./generated";

type Schemas = components["schemas"];

// ============================================================================
// Re-exports desde el OpenAPI del backend
// ============================================================================

// ----- Auth -----

export type AuthUser = Schemas["AuthenticatedUserResponse"];
export type AuthTokenResponse = Schemas["AuthTokenResponse"];
export type LoginRequest = Schemas["LoginRequest"];
export type RefreshTokenRequest = Schemas["RefreshTokenRequest"];

// ----- Transactions -----

export type TransactionDetailDto = Schemas["TransactionDetailDto"];
export type TransactionListItemDto = Schemas["TransactionListItemDto"];
export type TransactionSummaryDto = Schemas["TransactionSummaryDto"];
export type PagedTransactionsDto = Schemas["PagedTransactionsDto"];
export type UploadTransactionsResponse = Schemas["UploadTransactionsResponse"];
export type UploadRowError = Schemas["UploadRowError"];

// ----- Accounts -----

export type AccountListItemDto = Schemas["AccountListItemDto"];

// ----- Reconciliations -----

export type ReconciliationDto = Schemas["ReconciliationDto"];
export type ReconciliationDiscrepancyDto = Schemas["ReconciliationDiscrepancyDto"];
export type ReconciliationMatchGroupDto = Schemas["ReconciliationMatchGroupDto"];
export type ReconciliationMatchGroupItemDto = Schemas["ReconciliationMatchGroupItemDto"];
export type ReconciliationStatus = Schemas["ReconciliationStatus"];
export type DiscrepancyType = Schemas["DiscrepancyType"];
export type ResolutionType = Schemas["ResolutionType"];
export type ResolveDiscrepancyRequest = Schemas["ResolveDiscrepancyRequest"];
export type ApproveReconciliationRequest = Schemas["ApproveReconciliationRequest"];
export type StatementUploadResponse = Schemas["StatementUploadResponse"];

// ----- Dashboard -----

export type DashboardDto = Schemas["DashboardDto"];
export type BalanceByCurrencyDto = Schemas["BalanceByCurrencyDto"];
export type ActivityPointDto = Schemas["ActivityPointDto"];
export type RecentReconciliationDto = Schemas["RecentReconciliationDto"];
export type DashboardQuickStatsDto = Schemas["DashboardQuickStatsDto"];

// ============================================================================
// Frontend-only (no aparecen en OpenAPI o backend los emite como string genérico)
// ============================================================================

/**
 * String-literal unions para enums que el backend serializa como `string`
 * crudo en response DTOs (e.g. `Status = r.Status.ToString()`). El backend
 * los tiene como enums C# tipados; sólo no salen como enums OpenAPI porque
 * los response mappers los pasan por ToString(). Mantener acá da autocomplete
 * y narrowing en runtime.
 */
export type TransactionType =
  | "Debit"
  | "Credit"
  | "TransferOut"
  | "TransferIn"
  | "Fee"
  | "Interest"
  | "Adjustment";

export type TransactionStatus =
  | "Pending"
  | "Processing"
  | "Validated"
  | "Posted"
  | "Reconciled"
  | "Rejected"
  | "Reversed";

export type AccountType =
  | "Checking"
  | "Savings"
  | "Credit"
  | "Loan"
  | "Treasury"
  | "Investment";

/**
 * Forma genérica del PagedResult<T> del backend. No se autogenera porque
 * openapi-typescript no instancia genéricos: el backend emite cada caso
 * concreto (PagedTransactionsDto, ReconciliationDtoPagedResult, etc.).
 * Este alias sirve para componer paginación nueva en el frontend.
 */
export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

/**
 * Request shapes de query params para los endpoints de búsqueda. El OpenAPI
 * los modela como `parameters` (no como schemas reutilizables), así que es
 * más limpio mantenerlos a mano como interfaces TS.
 */
export interface SearchTransactionsRequest {
  accountId?: string;
  startDate?: string; // YYYY-MM-DD
  endDate?: string;
  type?: TransactionType;
  status?: TransactionStatus;
  minAmount?: number;
  maxAmount?: number;
  category?: string;
  searchText?: string;
  page?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
}

export interface SearchReconciliationsRequest {
  accountId?: string;
  startDate?: string; // YYYY-MM-DD
  endDate?: string;
  status?: ReconciliationStatus;
  page?: number;
  pageSize?: number;
}

// ----- Common -----

export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}
