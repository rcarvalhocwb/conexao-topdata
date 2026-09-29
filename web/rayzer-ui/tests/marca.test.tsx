import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { LETREIROS, RayzerAppIcon, RayzerBrandMark, RayzerLogo, XAcessLogo } from "../src";
import { ler } from "./util";

const html = (el: React.ReactElement) => renderToStaticMarkup(el);

describe("arquitetura de marca", () => {
  it("o logo corporativo é RAYZER X, sem o produto", () => {
    const h = html(<RayzerLogo />);
    expect(h).toContain(LETREIROS.Rayzer.d);
    expect(h).toContain(LETREIROS.Descritor.d);
    expect(h).not.toContain(LETREIROS.Acess.d);
    expect(h).toContain('aria-label="Rayzer X — Serviços e Tecnologia"');
  });

  it("o logo do produto é Xacess, com tagline ou endosso da Rayzer", () => {
    const principal = html(<XAcessLogo />);
    expect(principal).toContain(LETREIROS.Acess.d);
    expect(principal).toContain(LETREIROS.Tagline.d);
    const assinatura = html(<XAcessLogo variant="signature" />);
    expect(assinatura).toContain(LETREIROS.By.d);
    expect(assinatura).toContain(LETREIROS.Rayzer.d);
    expect(assinatura).toContain('aria-label="XAcess by Rayzer X"');
  });

  it("sem descritor, a horizontal é só RAYZER X", () => {
    expect(html(<RayzerLogo descriptor={false} />)).not.toContain(LETREIROS.Descritor.d);
  });

  it("a altura define o tamanho e a largura segue a proporção", () => {
    expect(html(<RayzerLogo descriptor={false} height={30} />)).toMatch(/width="12[0-9.]+" height="30"/);
  });

  it("dois símbolos na mesma página não dividem ids de gradiente", () => {
    const h = html(
      <>
        <RayzerBrandMark />
        <RayzerBrandMark />
        <RayzerAppIcon />
      </>,
    );
    const ids = [...h.matchAll(/ id="([^"]+)"/g)].map((m) => m[1]);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it("monocromático usa só currentColor", () => {
    const h = html(<RayzerLogo mono />);
    expect(h).not.toContain("url(#");
    expect(h).not.toContain("<linearGradient");
  });

  it("os letreiros são os mesmos do desktop (Tokens.xaml)", () => {
    const tokens = ler("src/Rayzer.Design/Tokens.xaml");
    for (const [nome, { d }] of Object.entries(LETREIROS)) {
      expect(tokens).toContain(`<Geometry x:Key="Rayzer.Letreiro.${nome}">${d}</Geometry>`);
    }
  });
});
