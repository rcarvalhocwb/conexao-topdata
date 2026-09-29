import { cx } from "../../lib/cx";

/**
 * TopOperationalBar — STATUS → EXCEÇÃO → CONTEXTO → AÇÃO, no topo de toda tela. Modular:
 * `blocks` recebe StatusCards (serviço, catracas, nuvem, simulação…), `aside` o relógio e
 * as ações, `message` a frase da situação ("Catraca 04 sem comunicação há 1 min…").
 */
export interface TopOperationalBarProps {
  blocks: React.ReactNode;
  aside?: React.ReactNode;
  message?: React.ReactNode;
  className?: string;
}

export function TopOperationalBar({ blocks, aside, message, className }: TopOperationalBarProps) {
  return (
    <header className={cx("border-b border-rayzer-border bg-rayzer-card px-6 pb-2.5 pt-3", className)}>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2.5">{blocks}</div>
        {aside ? <div className="flex items-center gap-4">{aside}</div> : null}
      </div>
      {message ? (
        <div className="mt-2 text-rayzer-small" aria-live="assertive">
          {message}
        </div>
      ) : null}
    </header>
  );
}

/** Relógio operacional no fuso do evento (padrão: Brasília). */
export function OperationalClock({ time, zoneLabel = "Horário de Brasília" }: { time: string; zoneLabel?: string }) {
  return (
    <div className="flex flex-col items-end leading-tight">
      <span className="font-rayzer-mono text-lg font-semibold text-rayzer-text" aria-label="Hora do evento">{time}</span>
      <span className="text-rayzer-caption text-rayzer-text-secondary">{zoneLabel}</span>
    </div>
  );
}
