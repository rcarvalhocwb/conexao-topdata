/**
 * Tom semântico: a cor sai do tema; o símbolo e o texto vão junto. A situação nunca
 * depende só da cor (WCAG 1.4.1).
 */
export type Tone = "success" | "danger" | "warning" | "info" | "brand" | "neutral";

/** ✓ sucesso · × perigo · ! atenção · ↻ info · ● marca · ○ neutro. */
export const toneGlyph: Record<Tone, string> = {
  success: "✓",
  danger: "×",
  warning: "!",
  info: "↻",
  brand: "●",
  neutral: "○",
};

/** Classes de texto e de fundo tingido por tom. */
export const toneClasses: Record<Tone, { text: string; subtle: string; bar: string; ring: string }> = {
  success: { text: "text-rayzer-success", subtle: "bg-rayzer-success-subtle", bar: "bg-rayzer-success", ring: "ring-rayzer-success" },
  danger: { text: "text-rayzer-danger", subtle: "bg-rayzer-danger-subtle", bar: "bg-rayzer-danger", ring: "ring-rayzer-danger" },
  warning: { text: "text-rayzer-warning", subtle: "bg-rayzer-warning-subtle", bar: "bg-rayzer-warning", ring: "ring-rayzer-warning" },
  info: { text: "text-rayzer-info", subtle: "bg-rayzer-info-subtle", bar: "bg-rayzer-info", ring: "ring-rayzer-info" },
  brand: { text: "text-rayzer-blue-fg", subtle: "bg-rayzer-blue-subtle", bar: "bg-rayzer-energy", ring: "ring-rayzer-blue" },
  neutral: { text: "text-rayzer-neutral", subtle: "bg-rayzer-neutral-subtle", bar: "bg-rayzer-neutral", ring: "ring-rayzer-neutral" },
};

/** Anel de foco padrão Rayzer: sempre visível no teclado. */
export const focusRing =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-rayzer-focus focus-visible:ring-offset-2 focus-visible:ring-offset-rayzer-bg";
