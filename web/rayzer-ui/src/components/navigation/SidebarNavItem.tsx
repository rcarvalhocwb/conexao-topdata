import { cx } from "../../lib/cx";

/**
 * SidebarNavItem — item do menu: ícone + nome; ativo com fundo e a linha de energia ciano
 * à esquerda; recolhido, só o ícone (o nome vira dica e rótulo acessível).
 */
export interface SidebarNavItemProps {
  icon: React.ReactNode;
  label: string;
  active?: boolean;
  collapsed?: boolean;
  disabled?: boolean;
  onClick?: () => void;
}

export function SidebarNavItem({ icon, label, active = false, collapsed = false, disabled = false, onClick }: SidebarNavItemProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      aria-current={active ? "page" : undefined}
      aria-label={collapsed ? label : undefined}
      title={collapsed ? label : undefined}
      className={cx(
        "group relative flex h-rayzer-touch w-full items-center gap-3 rounded-rayzer-md px-3.5 text-left text-rayzer-body transition-colors duration-rayzer-fast",
        "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-rayzer-focus",
        active ? "bg-rayzer-nav-active font-semibold text-rayzer-nav-text-active" : "text-rayzer-nav-text hover:bg-rayzer-nav-hover hover:text-rayzer-nav-text-active",
        disabled && "cursor-not-allowed opacity-50 hover:bg-transparent",
        collapsed && "justify-center px-0",
      )}
    >
      {active ? (
        <span aria-hidden="true" className="absolute inset-y-2.5 left-0 w-[3px] rounded-full bg-rayzer-nav-indicator shadow-[0_0_8px_var(--rayzer-brand-cyan)]" />
      ) : null}
      <span className="grid w-5 shrink-0 place-items-center">{icon}</span>
      {collapsed ? null : <span className="truncate">{label}</span>}
    </button>
  );
}
