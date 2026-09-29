/**
 * O X da marca como grupo SVG, para compor dentro de outro SVG (lockups, ícone, abertura).
 * Geometria na caixa 32 × 32 — a mesma de Tokens.xaml (Rayzer.Mark.*.Geometry).
 */
export const X_PATHS = {
  flowUpper: "M3,4 L11,4 L20,16 L12,16 Z",
  flowLower: "M12,16 L20,16 L29,28 L21,28 Z",
  gateUpper: "M21,4 L29,4 L20.75,15 L16.75,9.67 Z",
  gateLower: "M11,28 L3,28 L11.25,17 L15.25,22.33 Z",
} as const;

export function XMarkDefs({ id }: { id: string }) {
  return (
    <>
      <linearGradient id={`${id}-fu`} x1="0" y1="0" x2="1" y2="1">
        <stop offset="0" stopColor="#4D94FF" />
        <stop offset="1" stopColor="#0066FF" />
      </linearGradient>
      <linearGradient id={`${id}-fl`} x1="0" y1="0" x2="1" y2="1">
        <stop offset="0" stopColor="#0066FF" />
        <stop offset="1" stopColor="#003FBF" />
      </linearGradient>
      <linearGradient id={`${id}-gu`} x1="1" y1="0" x2="0" y2="1">
        <stop offset="0" stopColor="#8AF0FF" />
        <stop offset="0.55" stopColor="#00D5FF" />
        <stop offset="1" stopColor="#009FE0" />
      </linearGradient>
      <linearGradient id={`${id}-gl`} x1="1" y1="0" x2="0" y2="1">
        <stop offset="0" stopColor="#F4F7FB" />
        <stop offset="1" stopColor="#8A9BB0" />
      </linearGradient>
      <filter id={`${id}-glow`} x="-50%" y="-50%" width="200%" height="200%">
        <feDropShadow dx="0" dy="0" stdDeviation="1.4" floodColor="#0066FF" floodOpacity="0.6" />
      </filter>
      <linearGradient id={`${id}-hl`} x1="0" y1="0" x2="0" y2="1">
        <stop offset="0" stopColor="#FFFFFF" stopOpacity="0.4" />
        <stop offset="0.45" stopColor="#FFFFFF" stopOpacity="0" />
      </linearGradient>
    </>
  );
}

/** As quatro faixas (+ realce vítreo). `mono`: tudo em currentColor. */
export function XMarkPaths({ id, mono = false }: { id: string; mono?: boolean }) {
  const f = (n: string) => (mono ? "currentColor" : `url(#${id}-${n})`);
  return (
    <>
      <path d={X_PATHS.gateUpper} fill={f("gu")} />
      <path d={X_PATHS.gateLower} fill={f("gl")} />
      <path d={X_PATHS.flowUpper} fill={f("fu")} />
      <path d={X_PATHS.flowLower} fill={f("fl")} />
      {!mono && (
        <>
          <path d={X_PATHS.flowUpper} fill={`url(#${id}-hl)`} />
          <path d={X_PATHS.gateUpper} fill={`url(#${id}-hl)`} />
        </>
      )}
    </>
  );
}

export const GLOW_FILTER = "drop-shadow(0 0 6px rgb(0 102 255 / 0.55))";
