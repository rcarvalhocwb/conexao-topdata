import { cx } from "../../lib/cx";
import { useId } from "react";
import { LETREIROS } from "./letreiros";
import { XMarkDefs, XMarkPaths } from "./XMark";

/**
 * RayzerAppIcon — ícone de aplicativo (brand board, seção 05): quadro arredondado em azul
 * profundo, borda Azul Principal e o X. `withName` põe o "acess" do produto embaixo (só a
 * partir de ~96 px; abaixo disso, favicon só com o X). Mesmo desenho de marca/svg.
 */
export interface RayzerAppIconProps {
  size?: number;
  withName?: boolean;
  title?: string;
  className?: string;
}

export function RayzerAppIcon({ size = 40, withName = false, title = "XAcess", className }: RayzerAppIconProps) {
  const id = `rz${useId().replace(/:/g, "")}`;
  const k = 150 / LETREIROS.Acess.largura;
  return (
    <svg width={size} height={size} viewBox="0 0 256 256" role="img" aria-label={title} className={cx("shrink-0", className)}>
      <defs>
        <XMarkDefs id={id} />
        <linearGradient id={`${id}-bg`} x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stopColor="#13264A" />
          <stop offset="1" stopColor="#0B1A33" />
        </linearGradient>
        <linearGradient id={`${id}-bd`} x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stopColor="#3D8BFF" />
          <stop offset="1" stopColor="#0066FF" />
        </linearGradient>
      </defs>
      <rect x="8" y="8" width="240" height="240" rx="56" fill={`url(#${id}-bg)`} stroke={`url(#${id}-bd)`} strokeWidth="6" />
      {withName ? (
        <>
          <g transform="translate(58.67 26.67) scale(4.3333)">
            <XMarkPaths id={id} />
          </g>
          <path d={LETREIROS.Acess.d} fill="#F4F7FB" transform={`translate(53 ${170 - 19.6 * k}) scale(${k})`} />
        </>
      ) : (
        <g transform="translate(40 40) scale(5.5)">
          <XMarkPaths id={id} />
        </g>
      )}
    </svg>
  );
}
