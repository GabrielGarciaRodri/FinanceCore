"use client";

import { useCallback, useRef, useState, type DragEvent } from "react";
import { FileIcon, UploadCloud, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

interface Props {
  /** Comma-separated MIME types or extensions, e.g. ".csv,.xlsx" */
  accept: string;
  /** Max file size in bytes (default 20MB to match backend limit) */
  maxSize?: number;
  /** Optional helper text shown below the drop area */
  helperText?: string;
  /** Currently selected file (controlled) */
  file: File | null;
  onFileSelect: (file: File | null) => void;
  /** Disable interaction (e.g. during upload) */
  disabled?: boolean;
}

const DEFAULT_MAX_SIZE = 20 * 1024 * 1024;   // 20 MB

export function FileDropzone({
  accept,
  maxSize = DEFAULT_MAX_SIZE,
  helperText,
  file,
  onFileSelect,
  disabled = false,
}: Props): JSX.Element {
  const inputRef = useRef<HTMLInputElement>(null);
  const [dragging, setDragging] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const validate = useCallback(
    (candidate: File): string | null => {
      if (candidate.size > maxSize) {
        return `Archivo excede ${(maxSize / 1024 / 1024).toFixed(0)} MB.`;
      }
      const accepts = accept.split(",").map((s) => s.trim().toLowerCase());
      const name = candidate.name.toLowerCase();
      const matches = accepts.some(
        (a) => (a.startsWith(".") ? name.endsWith(a) : candidate.type === a),
      );
      if (!matches) {
        return `Tipo no permitido. Aceptados: ${accept}.`;
      }
      return null;
    },
    [accept, maxSize],
  );

  const handleFiles = useCallback(
    (files: FileList | null) => {
      if (!files || files.length === 0) return;
      const candidate = files[0];
      const err = validate(candidate);
      if (err) {
        setError(err);
        return;
      }
      setError(null);
      onFileSelect(candidate);
    },
    [validate, onFileSelect],
  );

  const onDrop = (e: DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    setDragging(false);
    if (disabled) return;
    handleFiles(e.dataTransfer.files);
  };

  const onDragOver = (e: DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    if (disabled) return;
    setDragging(true);
  };

  const onDragLeave = () => setDragging(false);

  const openFilePicker = () => {
    if (disabled) return;
    inputRef.current?.click();
  };

  const clear = () => {
    onFileSelect(null);
    if (inputRef.current) inputRef.current.value = "";
  };

  return (
    <div className="space-y-2">
      <div
        onDrop={onDrop}
        onDragOver={onDragOver}
        onDragLeave={onDragLeave}
        onClick={openFilePicker}
        role="button"
        tabIndex={disabled ? -1 : 0}
        onKeyDown={(e) => (e.key === "Enter" || e.key === " ") && openFilePicker()}
        className={cn(
          "flex flex-col items-center justify-center gap-2 rounded-md border-2 border-dashed p-8 text-center transition-colors",
          dragging
            ? "border-primary bg-primary/5"
            : "border-muted-foreground/25 hover:border-muted-foreground/40",
          disabled && "cursor-not-allowed opacity-60",
          !disabled && "cursor-pointer",
        )}
      >
        {file ? (
          <FileBadge file={file} onClear={clear} disabled={disabled} />
        ) : (
          <>
            <UploadCloud className="h-8 w-8 text-muted-foreground" />
            <div className="text-sm">
              <span className="font-medium">Arrastrá un archivo</span>
              <span className="text-muted-foreground"> o hacé clic para elegir</span>
            </div>
            <p className="text-xs text-muted-foreground">
              {accept} · máx {(maxSize / 1024 / 1024).toFixed(0)} MB
            </p>
          </>
        )}
        <input
          ref={inputRef}
          type="file"
          className="hidden"
          accept={accept}
          disabled={disabled}
          onChange={(e) => handleFiles(e.target.files)}
        />
      </div>

      {error && <p className="text-xs text-destructive">{error}</p>}
      {!error && helperText && (
        <p className="text-xs text-muted-foreground">{helperText}</p>
      )}
    </div>
  );
}

function FileBadge({
  file,
  onClear,
  disabled,
}: {
  file: File;
  onClear: () => void;
  disabled: boolean;
}): JSX.Element {
  return (
    <div className="flex w-full max-w-sm items-center gap-3 rounded-md border bg-background p-3 text-left">
      <FileIcon className="h-5 w-5 shrink-0 text-muted-foreground" />
      <div className="flex-1 overflow-hidden">
        <div className="truncate text-sm font-medium">{file.name}</div>
        <div className="text-xs text-muted-foreground">
          {(file.size / 1024).toFixed(1)} KB
        </div>
      </div>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        className="h-7 w-7 shrink-0"
        disabled={disabled}
        onClick={(e) => {
          e.stopPropagation();
          onClear();
        }}
        aria-label="Quitar archivo"
      >
        <X className="h-3.5 w-3.5" />
      </Button>
    </div>
  );
}
