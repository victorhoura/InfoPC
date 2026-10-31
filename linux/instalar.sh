#!/usr/bin/env bash
# InfoPC para Linux - instalador (compila a partir do repositório e instala na sua conta, sem root).
#
#   bash linux/instalar.sh            instala em ~/.local/opt/InfoPC (pergunta antes de instalar pacotes extras)
#   bash linux/instalar.sh --yes      sem perguntas
#   bash linux/instalar.sh --ajuda    todas as opções
#
# O que faz: confere o sistema, instala o .NET SDK 8 na sua conta se faltar (~/.dotnet), compila o InfoPC em um único
# executável, copia para a pasta de instalação, cria o comando "infopc" e o atalho no menu. Pacotes do sistema (smartmontools,
# pciutils, bibliotecas gráficas...) só são instalados com a sua confirmação, e só esta etapa usa sudo.
# Para remover: bash linux/desinstalar.sh

set -euo pipefail

PREFIX="${HOME}/.local/opt/InfoPC"
BIN_DIR="${HOME}/.local/bin"
APPS_DIR="${XDG_DATA_HOME:-${HOME}/.local/share}/applications"
DOTNET_DIR="${DOTNET_INSTALL_DIR:-${HOME}/.dotnet}"
ASSUME_YES=0
NO_SHORTCUT=0
NO_DEPS=0
BUILD_ONLY=0
OUT_DIR=""

c_b=""; c_g=""; c_y=""; c_r=""; c_0=""
if [ -t 1 ]; then c_b=$'\033[1m'; c_g=$'\033[32m'; c_y=$'\033[33m'; c_r=$'\033[31m'; c_0=$'\033[0m'; fi
say()  { printf '%s\n' "$*"; }
ok()   { printf '%s✓%s %s\n' "$c_g" "$c_0" "$*"; }
info() { printf '%s•%s %s\n' "$c_b" "$c_0" "$*"; }
warn() { printf '%s!%s %s\n' "$c_y" "$c_0" "$*" >&2; }
die()  { printf '%s✗ %s%s\n' "$c_r" "$*" "$c_0" >&2; exit 1; }

usage() {
cat <<'EOF'
Uso: bash linux/instalar.sh [opções]

  --yes, -y            não pergunta nada (aceita instalar o .NET e os pacotes extras)
  --prefix PASTA       onde instalar (padrão: ~/.local/opt/InfoPC)
  --sem-atalho         não cria o atalho no menu nem o comando "infopc"
  --sem-pacotes        não instala pacotes do sistema (smartmontools, pciutils, bibliotecas gráficas...)
  --so-compilar PASTA  só compila para PASTA, sem instalar nada
  --ajuda, -h          esta ajuda

Variáveis: DOTNET_INSTALL_DIR (onde instalar o .NET, padrão ~/.dotnet).
EOF
}

while [ $# -gt 0 ]; do
  case "$1" in
    --yes|-y) ASSUME_YES=1 ;;
    --prefix) [ $# -ge 2 ] || die "--prefix precisa de uma pasta"; PREFIX="$2"; shift ;;
    --sem-atalho) NO_SHORTCUT=1 ;;
    --sem-pacotes) NO_DEPS=1 ;;
    --so-compilar) [ $# -ge 2 ] || die "--so-compilar precisa de uma pasta"; BUILD_ONLY=1; OUT_DIR="$2"; shift ;;
    --ajuda|-h|--help) usage; exit 0 ;;
    *) usage >&2; die "opção desconhecida: $1" ;;
  esac
  shift
done

ask() { # ask "pergunta" -> 0 (sim) / 1 (não); com --yes sempre sim
  [ "$ASSUME_YES" = 1 ] && return 0
  [ -t 0 ] || return 1
  local r; read -r -p "$1 [S/n] " r || return 1
  case "${r:-S}" in s|S|sim|SIM|y|Y|yes) return 0 ;; *) return 1 ;; esac
}

# ---------------------------------------------------------------- 1) o sistema
[ "$(uname -s)" = "Linux" ] || die "Este instalador é só para Linux (sistema detectado: $(uname -s))."
case "$(uname -m)" in
  x86_64|amd64) RID="linux-x64" ;;
  aarch64|arm64) RID="linux-arm64" ;;
  *) die "Arquitetura $(uname -m) não suportada. O InfoPC roda em x86_64 e arm64." ;;
