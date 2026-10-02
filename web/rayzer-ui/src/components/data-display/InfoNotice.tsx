import { cx } from "../../lib/cx";
import { type Tone, toneClasses, toneGlyph } from "../../lib/tone";

/**
 * InfoNotice — observação no rodapé de uma área, ou alerta STATUS → EXCEÇÃO → CONTEXTO →
 * AÇÃO: título (o que aconteceu), texto (contexto) e ações à direita.
 */
export interface InfoNoticeProps {
  tone?: Tone;
  title: string;
  children?: React.ReactNode;
  actions?: React.ReactNode;
  className?: string;
}

export function InfoNotice({ tone = "info", title, children, actions, className }: InfoNoticeProps) {
  const t = toneClasses[tone];
  const role = tone === "danger" || tone === "warning" ? "alert" : "note";
  return (
    <div className={cx("flex gap-2.5 rounded-rayzer-md border-l-4 px-3.5 py-2.5", t.subtle, className)} style={{ borderColor: "currentColor" }} role={role}>
      <span aria-hidden="true" className={cx("pt-px text-base font-bold leading-5", t.text)}>{toneGlyph[tone]}</span>
      <div className="min-w-0 flex-1">
        <p className="font-semibold text-rayzer-text">{title}</p>
        {children ? <div className="mt-0.5 text-rayzer-small text-rayzer-text-secondary">{children}</div> : null}
      </div>
      {actions ? <div className="flex shrink-0 items-center gap-2">{actions}</div> : null}
      <span className={cx("sr-only", t.text)} />
    </div>
  );
}
