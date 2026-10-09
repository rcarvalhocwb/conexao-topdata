import { useId } from "react";
import { cx } from "../../lib/cx";

/** Rótulo, dica e erro em volta de um campo: a mesma anatomia para Input, Select e Date. */
export interface FieldProps {
  label: string;
  hint?: string;
  error?: string;
  children: (ids: { id: string; describedBy?: string; invalid: boolean }) => React.ReactNode;
  className?: string;
}

export function Field({ label, hint, error, children, className }: FieldProps) {
  const id = useId();
  const hintId = hint ? `${id}-dica` : undefined;
  const errorId = error ? `${id}-erro` : undefined;
  const describedBy = [hintId, errorId].filter(Boolean).join(" ") || undefined;

  return (
    <div className={cx("flex flex-col", className)}>
      <label htmlFor={id} className="mb-1.5 text-rayzer-small font-semibold text-rayzer-text-secondary">
        {label}
      </label>
      {children({ id, describedBy, invalid: Boolean(error) })}
      {hint && !error ? <span id={hintId} className="mt-1 text-rayzer-caption text-rayzer-text-tertiary">{hint}</span> : null}
      {error ? <span id={errorId} className="mt-1 text-rayzer-caption font-semibold text-rayzer-danger">× {error}</span> : null}
    </div>
  );
}

/** Classes da caixa do campo: 40 px, borda 3:1, foco com anel azul e borda de 2 px. */
export const inputBox = (invalid: boolean) =>
  cx(
    "h-rayzer-control w-full rounded-rayzer-sm border bg-rayzer-input-bg px-2.5 text-rayzer-body text-rayzer-text placeholder:text-rayzer-text-tertiary",
    "transition-colors duration-rayzer-fast hover:border-rayzer-text-secondary",
    "focus:outline-none focus:ring-2 focus:ring-rayzer-focus focus:border-transparent",
    "disabled:cursor-not-allowed disabled:opacity-55",
    invalid ? "border-rayzer-danger" : "border-rayzer-input-border",
  );
