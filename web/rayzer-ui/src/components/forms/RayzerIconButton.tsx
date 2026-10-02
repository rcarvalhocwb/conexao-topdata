import { forwardRef } from "react";
import { cx } from "../../lib/cx";
import { focusRing } from "../../lib/tone";

/** RayzerIconButton — só ícone; `label` é obrigatório (vira aria-label e dica). */
export interface RayzerIconButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  label: string;
  icon: React.ReactNode;
  size?: "sm" | "md";
}

export const RayzerIconButton = forwardRef<HTMLButtonElement, RayzerIconButtonProps>(function RayzerIconButton(
  { label, icon, size = "md", className, type = "button", ...rest },
  ref,
) {
  return (
    <button
      ref={ref}
      type={type}
      aria-label={label}
      title={label}
      className={cx(
        "inline-grid place-items-center rounded-rayzer-md text-rayzer-text-secondary transition-colors duration-rayzer-fast hover:bg-rayzer-hover hover:text-rayzer-text",
        "disabled:cursor-not-allowed disabled:opacity-50",
        size === "sm" ? "h-8 w-8" : "h-9 w-9",
        focusRing,
        className,
      )}
      {...rest}
    >
      {icon}
    </button>
  );
});
