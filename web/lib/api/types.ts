/**
 * Tipos compartidos con el backend FinanceCore.API.
 * Espejo en TypeScript de los DTOs en C#. Mantener sincronizado.
 */

// ----- Auth -----

export interface AuthUser {
  id: string;
  email: string;
  displayName: string | null;
  roles: string[];
}

export interface AuthTokenResponse {
  accessToken: string;
  expiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: AuthUser | null;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RefreshTokenRequest {
  refreshToken: string;
}

// ----- Transactions -----

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

export interface TransactionDetailDto {
  id: string;
  externalId: string;
  accountId: string;
  type: TransactionType;
  status: TransactionStatus;
  amount: number;
  currencyCode: string;
  valueDate: string;
  bookingDate: string;
  description: string | null;
  category: string | null;
  counterpartyName: string | null;
  counterpartyAccount: string | null;
  counterpartyBank: string | null;
  originalAmount: number | null;
  originalCurrency: string | null;
  exchangeRateUsed: number | null;
  reconciliationId: string | null;
  reconciledAt: string | null;
  createdAt: string;
  processedAt: string | null;
}

export interface TransactionListItemDto {
  id: string;
  externalId: string;
  accountId: string;
  type: string;
  status: string;
  amount: number;
  currencyCode: string;
  valueDate: string;
  description: string | null;
  category: string | null;
  isReconciled: boolean;
}

export interface PagedTransactionsDto {
  items: TransactionListItemDto[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

export interface TransactionSummaryDto {
  accountId: string;
  startDate: string;
  endDate: string;
  totalCount: number;
  totalDebits: number;
  totalCredits: number;
  netChange: number;
  averageTransactionAmount: number;
  largestDebit: number;
  largestCredit: number;
}

// ----- Reconciliations -----

export type ReconciliationStatus =
  | "Pending"
  | "InProgress"
  | "Completed"
  | "CompletedWithDiscrepancies"
  | "Failed";

export interface ReconciliationDto {
  id: string;
  accountId: string;
  reconciliationDate: string;
  status: ReconciliationStatus;
  totalInternalRecords: number;
  totalExternalRecords: number;
  matchedCount: number;
  unmatchedInternal: number;
  unmatchedExternal: number;
  totalInternalAmount: number;
  totalExternalAmount: number;
  discrepancyAmount: number;
  startedAt: string | null;
  completedAt: string | null;
  durationMs: number | null;
  processedBy: string;
  approvedBy: string | null;
  notes: string | null;
  discrepancies: ReconciliationDiscrepancyDto[];
}

export interface ReconciliationDiscrepancyDto {
  id: string;
  discrepancyType: string;
  internalTransactionId: string | null;
  externalReference: string | null;
  internalAmount: number | null;
  externalAmount: number | null;
  differenceAmount: number | null;
  internalDate: string | null;
  externalDate: string | null;
  isResolved: boolean;
  resolutionType: string | null;
  resolutionNotes: string | null;
  resolvedAt: string | null;
}

// ----- Common -----

export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}
