#!/usr/bin/env bash
# Baixa os SDKs e manuais oficiais da Topdata para vendor/topdata/sdk/.
# Os arquivos sao proprietarios e NAO sao versionados (ver .gitignore).
#
#   ./fetch-sdk.sh            # tudo
#   ./fetch-sdk.sh inner      # so a linha Inner (EasyInner)
#   ./fetch-sdk.sh facial     # so o leitor facial
set -euo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="$DIR/sdk"
FILTRO="${1:-todos}"
mkdir -p "$OUT"

ok=0; falhou=0
while IFS=$'\t' read -r nome categoria url; do
  [ "$nome" = "nome" ] && continue
  [ -z "${nome:-}" ] && continue
  if [ "$FILTRO" != "todos" ] && [ "$categoria" != "$FILTRO" ]; then continue; fi

  destino="$OUT/$nome"
  if [ -s "$destino" ]; then
    echo "  ja existe: $nome"
    ok=$((ok+1)); continue
  fi

  echo "  baixando: $nome"
  if ! curl -fsSL --retry 3 --retry-delay 2 --max-time 300 -o "$destino.parcial" "$url"; then
    echo "    FALHOU (rede ou link mudou)"
    rm -f "$destino.parcial"; falhou=$((falhou+1)); continue
  fi

  # Confere se veio o arquivo e nao uma pagina de erro HTML
  tipo="$(file -b --mime-type "$destino.parcial" 2>/dev/null || echo desconhecido)"
  case "$tipo" in
    application/zip|application/pdf|application/octet-stream)
      mv "$destino.parcial" "$destino"
      echo "    ok ($(du -h "$destino" | cut -f1), $tipo)"
      ok=$((ok+1)) ;;
    *)
      echo "    FALHOU: veio '$tipo' em vez de zip/pdf - o link provavelmente expirou"
      rm -f "$destino.parcial"; falhou=$((falhou+1)) ;;
  esac
done < "$DIR/sources.tsv"

if [ "$ok" -gt 0 ]; then
  ( cd "$OUT" && sha256sum ./* > CHECKSUMS.txt 2>/dev/null || true )
  echo
  echo "SHA-256 gravado em $OUT/CHECKSUMS.txt"
  echo "Guarde esse arquivo: e a linha de base para detectar troca de DLL em campo."
fi

echo
echo "Concluido: $ok arquivo(s), $falhou falha(s). Destino: $OUT"
[ "$falhou" -eq 0 ] || { echo "Links oficiais em: https://integrador.topdata.com.br/"; exit 1; }
