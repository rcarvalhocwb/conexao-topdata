import { forwardRef } from "react";
import { cx } from "../../lib/cx";
import { focusRing } from "../../lib/tone";

/**
 * RayzerButton — primary (Azul Principal, a ação da tela), secondary (padrão), ghost (ação
 * em linha, "Ver acessos") e danger. Altura 40 px para alinhar com os campos. Responde na
 * hora: sem animação de clique.
 */
export interface RayzerButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: "primary" | "secondary" | "ghost" | "danger";
  size?: "sm" | "md";
  icon?: React.ReactNode;
  loading?: boolean;
}

const variantes = {
  primary:
    "bg-rayzer-blue text-rayzer-on-brand border-rayzer-blue hover:bg-rayzer-blue-hover hover:border-rayzer-blue-hover active:bg-rayzer-blue-pressed shadow-rayzer-glow-sm",
  secondary: "bg-rayzer-card text-rayzer-text border-rayzer-border-strong hover:bg-rayzer-hover active:bg-rayzer-selected",
  ghost: "bg-transparent text-rayzer-blue-fg border-transparent hover:bg-rayzer-blue-subtle active:bg-rayzer-selected",
  danger: "bg-rayzer-danger-subtle text-rayzer-danger border-rayzer-danger hover:brightness-110",
} as const;

export const RayzerButton = forwardRef<HTMLButtonElement, RayzerButtonProps>(function RayzerButton(
  { variant = "secondary", size = "md", icon, loading = false, disabled, className, children, type = "button", ...rest },
  ref,
) {
  return (
    <button
      ref={ref}
      type={type}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      className={cx(
        "inline-flex items-center justify-center gap-2 rounded-rayzer-md border font-semibold transition-colors duration-rayzer-fast",
        "disabled:cursor-not-allowed disabled:opacity-50",
        size === "sm" ? "h-8 px-3 text-rayzer-small" : "h-rayzer-control px-4 text-rayzer-body",
        variantes[variant],
        focusRing,
        className,
      )}
      {...rest}
    >
      {loading ? <span aria-hidden="true" className="h-3.5 w-3.5 animate-spin rounded-full border-2 border-current border-t-transparent" /> : icon}
      {children}
    </button>
  );
});
