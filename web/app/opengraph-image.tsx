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
// Todo CENTRADO: WhatsApp recorta la imagen a un cuadrado central para su
// thumbnail compacto, así que la marca completa debe vivir en ese cuadrado
// (x ∈ [285, 915] aprox); un layout a la izquierda sale decapitado.
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
          alignItems: "center",
          justifyContent: "center",
          gap: 36,
          backgroundColor: "#0f172a",
          padding: "56px 80px",
        }}
      >
        <div style={{ display: "flex", alignItems: "flex-end", gap: 16 }}>
          {bars.map((h, i) => (
            <div
              key={i}
              style={{
                width: 30,
                height: h * 0.75,
                borderRadius: 8,
                backgroundColor: i === bars.length - 1 ? "#38bdf8" : "#334155",
              }}
            />
          ))}
        </div>

        <div
          style={{
            display: "flex",
            flexDirection: "column",
            alignItems: "center",
            gap: 18,
          }}
        >
          <div style={{ fontSize: 88, fontWeight: 700, color: "#f8fafc" }}>
            FinanceCore
          </div>
          <div
            style={{ fontSize: 34, color: "#94a3b8", textAlign: "center" }}
          >
            Conciliación financiera multi-cuenta · multi-moneda
          </div>
        </div>

        <div style={{ display: "flex", fontSize: 26, color: "#64748b" }}>
          Demo pública · .NET 10 · Next.js 14 · PostgreSQL
        </div>
      </div>
    ),
    size
  );
}
