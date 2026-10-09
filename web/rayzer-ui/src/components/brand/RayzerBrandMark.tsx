import { cx } from "../../lib/cx";
import { useId } from "react";
import { GLOW_FILTER, XMarkDefs, XMarkPaths } from "./XMark";

/**
 * RayzerBrandMark — o X da marca (brand board, "Símbolo isolado"): duas faixas em fluxo que
 * se encontram. A do fluxo (\) é contínua, em azul; a da passagem (/) vai do ciano ao prata.
 * Realce vítreo no alto e, com `glow`, o brilho azul controlado. Mesma geometria do desktop.
 */
export interface RayzerBrandMarkProps {
  /** Lado em px. Mínimo recomendado: 16. */
  size?: number;
  /** Brilho azul sutil — fundo escuro, cabeçalho, abertura. */
  glow?: boolean;
  /** Uma cor só (currentColor): monocromático branco, cinza ou Azul Escuro. */
  mono?: boolean;
  /** Rótulo acessível; vazio torna o símbolo decorativo. */
  title?: string;
  className?: string;
}

export function RayzerBrandMark({ size = 32, glow = false, mono = false, title = "Rayzer X", className }: RayzerBrandMarkProps) {
  const id = `rz${useId().replace(/:/g, "")}`;
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 32 32"
      className={cx("shrink-0", className)}
      role={title ? "img" : undefined}
      aria-label={title || undefined}
      aria-hidden={title ? undefined : true}
      style={glow && !mono ? { filter: GLOW_FILTER } : undefined}
    >
      {!mono && (
        <defs>
          <XMarkDefs id={id} />
        </defs>
      )}
      <XMarkPaths id={id} mono={mono} />
    </svg>
  );
}
