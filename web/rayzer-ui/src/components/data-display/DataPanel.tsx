import { cx } from "../../lib/cx";

/** DataPanel — cartão de dados: cabeçalho (título, subtítulo), ferramentas à direita e o conteúdo. */
export interface DataPanelProps {
  title?: string;
  subtitle?: string;
  toolbar?: React.ReactNode;
  children: React.ReactNode;
  className?: string;
}

export function DataPanel({ title, subtitle, toolbar, children, className }: DataPanelProps) {
  return (
    <section className={cx("flex min-h-0 flex-col overflow-hidden rounded-rayzer-lg border border-rayzer-border bg-rayzer-card", className)} aria-label={title}>
      {title ? (
        <header className="flex items-center justify-between gap-3 px-4 pb-2.5 pt-3">
          <div className="min-w-0">
            <h2 className="font-rayzer-display text-rayzer-section text-rayzer-text">{title}</h2>
            {subtitle ? <p className="text-rayzer-caption text-rayzer-text-secondary">{subtitle}</p> : null}
          </div>
          {toolbar}
        </header>
      ) : null}
      <div className="relative min-h-0 flex-1">{children}</div>
    </section>
  );
}
