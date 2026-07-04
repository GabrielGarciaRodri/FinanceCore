"use client";

import Link from "next/link";
import { ArrowRight } from "lucide-react";
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
import { formatMoney } from "@/lib/format";
import type { AccountListItemDto } from "@/lib/api/types";

interface Props {
  accounts: AccountListItemDto[] | undefined;
  isLoading: boolean;
  isError: boolean;
  errorMessage?: string;
}

export function AccountsTable({
  accounts,
  isLoading,
  isError,
  errorMessage,
}: Props): JSX.Element {
  if (isLoading) {
    return (
      <div className="space-y-2">
        {Array.from({ length: 4 }).map((_, i) => (
          <Skeleton key={i} className="h-12 w-full" />
        ))}
      </div>
    );
  }

  if (isError) {
    return (
      <p className="rounded-md border border-destructive/30 bg-destructive/5 p-4 text-sm text-destructive">
        No fue posible cargar las cuentas.
        {errorMessage ? ` ${errorMessage}` : ""}
      </p>
    );
  }

  if (!accounts || accounts.length === 0) {
    return (
      <p className="rounded-md border p-4 text-sm text-muted-foreground">
        No hay cuentas registradas.
      </p>
    );
  }

  return (
    <div className="rounded-md border">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Cuenta</TableHead>
            <TableHead>Tipo</TableHead>
            <TableHead>Moneda</TableHead>
            <TableHead className="text-right">Balance actual</TableHead>
            <TableHead>Estado</TableHead>
            <TableHead />
          </TableRow>
        </TableHeader>
        <TableBody>
          {accounts.map((account) => (
            <TableRow key={account.id}>
              <TableCell>
                <div className="font-medium">{account.accountName}</div>
                <div className="text-xs text-muted-foreground">
                  {account.accountNumber}
                </div>
              </TableCell>
              <TableCell className="text-sm">{account.type}</TableCell>
              <TableCell>
                <Badge variant="outline">{account.currencyCode}</Badge>
              </TableCell>
              <TableCell className="text-right font-mono text-sm tabular-nums">
                {formatMoney(account.currentBalance, account.currencyCode)}
              </TableCell>
              <TableCell>
                {account.isActive ? (
                  <Badge variant="secondary">Activa</Badge>
                ) : (
                  <Badge variant="outline" className="text-muted-foreground">
                    Inactiva
                  </Badge>
                )}
              </TableCell>
              <TableCell className="text-right">
                <Button asChild variant="ghost" size="sm">
                  <Link href={`/transactions?accountId=${account.id}`}>
                    Transacciones
                    <ArrowRight className="h-4 w-4" />
                  </Link>
                </Button>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}
