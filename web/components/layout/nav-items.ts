import type { ComponentType } from "react";
import { LayoutDashboard, Receipt, ScrollText, Upload } from "lucide-react";

export interface NavItem {
  href: string;
  label: string;
  icon: ComponentType<{ className?: string }>;
  /** Solo visible para usuarios con permiso de escritura (oculto para Reader/demo). */
  writeOnly?: boolean;
}

/** Navegación principal, compartida por el sidebar (desktop) y la nav móvil. */
export const NAV_ITEMS: NavItem[] = [
  { href: "/dashboard", label: "Dashboard", icon: LayoutDashboard },
  { href: "/transactions", label: "Transacciones", icon: Receipt },
  { href: "/reconciliations", label: "Reconciliaciones", icon: ScrollText },
  { href: "/upload", label: "Cargar archivos", icon: Upload, writeOnly: true },
];
