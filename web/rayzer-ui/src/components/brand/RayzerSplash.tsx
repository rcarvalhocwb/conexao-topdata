import { useEffect, useId, useState } from "react";
import { cx } from "../../lib/cx";
import { RayzerLogo } from "./RayzerLogo";
import { XAcessLogo } from "./XAcessLogo";
import { XMarkDefs, XMarkPaths } from "./XMark";

/**
 * RayzerSplash — a abertura do XAcess (brand board, seção 08): fluxos se aproximam, pulso
 * na interseção, o X se completa, marca e produto. Toca uma vez (2,9 s — tempos em
 * marca/animacao-abertura.json) e chama `onDone`. Nunca bloqueia: `pointer-events: none`;
 * com prefers-reduced-motion, não aparece e chama `onDone` na hora.
 */
export interface RayzerSplashProps {
  onDone?: () => void;
  /** Para no quadro final, sem animar (documentação, captura). */
  still?: boolean;
  className?: string;
}

export const SPLASH_MS = 2900;

const FLOWS = [
  { d: "M -20,250 C 120,250 180,170 260,170", w: 3, o: 1 },
  { d: "M 540,90 C 400,90 340,170 260,170", w: 3, o: 1 },
  { d: "M -20,205 C 130,205 190,170 260,170", w: 2, o: 0.45 },
  { d: "M 540,135 C 390,135 330,170 260,170", w: 2, o: 0.45 },
];

export function RayzerSplash({ onDone, still = false, className }: RayzerSplashProps) {
  const id = `rz${useId().replace(/:/g, "")}`;
  const [visible, setVisible] = useState(true);

  useEffect(() => {
    if (still) return;
    const reduzido = typeof window !== "undefined" && window.matchMedia?.("(prefers-reduced-motion: reduce)").matches;
    const t = window.setTimeout(() => {
      setVisible(false);
      onDone?.();
    }, reduzido ? 0 : SPLASH_MS);
    return () => window.clearTimeout(t);
  }, [still, onDone]);

  if (!visible) return null;

  const parado = still ? { animation: "none" } : undefined;
  return (
    <div
      role="status"
      aria-label="Abrindo o XAcess"
      className={cx("rayzer-splash pointer-events-none fixed inset-0 z-50 grid place-items-center bg-rayzer-sidebar", className)}
      style={parado}
    >
      <div className="relative w-[min(560px,90vw)]">
        <svg viewBox="0 0 520 340" className="block w-full" aria-hidden="true">
          <defs>
            <XMarkDefs id={id} />
            <linearGradient id={`${id}-en`} x1="0" y1="0" x2="1" y2="0">
              <stop offset="0" stopColor="#0066FF" />
              <stop offset="1" stopColor="#00D5FF" />
            </linearGradient>
            <radialGradient id={`${id}-pu`}>
              <stop offset="0" stopColor="#00D5FF" stopOpacity="0.8" />
              <stop offset="0.45" stopColor="#0066FF" stopOpacity="0.33" />
              <stop offset="1" stopColor="#0066FF" stopOpacity="0" />
            </radialGradient>
          </defs>
          {!still && (
            <>
              <g className="rz-splash-flows" style={{ filter: "drop-shadow(0 0 8px rgb(0 102 255 / 0.5))" }}>
                {FLOWS.map((f) => (
                  <path key={f.d} className="rz-splash-flow" d={f.d} pathLength={100} fill="none" stroke={`url(#${id}-en)`}
                        strokeWidth={f.w} strokeLinecap="round" opacity={f.o} />
                ))}
              </g>
              <circle className="rz-splash-pulse" cx="260" cy="170" r="120" fill={`url(#${id}-pu)`} />
              <g transform="translate(185 95) scale(4.6875)">
                <g className="rz-splash-mark">
                  <XMarkPaths id={id} />
                </g>
              </g>
            </>
          )}
        </svg>
        <div className="rz-splash-brands absolute inset-0 flex flex-col items-center justify-center gap-6 text-rayzer-nav-text-active" style={still ? { opacity: 1, animation: "none" } : undefined}>
          <RayzerLogo descriptor={false} height={46} glow />
          <XAcessLogo variant="compact" height={40} />
          <span className="rz-splash-line block h-[3px] w-[170px] rounded-full bg-rayzer-energy" style={still ? { transform: "none", animation: "none" } : undefined} />
        </div>
      </div>
    </div>
  );
}