esac
if ls /lib/ld-musl-* /usr/lib/ld-musl-* >/dev/null 2>&1; then
  die "Esta distribuição usa musl (Alpine): o InfoPC precisa de glibc (Ubuntu, Debian, Fedora, Arch, openSUSE e derivadas)."
fi

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/.." && pwd)"
PROJ="$HERE/src/InfoPC.Linux/InfoPC.Linux.csproj"
[ -f "$PROJ" ] || die "Não achei $PROJ. Rode este script de dentro do repositório clonado (bash linux/instalar.sh)."

OS_NAME="Linux"
if [ -r /etc/os-release ]; then
  # shellcheck disable=SC1091
  OS_NAME="$(. /etc/os-release; printf '%s' "${PRETTY_NAME:-Linux}")"
fi
info "Sistema: $OS_NAME · $(uname -m) · alvo $RID"

PM=""
for pm in apt-get dnf pacman zypper; do
  if command -v "$pm" >/dev/null 2>&1; then PM="$pm"; break; fi
done

run_root() { # executa como root: direto, via sudo ou avisa
  if [ "$(id -u)" = 0 ]; then "$@"
  elif command -v sudo >/dev/null 2>&1; then sudo "$@"
  else warn "Preciso de permissão de administrador para: $*  (instale o sudo ou rode como root)"; return 1; fi
}

# ---------------------------------------------------------------- 2) pacotes do sistema (uma pergunta só, no começo)
# obrigatórios: ferramentas para baixar o .NET e as bibliotecas que o próprio .NET usa.
# opcionais: bibliotecas gráficas para a janela abrir e programas que dão mais detalhes (S.M.A.R.T., PCI, USB, sensores).
LDCACHE="$(PATH="$PATH:/sbin:/usr/sbin" ldconfig -p 2>/dev/null || true)"   # guarda a saída: "grep -q" fechando o pipe falharia com pipefail
have_lib() { case "$LDCACHE" in *"$1"*) return 0 ;; *) return 1 ;; esac; }
have_cmd() { PATH="$PATH:/usr/sbin:/sbin" command -v "$1" >/dev/null 2>&1; }   # smartctl, dmidecode... ficam em /usr/sbin, fora do PATH de usuário em algumas distros
have_ca()  { [ -e /etc/ssl/certs/ca-certificates.crt ] || [ -e /etc/pki/tls/certs/ca-bundle.crt ] || [ -e /etc/ssl/ca-bundle.pem ] || [ -e /etc/ssl/cert.pem ]; }

need=()    # obrigatórios que faltam
extra=()   # opcionais que faltam
install_cmd=()
gui_missing=0
for l in libX11.so.6 libfontconfig.so.1 libICE.so.6 libSM.so.6; do
  if ! have_lib "$l"; then gui_missing=1; fi
done
dl_missing=0
if ! have_cmd curl && ! have_cmd wget; then dl_missing=1; fi

