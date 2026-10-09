import { useId } from "react";
import { cx } from "../../lib/cx";
import { LETREIROS, type Lockup as LockupData } from "./letreiros";
import { XMarkDefs, XMarkPaths } from "./XMark";

/**
 * Desenha um lockup do brand board (letreiros.ts, gerado por tools/gerar-marca.py) num SVG
 * só: letreiros em vetor + o X. `height` define o tamanho; a largura segue a proporção.
 * Texto em currentColor; o apoio (descritor, tagline, "by") em `supportColor`.
 */
export interface LockupProps {
  data: LockupData;
  height: number;
  title: string;
  supportColor?: string;
  glow?: boolean;
  mono?: boolean;
  className?: string;
}

export function Lockup({ data, height, title, supportColor = "var(--rayzer-text-secondary)", glow = false, mono = false, className }: LockupProps) {
  const id = `rz${useId().replace(/:/g, "")}`;
  const width = Math.round((height * data.w) / data.h * 100) / 100;
  return (
    <svg width={width} height={height} viewBox={`0 0 ${data.w} ${data.h}`} role="img" aria-label={title} className={cx("block shrink-0", className)}>
      {!mono && (
        <defs>
          <XMarkDefs id={id} />
        </defs>
      )}
      {data.itens.map((it, i) =>
        it.tipo === "texto" ? (
          <path
            key={i}
            d={LETREIROS[it.nome].d}
            transform={`translate(${it.x} ${it.y}) scale(${it.k})`}
            fill={it.papel === "texto" || mono ? "currentColor" : supportColor}
          />
        ) : (
          <g key={i} transform={`translate(${it.x} ${it.y}) scale(${it.lado / 32})`} filter={glow && !mono ? `url(#${id}-glow)` : undefined}>
            <XMarkPaths id={id} mono={mono} />
          </g>
        ),
      )}
    </svg>
  );
}
