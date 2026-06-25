"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import {
  LayoutDashboard,
  Receipt,
  ScrollText,
  Upload,
  Wallet,
} from "lucide-react";
import { useAuth } from "@/lib/auth/context";
import { cn } from "@/lib/utils";

interface NavItem {
  href: string;
  label: string;
  icon: React.ComponentType<{ className?: string }>;
  /** Solo visible para usuarios con permiso de escritura (oculto para Reader/demo). */
  writeOnly?: boolean;
}

const NAV: NavItem[] = [
  { href: "/dashboard", label: "Dashboard", icon: LayoutDashboard },
  { href: "/transactions", label: "Transacciones", icon: Receipt },
  { href: "/reconciliations", label: "Reconciliaciones", icon: ScrollText },
  { href: "/upload", label: "Cargar archivos", icon: Upload, writeOnly: true },
];

export function AppSidebar(): JSX.Element {
  const pathname = usePathname();
  const { canWrite } = useAuth();
  const items = NAV.filter((item) => !item.writeOnly || canWrite);

  return (
    <aside className="hidden w-56 shrink-0 border-r bg-muted/30 lg:flex lg:flex-col">
      <div className="flex h-14 items-center gap-2 border-b px-4">
        <Wallet className="h-5 w-5 text-primary" />
        <span className="text-sm font-semibold">FinanceCore</span>
      </div>
      <nav className="flex-1 space-y-1 p-2">
        {items.map((item) => {
          const Icon = item.icon;
          const active =
            pathname === item.href || pathname?.startsWith(`${item.href}/`);
          return (
            <Link
              key={item.href}
              href={item.href}
              className={cn(
                "flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors",
                active
                  ? "bg-primary text-primary-foreground"
                  : "text-muted-foreground hover:bg-accent hover:text-foreground"
              )}
            >
              <Icon className="h-4 w-4" />
              {item.label}
            </Link>
          );
        })}
      </nav>
      <div className="border-t p-3 text-xs text-muted-foreground">
        v0.1 — fase F
      </div>
    </aside>
  );
}
