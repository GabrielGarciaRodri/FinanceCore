import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";

export default function DashboardPage(): JSX.Element {
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Dashboard</h1>
        <p className="text-sm text-muted-foreground">
          Placeholder. La Fase C llena este espacio con cards de saldos por
          moneda, actividad reciente y reconciliaciones del día.
        </p>
      </div>

      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Saldos por moneda</CardTitle>
            <CardDescription>Próximamente</CardDescription>
          </CardHeader>
          <CardContent className="text-sm text-muted-foreground">
            Conectar al endpoint <code>/api/transactions/...</code> y graficar.
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-base">Actividad reciente</CardTitle>
            <CardDescription>Próximamente</CardDescription>
          </CardHeader>
          <CardContent className="text-sm text-muted-foreground">
            Histograma de transacciones diarias.
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-base">Últimas reconciliaciones</CardTitle>
            <CardDescription>Próximamente</CardDescription>
          </CardHeader>
          <CardContent className="text-sm text-muted-foreground">
            Lista con estado y descuadre por cuenta.
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
