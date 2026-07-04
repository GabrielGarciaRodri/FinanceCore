"use client";

import { useState } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { Menu } from "lucide-react";
import {
  Sheet,
  SheetContent,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui/sheet";
import { FeedbackLinks } from "@/components/layout/feedback-links";
import { LogoMark } from "@/components/layout/logo-mark";
import { NAV_ITEMS } from "@/components/layout/nav-items";
import { useAuth } from "@/lib/auth/context";
import { cn } from "@/lib/utils";

/**
 * Navegación para pantallas chicas: botón hamburguesa (visible < lg) que abre
 * un Sheet lateral con los mismos links del sidebar. El sidebar desktop sigue
 * manejando la navegación en >= lg.
 */
export function MobileNav(): JSX.Element {
  const pathname = usePathname();
  const { canWrite } = useAuth();
  const [open, setOpen] = useState(false);
  const items = NAV_ITEMS.filter((item) => !item.writeOnly || canWrite);

  return (
    <Sheet open={open} onOpenChange={setOpen}>
      <SheetTrigger
        aria-label="Abrir menú"
        className="inline-flex h-9 w-9 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-accent hover:text-foreground lg:hidden"
      >
        <Menu className="h-5 w-5" />
      </SheetTrigger>
      <SheetContent side="left" className="w-64 p-0" aria-describedby={undefined}>
        <SheetTitle className="flex h-14 items-center gap-2 border-b px-4 text-sm font-semibold">
          <LogoMark className="h-5 w-5 text-primary" />
          FinanceCore
        </SheetTitle>
        <nav className="space-y-1 p-2">
          {items.map((item) => {
            const Icon = item.icon;
            const active =
              pathname === item.href || pathname?.startsWith(`${item.href}/`);
            return (
              <Link
                key={item.href}
                href={item.href}
                onClick={() => setOpen(false)}
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
        <div className="border-t p-3">
          <FeedbackLinks />
        </div>
      </SheetContent>
    </Sheet>
  );
}
