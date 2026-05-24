"use client";

import { ChevronLeft, ChevronRight } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

interface Props {
  /** Cantidad de items que llegaron en la página actual. */
  itemsInPage: number;
  page: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  onPageSizeChange: (size: number) => void;
}

const PAGE_SIZE_OPTIONS = [25, 50, 100];

/**
 * Paginación sin totalCount (el endpoint /api/reconciliations devuelve
 * lista plana). Inferimos hasNext con itemsInPage === pageSize.
 */
export function PaginationBar({
  itemsInPage,
  page,
  pageSize,
  onPageChange,
  onPageSizeChange,
}: Props): JSX.Element {
  const hasPrev = page > 1;
  const hasNext = itemsInPage === pageSize;

  return (
    <div className="flex flex-col items-center justify-between gap-3 text-sm sm:flex-row">
      <div className="flex items-center gap-2">
        <span className="text-xs text-muted-foreground">Por página</span>
        <Select
          value={pageSize.toString()}
          onValueChange={(v) => onPageSizeChange(Number(v))}
        >
          <SelectTrigger className="h-8 w-[80px]">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {PAGE_SIZE_OPTIONS.map((n) => (
              <SelectItem key={n} value={n.toString()}>
                {n}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <div className="flex items-center gap-2">
        <span className="text-xs text-muted-foreground tabular-nums">
          Página {page}
        </span>
        <Button
          variant="outline"
          size="sm"
          disabled={!hasPrev}
          onClick={() => onPageChange(page - 1)}
        >
          <ChevronLeft className="h-4 w-4" />
          Anterior
        </Button>
        <Button
          variant="outline"
          size="sm"
          disabled={!hasNext}
          onClick={() => onPageChange(page + 1)}
        >
          Siguiente
          <ChevronRight className="h-4 w-4" />
        </Button>
      </div>
    </div>
  );
}
