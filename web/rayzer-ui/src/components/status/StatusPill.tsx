import { cx } from "../../lib/cx";
import { type Tone, toneClasses, toneGlyph } from "../../lib/tone";

/**
 * StatusPill — situação em pílula: símbolo + texto + cor (Operacional, Online, Offline,
 * Simulação, Sincronizando, Atenção, Negado). Nunca só a cor.
 */
export interface StatusPillProps {
  tone: Tone;
  label: string;
  /** Símbolo próprio (● online, ○ offline); padrão: o do tom. */
  glyph?: string;
  size?: "sm" | "md";
  className?: string;
}

export function StatusPill({ tone, label, glyph, size = "md", className }: StatusPillProps) {
  const t = toneClasses[tone];
  return (
    <span
      className={cx(
        "inline-flex items-center gap-1.5 rounded-full font-semibold whitespace-nowrap",
        size === "sm" ? "px-2 py-0.5 text-rayzer-caption" : "px-2.5 py-1 text-rayzer-small",
        t.text,
        t.subtle,
        className,
      )}
    >
      <span aria-hidden="true" className="font-bold leading-none">{glyph ?? toneGlyph[tone]}</span>
      {label}
    </span>
  );
}
