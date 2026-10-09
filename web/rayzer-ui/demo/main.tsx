import { StrictMode, useState } from "react";
import { createRoot } from "react-dom/client";
import "./estilo.css";
import {
  AppShell, DataPanel, DataTable, EmptyState, FilterBar, IconActivity, IconCheck, IconClock, IconClose, IconCloud,
  IconEthernet, IconFolder, IconHistory, IconHome, IconList, IconPlay, IconReport, IconSearch, IconServer, IconSettings,
  IconSync, IconTurnstile, IconUsers, InfoNotice, KpiCard, OperationalClock, PageHeader, RayzerAppIcon, RayzerButton,
  RayzerDateInput, RayzerLogo, RayzerSelect, RayzerSplash, SectionHeader, Sidebar, StatusCard, StatusPill,
  TopOperationalBar, TurnstileActionBar, TurnstileCard, XAcessLogo, RayzerBrandMark,
} from "../src";

/*
 * Dados de demonstração — o mesmo cenário do modo simulação do desktop (duas catracas, um
 * acesso). Nada aqui fala com catraca ou nuvem.
 */
const MENU = [
  { id: "painel", label: "Painel ao vivo", icon: <IconHome size={18} /> },
  { id: "catracas", label: "Catracas", icon: <IconTurnstile size={18} /> },
  { id: "acessos", label: "Acessos", icon: <IconList size={18} /> },
  { id: "codigo", label: "Consultar código", icon: <IconSearch size={18} /> },
  { id: "sync", label: "Sincronização", icon: <IconSync size={18} /> },
  { id: "contas", label: "Prestação de contas", icon: <IconReport size={18} /> },
  { id: "config", label: "Configurações", icon: <IconSettings size={18} /> },
  { id: "diag", label: "Diagnóstico", icon: <IconActivity size={18} /> },
  { id: "sim", label: "Simulador", icon: <IconPlay size={18} /> },
];

type Acesso = { hora: string; catraca: string; resultado: "Liberou" | "Negou"; codigo: string };
const ACESSOS: Acesso[] = [
  { hora: "20:07:46", catraca: "Entrada 1", resultado: "Liberou", codigo: "0000123" },
  { hora: "20:07:12", catraca: "Entrada 2", resultado: "Negou", codigo: "0004410" },
];

