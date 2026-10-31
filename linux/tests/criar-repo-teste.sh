#!/bin/sh
# So para o job "pacotes" do CI (.github/workflows/linux.yml), em conteiner descartavel: altera o sistema.
# Cria um repositorio local com duas versoes do pacote infopc-teste, para testar a volta a uma versao mais antiga.
# Uso: sh linux/tests/criar-repo-teste.sh apt|dnf|zypper
set -e
pm="$1"
root=/tmp/infopc-repo
mkdir -p "$root"

case "$pm" in
  apt)
    for v in 1.0 2.0; do
      d="$root/build-$v"
      mkdir -p "$d/DEBIAN"
      cat > "$d/DEBIAN/control" <<EOF
Package: infopc-teste
Version: $v
Architecture: all
Maintainer: InfoPC <infopc@invalid>
Description: pacote de teste do CI do InfoPC
EOF
      dpkg-deb --build "$d" "$root/infopc-teste_$v.deb"
      rm -rf "$d"
    done
    (cd "$root" && dpkg-scanpackages -m . /dev/null > Packages)   # -m: as duas versoes, nao so a mais nova
    echo "deb [trusted=yes] file:$root ./" > /etc/apt/sources.list.d/infopc-teste.list
    apt-get update -qq
    ;;
  dnf|zypper)
    cat > "$root/infopc-teste.spec" <<'EOF'
Name: infopc-teste
Version: %{versao}
Release: 1
Summary: pacote de teste do CI do InfoPC
License: MIT
BuildArch: noarch
%description
Pacote de teste do CI do InfoPC.
%files
EOF
    for v in 1.0 2.0; do
      rpmbuild --define "_topdir $root/rpm" --define "versao $v" -bb "$root/infopc-teste.spec"
    done
    createrepo_c "$root/rpm/RPMS"
    if [ "$pm" = dnf ]; then
      cat > /etc/yum.repos.d/infopc-teste.repo <<EOF
[infopc-teste]
name=infopc-teste
baseurl=file://$root/rpm/RPMS
gpgcheck=0
EOF
    else
      zypper --non-interactive addrepo --no-gpgcheck "$root/rpm/RPMS" infopc-teste
      zypper --non-interactive refresh infopc-teste
    fi
    ;;
  *)
    echo "gerenciador sem repositorio de teste: $pm" >&2
    exit 1
    ;;
esac
