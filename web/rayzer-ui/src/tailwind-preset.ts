import type { Config } from "tailwindcss";

/**
 * Preset Tailwind do Rayzer Design System. Toda cor aponta para uma variável --rayzer-*
 * (tokens/rayzer.css): o tema troca só com data-theme, sem recompilar classes.
 *
 * No app: presets: [rayzerPreset], e inclua "node_modules/@rayzer/ui/src/**\/*.{ts,tsx}"
 * (ou o caminho do pacote) em content.
 */
const v = (nome: string) => `var(--rayzer-${nome})`;

const rayzerPreset = {
  content: [],
  theme: {
    extend: {
      colors: {
        rayzer: {
          bg: v("bg"),
          "bg-elevated": v("bg-elevated"),
          sidebar: v("sidebar-bg"),
          card: v("card-bg"),
          sunken: v("surface-sunken"),
          hover: v("surface-hover"),
          selected: v("surface-selected"),
          border: v("border"),
          "border-strong": v("border-strong"),
          divider: v("divider"),
          overlay: v("overlay"),
          text: v("text-primary"),
          "text-secondary": v("text-secondary"),
          "text-tertiary": v("text-tertiary"),
          "text-disabled": v("text-disabled"),
          "on-brand": v("text-on-brand"),
          blue: v("brand-blue"),
          "blue-hover": v("brand-blue-hover"),
          "blue-pressed": v("brand-blue-pressed"),
          "blue-fg": v("brand-blue-fg"),
          "blue-subtle": v("brand-blue-subtle"),
          cyan: v("brand-cyan"),
          navy: v("brand-navy"),
          graphite: v("graphite"),
          gray: v("gray"),
          white: v("white"),
          success: v("success"),
          "success-subtle": v("success-subtle"),
          danger: v("danger"),
          "danger-subtle": v("danger-subtle"),
          warning: v("warning"),
          "warning-subtle": v("warning-subtle"),
          info: v("info"),
          "info-subtle": v("info-subtle"),
          neutral: v("neutral"),
          "neutral-subtle": v("neutral-subtle"),
          "input-bg": v("input-bg"),
          "input-border": v("input-border"),
          focus: v("focus"),
          "nav-text": v("nav-text"),
          "nav-text-active": v("nav-text-active"),
          "nav-text-muted": v("nav-text-muted"),
          "nav-hover": v("nav-hover"),
          "nav-active": v("nav-active"),
          "nav-indicator": v("nav-indicator"),
        },
      },
      fontFamily: {
        // Tipografia do brand board: Inter (suporte, sistemas), Sora (interface, títulos),
        // JetBrains Mono (técnica, logs, dados). Carregadas por "@rayzer/ui/fonts.css".
        rayzer: ["Inter", '"Segoe UI Variable Text"', '"Segoe UI"', "system-ui", "sans-serif"],
        "rayzer-display": ["Sora", '"Segoe UI Variable Display"', '"Segoe UI"', "system-ui", "sans-serif"],
        "rayzer-mono": ['"JetBrains Mono"', '"Cascadia Mono"', "Consolas", "ui-monospace", "monospace"],
      },
      fontSize: {
        "rayzer-overline": ["11px", { lineHeight: "14px", letterSpacing: "0.08em", fontWeight: "600" }],
        "rayzer-caption": ["12px", { lineHeight: "16px" }],
        "rayzer-small": ["13px", { lineHeight: "18px" }],
        "rayzer-body": ["14px", { lineHeight: "20px" }],
        "rayzer-section": ["15px", { lineHeight: "20px", fontWeight: "600" }],
        "rayzer-title": ["20px", { lineHeight: "26px", fontWeight: "600" }],
        "rayzer-display": ["26px", { lineHeight: "32px", fontWeight: "600", letterSpacing: "-0.01em" }],
        "rayzer-metric": ["32px", { lineHeight: "36px", fontWeight: "600", letterSpacing: "-0.02em" }],
      },
      spacing: {
        "rayzer-1": "4px",
        "rayzer-2": "8px",
        "rayzer-3": "12px",
        "rayzer-4": "16px",
        "rayzer-5": "20px",
        "rayzer-6": "24px",
        "rayzer-7": "32px",
        "rayzer-8": "40px",
      },
      borderRadius: {
        "rayzer-sm": "4px",
        "rayzer-md": "8px",
        "rayzer-lg": "12px",
      },
      boxShadow: {
        "rayzer-sm": v("shadow-sm"),
        "rayzer-md": v("shadow-md"),
        "rayzer-lg": v("shadow-lg"),
        "rayzer-glow": v("glow-blue"),
        "rayzer-glow-sm": v("glow-blue-sm"),
      },
      backgroundImage: {
        "rayzer-energy": v("energy"),
      },
      transitionDuration: {
        "rayzer-fast": "120ms",
        "rayzer-normal": "200ms",
      },
      transitionTimingFunction: {
        rayzer: "cubic-bezier(0.2, 0, 0, 1)",
      },
      height: {
        "rayzer-control": "40px",
        "rayzer-touch": "44px",
      },
    },
  },
} satisfies Config;

export default rayzerPreset;
