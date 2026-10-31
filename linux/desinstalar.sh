#!/usr/bin/env bash
# InfoPC para Linux - desinstalador. Remove só o que o instalador criou, na sua conta.
#
#   bash linux/desinstalar.sh           remove o programa e o atalho; mantém suas configurações (dados/)
#   bash linux/desinstalar.sh --tudo    remove também as configurações e o histórico
#
# Não remove o .NET (~/.dotnet), caso você o use para outras coisas, nem pacotes do sistema que você tenha instalado.

set -euo pipefail

PREFIX="${HOME}/.local/opt/InfoPC"
BIN_DIR="${HOME}/.local/bin"
APPS_DIR="${XDG_DATA_HOME:-${HOME}/.local/share}/applications"
MARK=".instalado-pelo-infopc"   # gravada pelo instalar.sh: prova que a pasta é do InfoPC mesmo depois de o programa sair
ALL=0

while [ $# -gt 0 ]; do
  case "$1" in
    --tudo) ALL=1 ;;
    --prefix) [ $# -ge 2 ] || { echo "--prefix precisa de uma pasta" >&2; exit 1; }; PREFIX="$2"; shift ;;
    --ajuda|-h|--help) sed -n '2,8p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Opção desconhecida: $1 (use --ajuda)" >&2; exit 1 ;;
  esac
  shift
done

# nunca mexe numa pasta que tenha algo além do que o instalador e o programa criam (ex.: --prefix apontando para ~/Documentos)
# nem numa que nunca teve o InfoPC. dotglob: o * pega também os nomes ocultos, inclusive os que começam com ".." (..algo)
if [ -d "$PREFIX" ]; then
  shopt -s dotglob nullglob
  for entry in "$PREFIX"/*; do
    case "$(basename "$entry")" in
      InfoPC|InfoPC.novo|LICENSE.txt|THIRD-PARTY-NOTICES.txt|LEIA-ME.txt|infopc.png|dados|"$MARK") ;;
      *) echo "A pasta $PREFIX tem \"$(basename "$entry")\", que não é do InfoPC: não vou apagar nada ali." >&2; exit 1 ;;
    esac
  done
  shopt -u dotglob nullglob
  if [ ! -f "$PREFIX/InfoPC" ] && [ ! -f "$PREFIX/$MARK" ]; then
    echo "A pasta $PREFIX não tem o InfoPC nem a marca $MARK do instalador: não vou apagar nada ali." >&2
    echo "Se ela for de uma instalação antiga do InfoPC, apague-a você mesmo." >&2
    exit 1
  fi
fi

removed=0
if [ -e "$BIN_DIR/infopc" ] || [ -L "$BIN_DIR/infopc" ]; then
  # só remove o link se ele apontar para a nossa instalação
  if [ "$(readlink "$BIN_DIR/infopc" 2>/dev/null || true)" = "$PREFIX/InfoPC" ]; then rm -f "$BIN_DIR/infopc"; echo "✓ Comando infopc removido."; removed=1; fi
fi
if [ -f "$APPS_DIR/infopc.desktop" ]; then rm -f "$APPS_DIR/infopc.desktop"; command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$APPS_DIR" >/dev/null 2>&1 || true; echo "✓ Atalho do menu removido."; removed=1; fi

if [ -d "$PREFIX" ]; then
  # apaga só o que é do programa, item por item; a pasta só sai se ficar vazia
  rm -f "$PREFIX/InfoPC" "$PREFIX/InfoPC.novo" "$PREFIX/LICENSE.txt" "$PREFIX/THIRD-PARTY-NOTICES.txt" "$PREFIX/LEIA-ME.txt" "$PREFIX/infopc.png"
  if [ "$ALL" = 1 ]; then rm -rf "$PREFIX/dados"; fi
  if [ -e "$PREFIX/dados" ] || [ -L "$PREFIX/dados" ]; then
    : > "$PREFIX/$MARK"   # a marca fica com as configurações (instalação antiga ganha a sua aqui), para o --tudo depois
    echo "✓ Programa removido. Suas configurações continuam em $PREFIX/dados (use --tudo para apagar)."
  else
    rm -f "$PREFIX/$MARK"
    if rmdir "$PREFIX" 2>/dev/null; then
      if [ "$ALL" = 1 ]; then echo "✓ Programa e configurações removidos de $PREFIX."; else echo "✓ Programa removido de $PREFIX."; fi
    else
      echo "✓ Programa removido. A pasta $PREFIX não ficou vazia e foi mantida."
    fi
  fi
  removed=1
fi
# configurações no plano B (quando a pasta do programa não era gravável)
FALLBACK="${XDG_DATA_HOME:-${HOME}/.local/share}/InfoPC"
if [ "$ALL" = 1 ] && [ -d "$FALLBACK" ]; then rm -rf "$FALLBACK"; echo "✓ Configurações removidas de $FALLBACK."; removed=1; fi

[ "$removed" = 1 ] || echo "Não encontrei uma instalação do InfoPC em $PREFIX."
exit 0
