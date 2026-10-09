import { forwardRef } from "react";
import { cx } from "../../lib/cx";
import { Field, inputBox } from "./Field";

/** RayzerInput — campo de texto com rótulo, dica e erro; `mono` para códigos. */
export interface RayzerInputProps extends React.InputHTMLAttributes<HTMLInputElement> {
  label: string;
  hint?: string;
  error?: string;
  mono?: boolean;
  fieldClassName?: string;
}

export const RayzerInput = forwardRef<HTMLInputElement, RayzerInputProps>(function RayzerInput(
  { label, hint, error, mono = false, fieldClassName, className, ...rest },
  ref,
) {
  return (
    <Field label={label} hint={hint} error={error} className={fieldClassName}>
      {({ id, describedBy, invalid }) => (
        <input
          ref={ref}
          id={id}
          aria-describedby={describedBy}
          aria-invalid={invalid || undefined}
          className={cx(inputBox(invalid), mono && "font-rayzer-mono", className)}
          {...rest}
        />
      )}
    </Field>
  );
});
