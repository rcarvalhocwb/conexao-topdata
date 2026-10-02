import { cx } from "../../lib/cx";

/**
 * AppShell — a estrutura: barra lateral fixa à esquerda; à direita a barra operacional, a
 * barra de carregamento e a área principal com o recuo padrão (28 × 20).
 */
export interface AppShellProps {
  sidebar: React.ReactNode;
  topbar?: React.ReactNode;
  /** Carregando: o traço de energia atravessa a linha abaixo da barra superior. */
  loading?: boolean;
  children: React.ReactNode;
  className?: string;
}

export function AppShell({ sidebar, topbar, loading = false, children, className }: AppShellProps) {
  return (
    <div className={cx("flex h-full min-h-screen bg-rayzer-bg font-rayzer text-rayzer-body text-rayzer-text", className)}>
      {sidebar}
      <div className="flex min-w-0 flex-1 flex-col">
        {topbar}
        <FlowBar active={loading} />
        <main className="min-h-0 flex-1 overflow-auto px-7 py-5">{children}</main>
      </div>
    </div>
  );
}

/** FlowBar — carregando: um traço que atravessa a linha (a linguagem de movimento é fluxo). */
export function FlowBar({ active }: { active: boolean }) {
  return (
    <div className="rayzer-flow-bar relative h-[3px] overflow-hidden" role={active ? "progressbar" : undefined} aria-label={active ? "Carregando" : undefined}>
      {active ? (
        <span
          className="absolute inset-y-0 left-0 w-[30%] rounded-full bg-rayzer-energy"
          style={{ animation: "rayzer-flow var(--rayzer-motion-flow) cubic-bezier(0.4,0,0.2,1) infinite" }}
        />
      ) : null}
    </div>
  );
}
