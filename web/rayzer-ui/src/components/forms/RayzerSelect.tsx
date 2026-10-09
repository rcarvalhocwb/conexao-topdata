import { forwardRef } from "react";
import { cx } from "../../lib/cx";
import { IconChevronDown } from "../../icons";
import { Field, inputBox } from "./Field";

/** RayzerSelect — seleção nativa (acessível e rápida), com a caixa e a seta Rayzer. */
export interface RayzerSelectProps extends React.SelectHTMLAttributes<HTMLSelectElement> {
  label: string;
  options: Array<{ value: string; label: string }>;
  hint?: string;
  error?: string;
  fieldClassName?: string;
}

export const RayzerSelect = forwardRef<HTMLSelectElement, RayzerSelectProps>(function RayzerSelect(
  { label, options, hint, error, fieldClassName, className, ...rest },
  ref,
) {
  return (
    <Field label={label} hint={hint} error={error} className={fieldClassName}>
      {({ id, describedBy, invalid }) => (
        <div className="relative">
          <select
            ref={ref}
            id={id}
            aria-describedby={describedBy}
            aria-invalid={invalid || undefined}
            className={cx(inputBox(invalid), "cursor-pointer appearance-none pr-9", className)}
            {...rest}
          >
            {options.map((o) => (
              <option key={o.value} value={o.value}>
                {o.label}
              </option>
            ))}
          </select>
          <IconChevronDown size={16} className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-rayzer-text-secondary" />
        </div>
      )}
    </Field>
  );
});
