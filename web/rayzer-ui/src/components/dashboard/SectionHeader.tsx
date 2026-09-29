import { cx } from "../../lib/cx";

/** SectionHeader — "Catracas", "Acessos em tempo real": linha de energia, título, subtítulo e ação. */
export interface SectionHeaderProps {
  title: string;
  subtitle?: string;
  action?: React.ReactNode;
  as?: "h2" | "h3";
  className?: string;
}

export function SectionHeader({ title, subtitle, action, as: Tag = "h2", className }: SectionHeaderProps) {
  return (
    <div className={cx("mb-2.5 flex items-center gap-2.5", className)}>
      <span aria-hidden="true" className="h-4 w-[3px] rounded-full bg-rayzer-energy" />
      <div className="min-w-0 flex-1">
        <Tag className="font-rayzer-display text-rayzer-section text-rayzer-text">{title}</Tag>
        {subtitle ? <p className="text-rayzer-caption text-rayzer-text-secondary">{subtitle}</p> : null}
      </div>
      {action}
    </div>
  );
}