if [ -n "$PM" ]; then
  # nomes dos pacotes em cada família (apt = Debian/Ubuntu/Mint; dnf = Fedora/RHEL; pacman = Arch/Manjaro; zypper = openSUSE)
  case "$PM" in
    apt-get)
      install_cmd=(env DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends)   # sem "recomendados": smartmontools puxaria um servidor de e-mail
      p_dl=(curl); p_ca=(ca-certificates); p_tar=(tar); p_gz=(gzip); p_ssl=(libssl3); p_z=(zlib1g); p_cxx=(libstdc++6)
      p_gui=(libx11-6 libice6 libsm6 libfontconfig1 libxcursor1 libxrandr2 libxi6 libxext6 fonts-dejavu-core)
      p_smart=(smartmontools); p_pci=(pciutils); p_usb=(usbutils); p_xdg=(xdg-utils); p_sens=(lm-sensors); p_dmi=(dmidecode)
      p_pk=(policykit-1); if apt-cache show pkexec >/dev/null 2>&1; then p_pk=(pkexec); fi   # o nome mudou no Debian 12 / Ubuntu 24.04
      ;;
    dnf)
      install_cmd=(dnf install -y --setopt=install_weak_deps=False)
      p_dl=(curl); p_ca=(ca-certificates); p_tar=(tar); p_gz=(gzip); p_ssl=(openssl-libs); p_z=(zlib); p_cxx=(libstdc++)
      p_gui=(libX11 libICE libSM fontconfig libXcursor libXrandr libXi libXext dejavu-sans-fonts)
      p_smart=(smartmontools); p_pci=(pciutils); p_usb=(usbutils); p_xdg=(xdg-utils); p_sens=(lm_sensors); p_dmi=(dmidecode); p_pk=(polkit)
      ;;
    pacman)
      install_cmd=(pacman -S --noconfirm --needed)
      p_dl=(curl); p_ca=(ca-certificates); p_tar=(tar); p_gz=(gzip); p_ssl=(openssl); p_z=(zlib); p_cxx=(gcc-libs)
      p_gui=(libx11 libice libsm fontconfig libxcursor libxrandr libxi libxext ttf-dejavu)
      p_smart=(smartmontools); p_pci=(pciutils); p_usb=(usbutils); p_xdg=(xdg-utils); p_sens=(lm_sensors); p_dmi=(dmidecode); p_pk=(polkit)
      ;;
    zypper)
      install_cmd=(zypper --non-interactive install)
      p_dl=(curl); p_ca=(ca-certificates); p_tar=(tar); p_gz=(gzip); p_ssl=(libopenssl3); p_z=(libz1); p_cxx=(libstdc++6)
      p_gui=(libX11-6 libICE6 libSM6 fontconfig libXcursor1 libXrandr2 libXi6 libXext6 dejavu-fonts)
      p_smart=(smartmontools); p_pci=(pciutils); p_usb=(usbutils); p_xdg=(xdg-utils); p_sens=(sensors); p_dmi=(dmidecode); p_pk=(polkit)
      ;;
  esac
  if [ "$dl_missing" = 1 ]; then need+=("${p_dl[@]}"); fi
  if ! have_cmd tar; then need+=("${p_tar[@]}"); fi
  if ! have_cmd gzip; then need+=("${p_gz[@]}"); fi
  if ! have_ca; then need+=("${p_ca[@]}"); fi
  if ! have_lib "libssl.so"; then need+=("${p_ssl[@]}"); fi
  if ! have_lib "libz.so.1"; then need+=("${p_z[@]}"); fi
  if ! have_lib "libstdc++.so.6"; then need+=("${p_cxx[@]}"); fi
  if [ "$NO_DEPS" = 0 ]; then
    if [ "$gui_missing" = 1 ]; then extra+=("${p_gui[@]}"); fi
    if ! have_cmd smartctl; then extra+=("${p_smart[@]}"); fi
    if ! have_cmd lspci; then extra+=("${p_pci[@]}"); fi
    if ! have_cmd lsusb; then extra+=("${p_usb[@]}"); fi
    if ! have_cmd pkexec; then extra+=("${p_pk[@]}"); fi
    if ! have_cmd xdg-open; then extra+=("${p_xdg[@]}"); fi
    if ! have_cmd sensors; then extra+=("${p_sens[@]}"); fi
    if ! have_cmd dmidecode; then extra+=("${p_dmi[@]}"); fi
  fi
elif [ "$dl_missing" = 1 ] || ! have_cmd tar || ! have_cmd gzip; then
  die "Não reconheci o gerenciador de pacotes desta distribuição. Instale curl, tar e gzip e rode de novo."
fi

