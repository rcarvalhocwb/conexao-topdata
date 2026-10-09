import { describe, expect, it } from "vitest";
import { contraste, coresXaml, variaveis } from "./util";

const escuro = variaveis("dark");
const claro = variaveis("light");

describe("tokens CSS", () => {
  it("escuro e claro definem as mesmas cores", () => {
    expect(Object.keys(claro).sort()).toEqual(Object.keys(escuro).sort());
  });

  it("o escuro usa a paleta do brand board", () => {
    expect(escuro).toMatchObject({
      "brand-blue": "#0066ff", "brand-cyan": "#00d5ff", bg: "#0b1a33", "card-bg": "#111827",
      "text-primary": "#f4f7fb", "text-secondary": "#8a9bb0", success: "#32d583", warning: "#f5a524", danger: "#f05252",
    });
  });

  // Mesma paleta nos dois stacks: a web não pode divergir do desktop (Temas/*.xaml).
  const PARES: Record<string, string> = {
    bg: "Rayzer.Background", "card-bg": "Rayzer.Surface", "bg-elevated": "Rayzer.Surface.Elevated",
    "surface-sunken": "Rayzer.Surface.Sunken", "surface-hover": "Rayzer.Surface.Hover", "surface-selected": "Rayzer.Surface.Selected",
    border: "Rayzer.Border", "border-strong": "Rayzer.Border.Strong", divider: "Rayzer.Border.Subtle",
    "text-primary": "Rayzer.Text.Primary", "text-secondary": "Rayzer.Text.Secondary", "text-tertiary": "Rayzer.Text.Tertiary",
    "brand-blue": "Rayzer.Brand.Primary", "brand-blue-hover": "Rayzer.Brand.Primary.Hover", "brand-blue-fg": "Rayzer.Brand.Primary.Foreground",
    "brand-cyan": "Rayzer.Brand.Cyan", success: "Rayzer.Success", warning: "Rayzer.Warning", danger: "Rayzer.Danger", info: "Rayzer.Info",
    "success-subtle": "Rayzer.Success.Subtle", "warning-subtle": "Rayzer.Warning.Subtle", "danger-subtle": "Rayzer.Danger.Subtle",
    "input-bg": "Rayzer.Input.Background", "input-border": "Rayzer.Input.Border", focus: "Rayzer.Focus",
    "sidebar-bg": "Rayzer.Nav.Background", "nav-text": "Rayzer.Nav.Text", "nav-hover": "Rayzer.Nav.Item.Hover", "nav-active": "Rayzer.Nav.Item.Active",
    graphite: "Rayzer.Graphite",
  };

  it.each([["dark", "src/Rayzer.Design/Temas/Escuro.xaml"], ["light", "src/Rayzer.Design/Temas/Claro.xaml"]] as const)(
    "o tema %s é igual ao do desktop",
    (tema, arquivo) => {
      const css = tema === "dark" ? escuro : claro;
      const xaml = coresXaml(arquivo);
      const diferentes = Object.entries(PARES)
        .filter(([v, k]) => css[v] !== xaml[k])
        .map(([v, k]) => `--rayzer-${v}=${css[v]} × ${k}=${xaml[k]}`);
      expect(diferentes).toEqual([]);
    },
  );

  it.each([["dark", escuro], ["light", claro]] as const)("tema %s passa em WCAG 2.2 AA", (_, t) => {
    const falhas: string[] = [];
    const exigir = (fg: string, bg: string, minimo: number) => {
      const r = contraste(t[fg]!, t[bg]!);
      if (r < minimo) falhas.push(`${fg} sobre ${bg}: ${r.toFixed(2)} < ${minimo}`);
    };
    for (const bg of ["bg", "card-bg", "bg-elevated", "surface-hover"]) {
      exigir("text-primary", bg, 4.5);
      exigir("text-secondary", bg, 4.5);
      for (const s of ["success", "warning", "danger", "info", "brand-blue-fg"]) exigir(s, bg, 4.5);
    }
    for (const s of ["success", "warning", "danger", "info"]) exigir(s, `${s}-subtle`, 4.5);
    exigir("text-on-brand", "brand-blue", 4.5);
    exigir("text-on-brand", "brand-blue-hover", 4.5);
    exigir("input-border", "input-bg", 3);
    exigir("nav-text", "sidebar-bg", 4.5);
    exigir("nav-text-muted", "sidebar-bg", 4.5);
    expect(falhas).toEqual([]);
  });
});
