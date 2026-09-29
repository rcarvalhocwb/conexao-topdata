import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

export const RAIZ_PACOTE = resolve(dirname(fileURLToPath(import.meta.url)), "..");
export const RAIZ_REPO = resolve(RAIZ_PACOTE, "../..");
export const ler = (caminho: string) => readFileSync(resolve(RAIZ_REPO, caminho), "utf8");

/** Variáveis --rayzer-* de um bloco do CSS (escuro ou claro), só as cores hex. */
export function variaveis(tema: "dark" | "light"): Record<string, string> {
  const css = ler("web/rayzer-ui/src/tokens/rayzer.css");
  const inicio = tema === "dark" ? css.indexOf(':root,\n[data-theme="dark"]') : css.indexOf('[data-theme="light"] {');
  const bloco = css.slice(inicio, css.indexOf("}", inicio));
  const saida: Record<string, string> = {};
  for (const m of bloco.matchAll(/--rayzer-([a-z0-9-]+):\s*(#[0-9a-fA-F]{6});/g)) saida[m[1]!] = m[2]!.toLowerCase();
  return saida;
}

/** Cores de um tema XAML do desktop: chave → #rrggbb. */
export function coresXaml(arquivo: string): Record<string, string> {
  const xaml = ler(arquivo);
  const saida: Record<string, string> = {};
  for (const m of xaml.matchAll(/<SolidColorBrush x:Key="([^"]+)" Color="#([0-9A-Fa-f]{6})"/g)) saida[m[1]!] = `#${m[2]!.toLowerCase()}`;
  return saida;
}

export function contraste(a: string, b: string): number {
  const lum = (hex: string) => {
    const c = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255).map((v) => (v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4));
    return 0.2126 * c[0]! + 0.7152 * c[1]! + 0.0722 * c[2]!;
  };
  const [x, y] = [lum(a), lum(b)];
  return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05);
}
