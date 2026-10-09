import { Lockup } from "./Lockup";
import { LOCKUPS } from "./letreiros";
import { RayzerBrandMark } from "./RayzerBrandMark";

/**
 * RayzerLogo — a marca corporativa RAYZER X (Rayzer Serviços e Tecnologia LTDA). Não é o
 * produto: para o XAcess use XAcessLogo. Brand board, seção 03:
 *
 * - `horizontal` (principal): RAYZER + X; `descriptor` acrescenta "SERVIÇOS E TECNOLOGIA LTDA".
 * - `vertical`: X em cima, RAYZER e o descritor embaixo.
 * - `symbol`: só o X (barra recolhida, favicon).
 *
 * Cor do letreiro: currentColor (use `text-rayzer-nav-text-active` na barra lateral).
 * Respiro mínimo: metade da altura do X em volta. Mínimos: 24 px de altura (horizontal sem
 * descritor), 40 px (com descritor), 16 px (símbolo).
 */
export interface RayzerLogoProps {
  variant?: "horizontal" | "vertical" | "symbol";
  /** "SERVIÇOS E TECNOLOGIA LTDA" sob o RAYZER. Padrão: true. */
  descriptor?: boolean;
  /** Altura em px. */
  height?: number;
  glow?: boolean;
  mono?: boolean;
  supportColor?: string;
  className?: string;
}

export function RayzerLogo({ variant = "horizontal", descriptor = true, height = 40, glow = false, mono = false, supportColor, className }: RayzerLogoProps) {
  if (variant === "symbol") {
    return <RayzerBrandMark size={height} glow={glow} mono={mono} className={className} />;
  }
  const data = variant === "vertical" ? LOCKUPS.CorpVertical : descriptor ? LOCKUPS.CorpHorizontal : LOCKUPS.CorpHorizontalSimples;
  return (
    <Lockup
      data={data}
      height={height}
      title="Rayzer X — Serviços e Tecnologia"
      glow={glow}
      mono={mono}
      supportColor={supportColor}
      className={className}
    />
  );
}
