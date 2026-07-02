/**
 * Modo demo pública (front en Vercel + API en Render free tier).
 * Se activa con NEXT_PUBLIC_DEMO_MODE=true. Las credenciales espejan los
 * defaults de Identity:Seed:DemoUser del backend y se pueden pisar por env
 * (son públicas por diseño: el usuario demo es rol Reader, sólo lectura).
 */
export const demoMode = process.env.NEXT_PUBLIC_DEMO_MODE === "true";

export const demoCredentials = {
  email: process.env.NEXT_PUBLIC_DEMO_EMAIL ?? "demo@financecore.local",
  password: process.env.NEXT_PUBLIC_DEMO_PASSWORD ?? "Demo!2026",
} as const;
