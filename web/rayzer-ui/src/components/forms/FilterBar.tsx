import { cx } from "../../lib/cx";

/**
 * FilterBar — filtros num cartão, campos alinhados pela base (todos com 40 px), a ação
 * principal ao fim. Enter envia.
 */
export interface FilterBarProps {
  children: React.ReactNode;
  onSubmit?: () => void;
  className?: string;
  label?: string;
}

export function FilterBar({ children, onSubmit, className, label = "Filtros" }: FilterBarProps) {
  return (
    <form
      role="search"
      aria-label={label}
      className={cx("mb-3 flex flex-wrap items-end gap-3 rounded-rayzer-lg border border-rayzer-border bg-rayzer-card px-4 py-3", className)}
      onSubmit={(e) => {
        e.preventDefault();
        onSubmit?.();
      }}
    >
      {children}
    </form>
  );
}
