import { cx } from "../../lib/cx";

/**
 * EmptyState — a lista vazia explica por que está vazia e o que fazer. O sistema nunca
 * parece quebrado só porque ainda não há dado.
 */
export interface EmptyStateProps {
  icon: React.ReactNode;
  title: string;
  description?: string;
  /** Observação complementar, menor. */
  note?: string;
  /** Bloco adicional (ação, InfoNotice). */
  children?: React.ReactNode;
  className?: string;
}

export function EmptyState({ icon, title, description, note, children, className }: EmptyStateProps) {
  return (
    <div className={cx("mx-auto flex max-w-[420px] flex-col items-center px-6 py-8 text-center", className)} role="status">
      <span className="grid h-16 w-16 place-items-center rounded-full border border-rayzer-border bg-rayzer-blue-subtle text-rayzer-blue-fg shadow-rayzer-glow-sm">
        {icon}
      </span>
      <p className="mt-3.5 font-rayzer-display text-rayzer-section text-rayzer-text">{title}</p>
      {description ? <p className="mt-1 text-rayzer-small text-rayzer-text-secondary">{description}</p> : null}
      {note ? <p className="mt-2 text-rayzer-caption text-rayzer-text-tertiary">{note}</p> : null}
      {children ? <div className="mt-3">{children}</div> : null}
    </div>
  );
}
