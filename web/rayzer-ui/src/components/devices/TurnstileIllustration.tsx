import { cx } from "../../lib/cx";
import { type Tone, toneClasses } from "../../lib/tone";

/**
 * Miniatura da catraca (tripé com leitor) em vetor; a luz do leitor acende na cor da
 * situação. É ilustração, não foto do modelo.
 */
export function TurnstileIllustration({ tone = "neutral", className }: { tone?: Tone; className?: string }) {
  return (
    <div
      className={cx("grid h-[72px] w-14 shrink-0 place-items-center rounded-rayzer-md border border-rayzer-border bg-rayzer-sunken", className)}
      aria-hidden="true"
    >
      <svg viewBox="0 0 32 42" width="40" height="52">
        <path d="M19 17 L31 14 M19 17 L29 26 M19 17 L22 6" stroke="var(--rayzer-text-tertiary)" strokeWidth="2.2" strokeLinecap="round" fill="none" />
        <rect x="5" y="10" width="14" height="30" rx="2" fill="var(--rayzer-card-bg)" stroke="var(--rayzer-border-strong)" />
        <rect x="4" y="3" width="16" height="8" rx="2" fill="var(--rayzer-graphite)" />
        <rect x="7" y="6" width="10" height="2" rx="1" className={cx("fill-current", toneClasses[tone].text)} />
        <circle cx="19" cy="17" r="2.2" fill="var(--rayzer-text-tertiary)" />
      </svg>
    </div>
  );
}
