import { describe, expect, it } from "vitest";
import { SPLASH_MS } from "../src/components/brand/RayzerSplash";
import { ler } from "./util";

describe("abertura (brand board, seção 08)", () => {
  const spec = JSON.parse(ler("marca/animacao-abertura.json"));
  const css = ler("web/rayzer-ui/src/tokens/rayzer.css");
  const xaml = ler("src/Rayzer.Design/Themes/Generic.xaml");

  it("web e JSON têm a mesma duração", () => {
    expect(SPLASH_MS).toBe(spec.duracaoTotalMs);
    const saida = spec.fases.find((f: { id: string }) => f.id === "saida");
    expect(css).toContain(`animation: rz-fade-out ${saida.fimMs - saida.inicioMs}ms linear ${saida.inicioMs}ms forwards;`);
  });

  it("cada fase começa no tempo do JSON (web)", () => {
    const inicio = (id: string) => spec.fases.find((f: { id: string }) => f.id === id).inicioMs;
    expect(css).toContain(`rz-draw 600ms var(--rz-ease) ${inicio("fluxos")}ms`);
    expect(css).toContain(`rz-mark 600ms var(--rz-ease) ${inicio("simbolo")}ms`);
  });

  it("o desktop termina no mesmo instante", () => {
    const m = xaml.match(/Storyboard\.TargetName="Raiz" Storyboard\.TargetProperty="Opacity" From="1" To="0" BeginTime="0:0:([\d.]+)" Duration="0:0:([\d.]+)"/);
    expect(m).not.toBeNull();
    expect(Math.round((Number(m![1]) + Number(m![2])) * 1000)).toBe(spec.duracaoTotalMs);
  });

  it("movimento reduzido desliga a abertura", () => {
    expect(css).toMatch(/prefers-reduced-motion: reduce\)\s*\{\s*\.rayzer-splash \{ display: none; \}/);
  });
});