function Painel({ tema, vazio }: { tema: "dark" | "light"; vazio?: boolean }) {
  const [ativo, setAtivo] = useState("painel");
  const [recolhido, setRecolhido] = useState(false);
  return (
    <div data-theme={tema} className="h-[768px] w-[1366px] overflow-hidden bg-rayzer-bg font-rayzer text-rayzer-text">
      <AppShell
        sidebar={
          <Sidebar
            items={MENU}
            activeId={ativo}
            onNavigate={setAtivo}
            collapsed={recolhido}
            onToggleCollapse={() => setRecolhido(!recolhido)}
            theme={tema}
            onToggleTheme={() => undefined}
            badges={<StatusPill tone="warning" label="Modo simulação" size="sm" />}
            footer={<span>Rayzer Serviços e Tecnologia · Versão 1.0.0</span>}
          />
        }
        topbar={
          <TopOperationalBar
            blocks={
              <>
                <StatusCard icon={<IconServer size={16} />} title="Serviço local" description="Operacional" tone="success" />
                <StatusCard icon={<IconTurnstile size={16} />} title="Catracas" description="2/2 online" tone="success" />
                <StatusCard icon={<IconCloud size={16} />} title="Nuvem" description="Sem sincronização" tone="neutral" />
                <StatusCard icon={<IconPlay size={16} />} title="Modo simulação" description="sem catraca física" tone="warning" />
              </>
            }
            aside={
              <div className="flex items-center gap-5">
                <OperationalClock time="20:07:55" />
                <span className="h-9 w-px bg-rayzer-border" aria-hidden="true" />
                <XAcessLogo variant="signature" height={38} />
              </div>
            }
            message={<StatusPill tone="success" label="Modo simulação (sem catraca física) — operando sem internet — nenhuma ação necessária" size="sm" />}
          />
        }
      >
        <PageHeader title="Painel ao vivo" description="2 de 2 catraca(s) operacionais" />
        <div className="grid grid-cols-5 gap-3">
          <KpiCard label="Acessos autorizados" value="1" hint="total desta base" tone="success" icon={<IconCheck size={16} />} />
          <KpiCard label="Passagens confirmadas" value="1" hint="giro da catraca confirmado" tone="brand" icon={<IconUsers size={16} />} />
          <KpiCard label="Acessos negados" value="0" hint="total desta base" tone="danger" icon={<IconClose size={16} />} />
          <KpiCard label="Fluxo · últimos 5 min" value="1" hint="entradas autorizadas" tone="info" icon={<IconHistory size={16} />} />
          <KpiCard label="Aguardando envio à nuvem" value="0" hint="sobe sozinho quando há internet" tone="success" icon={<IconCloud size={16} />} />
        </div>
        <div className="mt-5 grid grid-cols-[1fr_1.05fr] gap-4">
          <div>
            <SectionHeader title="Catracas" />
            <div className="space-y-3">
              {["01", "02"].map((n) => (
                <TurnstileCard
                  key={n}
                  code={`CATRACA ${n}`}
                  name={`Entrada ${Number(n)}`}
                  status={{ tone: "success", label: "Atendendo" }}
                  lastEvent={n === "01" ? { decision: "Liberou", when: "há 9 s" } : undefined}
                  meta={[
                    { icon: <IconSettings size={14} />, label: "Firmware", value: "4.2.0" },
                    { icon: <IconFolder size={14} />, label: "Grupo", value: "catracas" },
                    { icon: <IconEthernet size={14} />, label: "Porta", value: "3570" },
                  ]}
                  actions={
                    <TurnstileActionBar>
                      <RayzerButton variant="ghost" size="sm" icon={<IconList size={16} />}>Ver acessos</RayzerButton>
                      <RayzerButton variant="ghost" size="sm" icon={<IconActivity size={16} />}>Diagnóstico</RayzerButton>
                    </TurnstileActionBar>
                  }
                />
              ))}
            </div>
          </div>
          <DataPanel title="Acessos em tempo real" subtitle="Cada leitura aparece aqui na hora, autorizada ou negada">
            <DataTable<Acesso>
              columns={[
                { key: "hora", header: "Hora", width: "96px", mono: true },
                { key: "catraca", header: "Catraca" },
                {
                  key: "resultado", header: "Resultado",
                  render: (r) => <StatusPill tone={r.resultado === "Liberou" ? "success" : "danger"} label={r.resultado} size="sm" />,
                },
                { key: "codigo", header: "Código", mono: true, align: "right" },
              ]}
              rows={vazio ? [] : ACESSOS}
              rowKey={(r) => r.hora}
              empty={
                <EmptyState icon={<IconList size={26} />} title="Aguardando o primeiro acesso"
                            description="Cada leitura nas catracas aparece aqui na hora, autorizada ou negada."
                            note="Sem catraca física? Na instalação em modo simulação, passe um código de teste na tela Simulador." />
              }
            />
          </DataPanel>
        </div>
      </AppShell>
    </div>
  );
}

