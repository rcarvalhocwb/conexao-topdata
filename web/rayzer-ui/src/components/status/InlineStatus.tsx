import { cx } from "../../lib/cx";
import { type Tone, toneClasses, toneGlyph } from "../../lib/tone";

/** InlineStatus — para linhas de tabela e blocos pequenos: símbolo + texto na cor do tom. */
export interface InlineStatusProps {
  tone: Tone;
  label: string;
  glyph?: string;
  icon?: React.ReactNode;
  className?: string;
}

export function InlineStatus({ tone, label, glyph, icon, className }: InlineStatusProps) {
  return (
    <span className={cx("inline-flex items-center gap-1.5 font-semibold", toneClasses[tone].text, className)}>
      {icon ?? <span aria-hidden="true" className="font-bold">{glyph ?? toneGlyph[tone]}</span>}
      <span>{label}</span>
    </span>
  );
}
