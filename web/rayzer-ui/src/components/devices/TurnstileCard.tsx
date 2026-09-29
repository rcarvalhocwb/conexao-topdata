import { cx } from "../../lib/cx";
import type { Tone } from "../../lib/tone";
import { StatusPill } from "../status/StatusPill";
import { TurnstileIllustration } from "./TurnstileIllustration";
import { TurnstileMetaItem } from "./TurnstileMetaItem";
import { TurnstileActionBar } from "./TurnstileActionBar";

export interface TurnstileMeta {
  icon: React.ReactNode;
  label: string;
  value: React.ReactNode;
}

/**
 * TurnstileCard — CATRACA 01 · Entrada 1 · situação · último evento · meta (firmware,
 * grupo, porta, reconexões) · ações. Leitura em um relance: quem caiu aparece em vermelho
 * com ○ e o tempo sem notícia.
 */
export interface TurnstileCardProps {
  /** "CATRACA 01". */
  code: string;
  /** "Entrada 1". */
  name: string;
  status: { tone: Tone; label: string; glyph?: string };
  lastEvent?: { decision: string; when: string };
  meta?: TurnstileMeta[];
  actions?: React.ReactNode;
  className?: string;
}

export function TurnstileCard({ code, name, status, lastEvent, meta = [], actions, className }: TurnstileCardProps) {
  return (
    <article className={cx("rounded-rayzer-lg border border-rayzer-border bg-rayzer-card px-3.5 pb-2 pt-3", className)} aria-label={`${code} ${name}: ${status.label}`}>
      <div className="flex gap-3.5">
        <TurnstileIllustration tone={status.tone} />
        <div className="min-w-0 flex-1">
          <div className="flex items-start justify-between gap-2">
            <div className="min-w-0">
              <p className="text-rayzer-overline uppercase text-rayzer-text-secondary">{code}</p>
              <h3 className="truncate font-rayzer-display text-rayzer-section text-rayzer-text">{name}</h3>
            </div>
            <StatusPill tone={status.tone} label={status.label} glyph={status.glyph} />
          </div>
          {lastEvent ? (
            <p className="my-1.5 text-rayzer-body">
              <span className="text-rayzer-text-secondary">Último evento: </span>
              <span className="font-semibold text-rayzer-text">{lastEvent.decision}</span>
              <span className="text-rayzer-text-secondary"> · </span>
              <span className="text-rayzer-text">{lastEvent.when}</span>
            </p>
          ) : null}
          {meta.length > 0 ? (
            <div className="flex flex-wrap gap-x-3 gap-y-1">
              {meta.map((m) => (
                <TurnstileMetaItem key={m.label} icon={m.icon} label={m.label} value={m.value} />
              ))}
            </div>
          ) : null}
        </div>
      </div>
      {actions ? <TurnstileActionBar>{actions}</TurnstileActionBar> : null}
    </article>
  );
}
