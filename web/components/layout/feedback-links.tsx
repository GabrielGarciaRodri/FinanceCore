import { Github, Mail } from "lucide-react";

const GITHUB_FEEDBACK_URL =
  "https://github.com/GabrielGarciaRodri/FinanceCore/issues/new?template=feedback.yml";

// Mail opcional vía env para no hardcodear una casilla personal en un repo
// público (harvesters de spam). Sin la var, queda sólo el link a Issues.
const FEEDBACK_EMAIL = process.env.NEXT_PUBLIC_FEEDBACK_EMAIL;

const MAILTO_URL = FEEDBACK_EMAIL
  ? `mailto:${FEEDBACK_EMAIL}?subject=${encodeURIComponent(
      "Feedback — FinanceCore demo"
    )}`
  : null;

/** Links de feedback (GitHub Issues con template + mailto opcional). */
export function FeedbackLinks(): JSX.Element {
  return (
    <div className="space-y-1">
      <a
        href={GITHUB_FEEDBACK_URL}
        target="_blank"
        rel="noopener noreferrer"
        className="flex items-center gap-2 rounded-md px-1 py-1 text-xs text-muted-foreground transition-colors hover:text-foreground"
      >
        <Github className="h-3.5 w-3.5" />
        Reportar feedback / bug
      </a>
      {MAILTO_URL && (
        <a
          href={MAILTO_URL}
          className="flex items-center gap-2 rounded-md px-1 py-1 text-xs text-muted-foreground transition-colors hover:text-foreground"
        >
          <Mail className="h-3.5 w-3.5" />
          Escribir por mail
        </a>
      )}
    </div>
  );
}
