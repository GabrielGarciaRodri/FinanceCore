import { ImageResponse } from "next/og";

// Edge runtime a propósito: el build node de @vercel/og falla en Windows
// (fileURLToPath → "Invalid URL" al prerenderizar), lo que rompe `next build`
// local. En edge la ruta queda dinámica y Vercel la sirve sin problema.
export const runtime = "edge";
export const alt = "FinanceCore — Conciliación financiera multi-cuenta";
export const size = { width: 1200, height: 630 };
export const contentType = "image/png";

// Los paths del isotipo real usan transforms que Satori no soporta; la
// preview del link va con wordmark + motivo de barras (paleta slate de la app).
export default function OpenGraphImage(): ImageResponse {
  const bars = [88, 150, 110, 200, 160, 240];

  return new ImageResponse(
    (
      <div
        style={{
          width: "100%",
          height: "100%",
          display: "flex",
          flexDirection: "column",
          justifyContent: "space-between",
          backgroundColor: "#0f172a",
          padding: "72px 88px",
        }}
      >
        <div style={{ display: "flex", alignItems: "flex-end", gap: 18 }}>
          {bars.map((h, i) => (
            <div
              key={i}
              style={{
                width: 34,
                height: h,
                borderRadius: 8,
                backgroundColor: i === bars.length - 1 ? "#38bdf8" : "#334155",
              }}
            />
          ))}
        </div>

        <div style={{ display: "flex", flexDirection: "column", gap: 20 }}>
          <div style={{ fontSize: 92, fontWeight: 700, color: "#f8fafc" }}>
            FinanceCore
          </div>
          <div style={{ fontSize: 38, color: "#94a3b8" }}>
            Conciliación financiera multi-cuenta · multi-moneda
          </div>
        </div>

        <div style={{ display: "flex", fontSize: 28, color: "#64748b" }}>
          Demo pública · .NET 10 · Next.js 14 · PostgreSQL
        </div>
      </div>
    ),
    size
  );
}