function Prancha() {
  return (
    <div data-theme="dark" className="w-[1366px] bg-rayzer-bg p-8 font-rayzer text-rayzer-text">
      <div className="grid grid-cols-2 gap-6">
        <section className="rounded-rayzer-lg border border-rayzer-border bg-rayzer-sidebar p-8">
          <p className="mb-6 text-rayzer-overline uppercase text-rayzer-text-secondary">01 · Marca corporativa — RayzerLogo</p>
          <div className="text-rayzer-white"><RayzerLogo height={96} glow /></div>
          <div className="mt-8 flex flex-wrap items-end gap-8 text-rayzer-white">
            <RayzerLogo variant="vertical" height={110} />
            <RayzerLogo variant="symbol" height={72} glow />
            <RayzerLogo mono height={40} descriptor={false} />
            <RayzerAppIcon size={80} />
          </div>
        </section>
        <section className="rounded-rayzer-lg border border-rayzer-border bg-rayzer-sidebar p-8">
          <p className="mb-6 text-rayzer-overline uppercase text-rayzer-text-secondary">02 · Produto — XAcessLogo</p>
          <div className="text-rayzer-white"><XAcessLogo height={96} /></div>
          <div className="mt-8 flex flex-wrap items-end gap-8 text-rayzer-white">
            <XAcessLogo variant="signature" height={70} />
            <XAcessLogo variant="compact" height={40} />
            <XAcessLogo variant="icon" height={96} />
            <RayzerAppIcon size={32} />
            <RayzerAppIcon size={16} />
          </div>
        </section>
        <section data-theme="light" className="rounded-rayzer-lg border border-rayzer-border bg-rayzer-bg p-8 text-rayzer-navy">
          <p className="mb-6 text-rayzer-overline uppercase text-rayzer-text-secondary">Sobre fundo claro</p>
          <div className="flex items-center gap-10"><RayzerLogo height={64} /><RayzerBrandMark size={52} mono /></div>
        </section>
        <section data-theme="light" className="rounded-rayzer-lg border border-rayzer-border bg-rayzer-bg p-8 text-rayzer-navy">
          <p className="mb-6 text-rayzer-overline uppercase text-rayzer-text-secondary">Sobre fundo claro</p>
          <div className="flex items-center gap-10"><XAcessLogo height={64} /><XAcessLogo variant="signature" height={52} mono /></div>
        </section>
      </div>
      <section className="mt-6 grid grid-cols-3 gap-6">
        <div className="rounded-rayzer-lg border border-rayzer-border bg-rayzer-card p-6">
          <p className="mb-4 text-rayzer-overline uppercase text-rayzer-text-secondary">07 · Tipografia</p>
          <p className="font-rayzer-display text-3xl font-semibold">Sora <span className="text-rayzer-small font-normal text-rayzer-text-secondary">interface / UI</span></p>
          <p className="mt-2 font-rayzer text-2xl">Inter <span className="text-rayzer-small text-rayzer-text-secondary">suporte / sistemas</span></p>
          <p className="mt-2 font-rayzer-mono text-2xl">JetBrains Mono <span className="text-rayzer-small text-rayzer-text-secondary">dados</span></p>
        </div>
        <div className="rounded-rayzer-lg border border-rayzer-border bg-rayzer-card p-6">
          <p className="mb-4 text-rayzer-overline uppercase text-rayzer-text-secondary">Formulário</p>
          <FilterBar className="mb-0 border-0 bg-transparent p-0">
            <RayzerDateInput label="De" withTime defaultValue="2026-12-20T18:00" />
            <RayzerSelect label="Catraca" options={[{ value: "", label: "Todas" }, { value: "1", label: "Entrada 1" }]} />
            <RayzerButton variant="primary" icon={<IconClock size={16} />}>Gerar</RayzerButton>
          </FilterBar>
        </div>
        <div className="space-y-3">
          <InfoNotice tone="danger" title="Catraca 02 offline há 2 min" actions={<RayzerButton size="sm">Diagnóstico</RayzerButton>}>
            Leituras na Entrada 2 não estão chegando. Verifique cabo e energia.
          </InfoNotice>
          <InfoNotice tone="warning" title="Modo simulação">Sem catraca física; os acessos são de teste.</InfoNotice>
        </div>
      </section>
    </div>
  );
}

function Demo() {
  const params = new URLSearchParams(window.location.search);
  const vista = params.get("vista") ?? "tudo";
  const [abertura, setAbertura] = useState(params.get("abertura") !== "0");
  return (
    <>
      {vista === "abertura" ? <RayzerSplash still /> : null}
      {abertura && vista === "tudo" ? <RayzerSplash onDone={() => setAbertura(false)} /> : null}
      {(vista === "tudo" || vista === "prancha") && <Prancha />}
      {(vista === "tudo" || vista === "escuro") && <Painel tema="dark" />}
      {(vista === "tudo" || vista === "claro") && <Painel tema="light" vazio />}
    </>
  );
}

createRoot(document.getElementById("raiz")!).render(
  <StrictMode>
    <Demo />
  </StrictMode>,
);
