import { cx } from "../../lib/cx";
import { type Tone, toneClasses } from "../../lib/tone";

/**
 * KpiCard — um número da operação: rótulo, ícone tingido, valor grande, texto auxiliar e
 * a linha de energia na cor do tom. `disabled` apaga o cartão sem esconder o número.
 */
export interface KpiCardProps {
  label: string;
  value: React.ReactNode;
  icon: React.ReactNode;
  hint?: string;
  tone?: Tone;
  disabled?: boolean;
  /** Mostra a linha de energia na base (padrão: sim). */
  energyLine?: boolean;
  className?: string;
}

export function KpiCard({ label, value, icon, hint, tone = "neutral", disabled = false, energyLine = true, className }: KpiCardProps) {
  const t = toneClasses[tone];
  return (
    <section
      className={cx(
        "relative flex flex-col overflow-hidden rounded-rayzer-lg border border-rayzer-border bg-rayzer-card px-4 py-3.5",
        tone === "brand" && "shadow-rayzer-glow-sm",
        disabled && "opacity-55",
        className,
      )}
      aria-label={`${label}: ${typeof value === "string" || typeof value === "number" ? value : ""}`}
    >
      <div className="flex items-start justify-between gap-2">
        <span className="text-rayzer-small font-semibold text-rayzer-text-secondary">{label}</span>
        {/* Selo: fundo tingido e contorno na cor da situação (brand board, seção 09). */}
        <span
          className={cx("grid h-8 w-8 shrink-0 place-items-center rounded-rayzer-md", t.subtle, t.text)}
          style={{ boxShadow: "inset 0 0 0 1px color-mix(in srgb, currentColor 55%, transparent)" }}
        >
          {icon}
        </span>
      </div>
      <span className="mt-1.5 font-rayzer-display text-rayzer-metric tabular-nums text-rayzer-text">{value}</span>
      {hint ? <span className="text-rayzer-caption text-rayzer-text-secondary">{hint}</span> : null}
      {energyLine ? <span aria-hidden="true" className={cx("mt-3 h-0.5 w-10 rounded-full opacity-85", t.bar)} /> : null}
    </section>
  );
}
