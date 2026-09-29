import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { DataTable, EmptyState, InfoNotice, KpiCard, RayzerButton, RayzerInput, StatusPill, TurnstileCard, IconList } from "../src";

const html = (el: React.ReactElement) => renderToStaticMarkup(el);

describe("componentes", () => {
  it("situação nunca é só cor: glifo e texto", () => {
    const h = html(<StatusPill tone="success" label="Operacional" />);
    expect(h).toContain("✓");
    expect(h).toContain("Operacional");
    expect(html(<StatusPill tone="danger" label="Offline" />)).toContain("×");
  });

  it("campo tem rótulo ligado, dica e erro anunciados", () => {
    const h = html(<RayzerInput label="Código" hint="Como no cartão" error="Obrigatório" />);
    const id = h.match(/<input[^>]* id="([^"]+)"/)![1];
    expect(h).toContain(`for="${id}"`);
    expect(h).toContain('aria-invalid="true"');
    expect(h).toMatch(/aria-describedby="[^"]+-erro"/);
  });

  it("botão carregando fica ocupado e desabilitado", () => {
    const h = html(<RayzerButton variant="primary" loading>Gerar</RayzerButton>);
    expect(h).toContain('aria-busy="true"');
    expect(h).toContain("disabled");
    expect(h).toContain('type="button"');
  });

  it("tabela vazia mostra o estado vazio, não uma grade em branco", () => {
    const h = html(
      <DataTable columns={[{ key: "a", header: "Hora" }]} rows={[]} rowKey={(_, i) => String(i)}
                 empty={<EmptyState icon={<IconList />} title="Aguardando o primeiro acesso" />} />,
    );
    expect(h).toContain("Aguardando o primeiro acesso");
    expect(h).not.toContain("<table");
  });

  it("tabela preserva o código como texto (zeros à esquerda)", () => {
    const h = html(<DataTable columns={[{ key: "codigo", header: "Código", mono: true }]} rows={[{ codigo: "000123" }]} rowKey={(r) => r.codigo} />);
    expect(h).toContain(">000123<");
    expect(h).toContain('scope="col"');
  });

  it("alerta de perigo é anunciado", () => {
    expect(html(<InfoNotice tone="danger" title="Catraca offline" />)).toContain('role="alert"');
    expect(html(<InfoNotice title="Observação" />)).toContain('role="note"');
  });

  it("KPI e cartão da catraca mostram o que existe", () => {
    expect(html(<KpiCard label="Acessos autorizados" value="1" tone="success" icon={<IconList />} />)).toContain("Acessos autorizados");
    const h = html(<TurnstileCard code="CATRACA 01" name="Entrada 1" status={{ tone: "success", label: "Atendendo" }} />);
    expect(h).toContain("Entrada 1");
    expect(h).toContain("Atendendo");
  });
});
