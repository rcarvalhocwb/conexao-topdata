import { cx } from "../../lib/cx";
import { type Tone, toneClasses } from "../../lib/tone";

/**
 * StatusCard — bloco da barra operacional: ícone tingido, título, descrição curta na cor
 * da situação e, em sucesso/marca, o brilho azul discreto.
 */
export interface StatusCardProps {
  icon: React.ReactNode;
  title: string;
  description: string;
  tone: Tone;
  className?: string;
}

export function StatusCard({ icon, title, description, tone, className }: StatusCardProps) {
  const t = toneClasses[tone];
  return (
    <div
      className={cx(
        "flex min-w-[168px] items-center gap-2.5 rounded-rayzer-md border border-rayzer-border bg-rayzer-sunken py-2 pl-3 pr-3.5",
        className,
      )}
      role="group"
      aria-label={`${title}: ${description}`}
    >
      <span className={cx("grid h-8 w-8 shrink-0 place-items-center rounded-rayzer-md", t.subtle, t.text)}>{icon}</span>
      <span className="flex min-w-0 flex-col">
        <span className="text-rayzer-small font-semibold text-rayzer-text">{title}</span>
        <span className={cx("truncate text-rayzer-caption font-medium", t.text)}>{description}</span>
      </span>
    </div>
  );
}