pkgs=(${need[@]+"${need[@]}"} ${extra[@]+"${extra[@]}"})   # a forma ${a[@]+...} evita "unbound variable" com array vazio em bash antigo
if [ ${#pkgs[@]} -gt 0 ]; then
  say ""
  [ ${#need[@]} -gt 0 ] && info "Necessários para instalar: ${need[*]}"
  [ ${#extra[@]} -gt 0 ] && info "Recomendados (janela, S.M.A.R.T., PCI/USB, sensores, senha na interface): ${extra[*]}"
  if ask "Instalar agora com: sudo ${install_cmd[*]} …?"; then
    if ! run_root "${install_cmd[@]}" "${pkgs[@]}"; then
      # um nome de pacote que não existe nesta versão da distribuição derruba o conjunto: tenta um por um
      warn "A instalação em conjunto falhou; tentando um pacote por vez…"
      failed=()
      for pkg in "${pkgs[@]}"; do run_root "${install_cmd[@]}" "$pkg" >/dev/null 2>&1 || failed+=("$pkg"); done
      if [ ${#failed[@]} -gt 0 ]; then warn "Não consegui instalar: ${failed[*]}. O InfoPC abre mesmo assim, só com menos detalhes."; fi
    fi
  else
    say "  Para instalar depois: sudo ${install_cmd[*]} ${pkgs[*]}"
    if [ ${#need[@]} -gt 0 ]; then die "Sem ${need[*]} não dá para baixar o .NET e compilar. Instale e rode de novo."; fi
  fi
fi
if ! have_cmd curl && ! have_cmd wget; then die "Preciso do curl ou do wget para baixar o .NET."; fi
if ! have_cmd tar; then die "Preciso do tar."; fi

# ---------------------------------------------------------------- 3) .NET SDK 8 (ou mais novo)
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1   # o InfoPC não usa ICU; assim a compilação funciona até em sistema mínimo

DOTNET=""
find_dotnet() {
  local d major
  for d in "$(command -v dotnet 2>/dev/null || true)" "$DOTNET_DIR/dotnet"; do
    [ -n "$d" ] && [ -x "$d" ] || continue
    major="$("$d" --list-sdks 2>/dev/null | sed -n 's/^\([0-9][0-9]*\)\..*/\1/p' | sort -n | tail -1 || true)"
    if [ -n "$major" ] && [ "$major" -ge 8 ]; then DOTNET="$d"; return 0; fi
  done
  return 1
}

tmp=""
if find_dotnet; then
  ok ".NET SDK encontrado: $("$DOTNET" --version) ($DOTNET)"
else
  info "O .NET SDK 8 não está instalado."
  say  "  Vou instalar o SDK na sua conta, em $DOTNET_DIR (sem root), com o script oficial da Microsoft (https://dot.net/v1/dotnet-install.sh)."
  say  "  São ~250 MB de download; o SDK só é usado para compilar."
  ask "Continuar?" || die "Sem o .NET SDK não dá para compilar. Instale o 'dotnet-sdk-8.0' pelo gerenciador de pacotes e rode de novo."
  tmp="$(mktemp -d)"
  if have_cmd curl; then curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$tmp/dotnet-install.sh" || die "Falha ao baixar o dotnet-install.sh (sem internet?)."
  else wget -qO "$tmp/dotnet-install.sh" https://dot.net/v1/dotnet-install.sh || die "Falha ao baixar o dotnet-install.sh (sem internet?)."; fi
  bash "$tmp/dotnet-install.sh" --channel 8.0 --install-dir "$DOTNET_DIR" --no-path || die "A instalação do .NET falhou."
  find_dotnet || die "O .NET foi instalado mas não responde. Veja $DOTNET_DIR."
  ok ".NET SDK instalado: $("$DOTNET" --version)"
fi

# ---------------------------------------------------------------- 4) compilar
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE" "$tmp"' EXIT
info "Compilando o InfoPC (a primeira vez baixa os pacotes do NuGet, ~150 MB, e leva alguns minutos)…"
"$DOTNET" publish "$PROJ" -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None \
  -o "$STAGE" -nologo -v quiet -clp:NoSummary || die "A compilação falhou. Para ver o erro completo: dotnet publish $PROJ -c Release -r $RID"
[ -x "$STAGE/InfoPC" ] || die "A compilação terminou mas o executável não apareceu em $STAGE."
ok "Compilado: $(du -h "$STAGE/InfoPC" | cut -f1)"

if [ "$BUILD_ONLY" = 1 ]; then
  mkdir -p "$OUT_DIR"; cp -f "$STAGE/InfoPC" "$OUT_DIR/InfoPC"
  cp -f "$REPO/LICENSE" "$OUT_DIR/LICENSE.txt" 2>/dev/null || true
  cp -f "$REPO/THIRD-PARTY-NOTICES.txt" "$OUT_DIR/THIRD-PARTY-NOTICES.txt" 2>/dev/null || true
  ok "Executável em $OUT_DIR/InfoPC"
  exit 0
fi

# ---------------------------------------------------------------- 5) instalar
info "Instalando em $PREFIX"
mkdir -p "$PREFIX"
: > "$PREFIX/.instalado-pelo-infopc"   # marca para o desinstalar.sh reconhecer a pasta (ele não apaga pasta sem ela nem o InfoPC)
cp -f "$STAGE/InfoPC" "$PREFIX/InfoPC.novo" && chmod 755 "$PREFIX/InfoPC.novo" && mv -f "$PREFIX/InfoPC.novo" "$PREFIX/InfoPC"   # troca atômica: funciona mesmo com o programa aberto
if [ -d "$PREFIX/dados" ]; then info "Suas configurações em $PREFIX/dados foram mantidas."; fi
cp -f "$REPO/LICENSE" "$PREFIX/LICENSE.txt" 2>/dev/null || true
cp -f "$REPO/THIRD-PARTY-NOTICES.txt" "$PREFIX/THIRD-PARTY-NOTICES.txt" 2>/dev/null || true
if [ -f "$HERE/LEIA-ME.txt" ]; then cp -f "$HERE/LEIA-ME.txt" "$PREFIX/LEIA-ME.txt"; fi
if [ -f "$REPO/Assets/logo.png" ]; then cp -f "$REPO/Assets/logo.png" "$PREFIX/infopc.png"; fi
ok "Programa instalado: $PREFIX/InfoPC"

if [ "$NO_SHORTCUT" = 0 ]; then
  mkdir -p "$BIN_DIR" "$APPS_DIR"
  ln -sf "$PREFIX/InfoPC" "$BIN_DIR/infopc"
  # Exec entre aspas segue a especificação do .desktop: "%" vira "%%" (senão vira código de campo); \ " ` e $ ganham escape,
  # com a contrabarra dobrada de novo pela regra geral dos valores (\ vira \\\\)
  EXEC_PATH="$(printf '%s' "$PREFIX/InfoPC" | sed -e 's/\\/\\\\\\\\/g' -e 's/"/\\\\"/g' -e 's/`/\\\\`/g' -e 's/\$/\\\\$/g' -e 's/%/%%/g')"
  cat > "$APPS_DIR/infopc.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=InfoPC
GenericName=Diagnóstico do PC
Comment=Informações do sistema, sensores, benchmarks e ajustes para jogos
Exec="$EXEC_PATH"
Icon=$PREFIX/infopc.png
Terminal=false
Categories=System;Monitor;Utility;
Keywords=hardware;sensores;temperatura;benchmark;cpu;gpu;jogos;
StartupWMClass=InfoPC
EOF
  if have_cmd update-desktop-database; then update-desktop-database "$APPS_DIR" >/dev/null 2>&1 || true; fi
  ok "Comando: infopc   ·   Atalho no menu de aplicativos: InfoPC"
  case ":$PATH:" in *":$BIN_DIR:"*) ;; *) warn "$BIN_DIR não está no seu PATH. Abra um novo terminal ou rode: export PATH=\"$BIN_DIR:\$PATH\"" ;; esac
fi

# ---------------------------------------------------------------- 6) conferência final
say ""
if ver="$("$PREFIX/InfoPC" --versao 2>/dev/null)"; then
  ok "InfoPC $ver instalado e respondendo."
else
  warn "O programa foi copiado, mas não respondeu a --versao. Rode '$PREFIX/InfoPC --diagnostico' para ver o motivo."
fi
say ""
say "${c_b}Como usar${c_0}"
say "  infopc                  abre a janela (ou procure InfoPC no menu de aplicativos)"
say "  infopc --ajuda          comandos de terminal: --info, --sensores, --relatorio, --diagnostico..."
say "  infopc --diagnostico    mostra o que falta instalar para ler tudo (S.M.A.R.T., sensores, GameMode...)"
say "  bash $HERE/desinstalar.sh   remove o programa"
say ""
say "Configurações e histórico: $PREFIX/dados (a pasta acompanha o programa, é portátil)."
