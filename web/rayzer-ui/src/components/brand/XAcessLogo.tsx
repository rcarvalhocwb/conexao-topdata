import { Lockup } from "./Lockup";
import { LOCKUPS } from "./letreiros";
import { RayzerAppIcon } from "./RayzerAppIcon";

/**
 * XAcessLogo — o produto XAcess, controle de acesso inteligente, by RAYZER X. O X da marca
 * abre o nome ("Xacess"). Brand board, seção 04:
 *
 * - `principal`: com a tagline "CONTROLE DE ACESSO INTELIGENTE".
 * - `signature`: com o endosso "by RAYZER X" (cabeçalho do sistema, documentos).
 * - `compact`: só "Xacess".
 * - `icon`: o ícone do produto (quadro arredondado).
 */
export interface XAcessLogoProps {
  variant?: "principal" | "signature" | "compact" | "icon";
  height?: number;
  glow?: boolean;
  mono?: boolean;
  supportColor?: string;
  className?: string;
}

export function XAcessLogo({ variant = "principal", height = 40, glow = false, mono = false, supportColor, className }: XAcessLogoProps) {
  if (variant === "icon") {
    return <RayzerAppIcon size={height} withName={height >= 96} className={className} />;
  }
  const data = variant === "signature" ? LOCKUPS.ProdutoAssinatura : variant === "compact" ? LOCKUPS.ProdutoCompacto : LOCKUPS.ProdutoPrincipal;
  const title = variant === "signature" ? "XAcess by Rayzer X" : "XAcess — Controle de acesso inteligente";
  return <Lockup data={data} height={height} title={title} glow={glow} mono={mono} supportColor={supportColor} className={className} />;
}
