import { cx } from "../../lib/cx";
import { RayzerAppIcon } from "../brand/RayzerAppIcon";
import { RayzerLogo } from "../brand/RayzerLogo";
import { SidebarNavItem } from "./SidebarNavItem";
import { IconMenu, IconMoon, IconSun } from "../../icons";

export interface SidebarItem {
  id: string;
  label: string;
  icon: React.ReactNode;
  disabled?: boolean;
}

/**
 * Sidebar — fixa, Azul Escuro nos dois temas (a âncora da marca). Logo no topo; grupo de
 * navegação; rodapé com selos (modo simulação), tema, recolher e a assinatura
 * institucional. Recolhida: 72 px, só ícones.
 */
export interface SidebarProps {
  items: SidebarItem[];
  activeId: string;
  onNavigate: (id: string) => void;
  collapsed?: boolean;
  onToggleCollapse?: () => void;
  theme?: "dark" | "light";
  onToggleTheme?: () => void;
  /** Selos e avisos permanentes (ex.: "MODO SIMULAÇÃO"). */
  badges?: React.ReactNode;
  /** Linha institucional (empresa, versão). */
  footer?: React.ReactNode;
  className?: string;
}

export function Sidebar({
  items,
  activeId,
  onNavigate,
  collapsed = false,
  onToggleCollapse,
  theme = "dark",
  onToggleTheme,
  badges,
  footer,
  className,
}: SidebarProps) {
  return (
    <aside
      className={cx(
        "flex shrink-0 flex-col bg-rayzer-sidebar transition-[width] duration-rayzer-normal ease-rayzer",
        collapsed ? "w-[72px]" : "w-[248px]",
        className,
      )}
      aria-label="Navegação principal"
    >
      {/* Arquitetura de marca (brand board, seção 09): a empresa no alto, o produto em uso
          logo abaixo. Um bloco de contexto, não um seletor — hoje há um produto só. */}
      <div className={cx("px-4 pb-4 pt-5 text-rayzer-nav-text-active", collapsed && "flex flex-col items-center px-0")}>
        <RayzerLogo variant={collapsed ? "symbol" : "horizontal"} descriptor={false} height={30} glow />
        <div
          className={cx(
            "mt-4 flex items-center gap-2.5 rounded-rayzer-md border border-[color:var(--rayzer-nav-hover)] bg-rayzer-nav-hover",
            collapsed ? "p-1.5" : "px-2.5 py-2",
          )}
          title="XAcess — Controle de acesso inteligente"
        >
          <RayzerAppIcon size={32} />
          {!collapsed && (
            <span className="min-w-0">
              <span className="block font-rayzer-display text-[15px] font-semibold leading-5 text-rayzer-nav-text-active">XAcess</span>
              <span className="block text-rayzer-caption text-rayzer-nav-text-muted">Controle de acesso</span>
            </span>
          )}
        </div>
      </div>
      <nav className="flex-1 space-y-1 overflow-y-auto px-2">
        {items.map((item) => (
          <SidebarNavItem
            key={item.id}
            icon={item.icon}
            label={item.label}
            active={item.id === activeId}
            disabled={item.disabled}
            collapsed={collapsed}
            onClick={() => onNavigate(item.id)}
          />
        ))}
      </nav>
      <div className="space-y-1 border-t border-[color:var(--rayzer-nav-hover)] px-2 pb-4 pt-3">
        {badges ? <div className={cx("px-1 pb-2", collapsed && "hidden")}>{badges}</div> : null}
        {onToggleTheme ? (
          <SidebarNavItem
            icon={theme === "dark" ? <IconSun size={18} /> : <IconMoon size={18} />}
            label={theme === "dark" ? "Tema claro" : "Tema escuro"}
            collapsed={collapsed}
            onClick={onToggleTheme}
          />
        ) : null}
        {onToggleCollapse ? (
          <SidebarNavItem icon={<IconMenu size={18} />} label={collapsed ? "Expandir menu" : "Recolher menu"} collapsed={collapsed} onClick={onToggleCollapse} />
        ) : null}
        {footer && !collapsed ? <div className="px-3.5 pt-3 text-rayzer-caption text-rayzer-nav-text-muted">{footer}</div> : null}
      </div>
    </aside>
  );
}
