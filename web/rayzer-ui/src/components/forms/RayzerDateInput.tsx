import { forwardRef } from "react";
import { cx } from "../../lib/cx";
import { Field, inputBox } from "./Field";

/**
 * RayzerDateInput — data (e hora, com `withTime`) nativa: calendário do sistema no idioma
 * do navegador. Valores no fuso do evento; a conversão para UTC é de quem consome.
 */
export interface RayzerDateInputProps extends Omit<React.InputHTMLAttributes<HTMLInputElement>, "type"> {
  label: string;
  withTime?: boolean;
  hint?: string;
  error?: string;
  fieldClassName?: string;
}

export const RayzerDateInput = forwardRef<HTMLInputElement, RayzerDateInputProps>(function RayzerDateInput(
  { label, withTime = false, hint, error, fieldClassName, className, ...rest },
  ref,
) {
  return (
    <Field label={label} hint={hint} error={error} className={fieldClassName}>
      {({ id, describedBy, invalid }) => (
        <input
          ref={ref}
          id={id}
          type={withTime ? "datetime-local" : "date"}
          aria-describedby={describedBy}
          aria-invalid={invalid || undefined}
          className={cx(inputBox(invalid), "[color-scheme:inherit]", className)}
          {...rest}
        />
      )}
    </Field>
  );
});
