import { cx } from "../../lib/cx";

/** PageHeader — título da página, descrição curta e ação opcional à direita. */
export interface PageHeaderProps {
  title: string;
  description?: React.ReactNode;
  actions?: React.ReactNode;
  className?: string;
}

export function PageHeader({ title, description, actions, className }: PageHeaderProps) {
  return (
    <div className={cx("mb-5 flex items-start justify-between gap-4", className)}>
      <div className="min-w-0">
        <h1 className="font-rayzer-display text-rayzer-display text-rayzer-text">{title}</h1>
        {description ? <p className="mt-1 max-w-[900px] text-rayzer-body text-rayzer-text-secondary">{description}</p> : null}
      </div>
      {actions ? <div className="flex shrink-0 items-center gap-2">{actions}</div> : null}
    </div>
  );
}
