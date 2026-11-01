"""Confere os downloads da v1.1.6 sem extrair nem executar os programas."""
import hashlib
from pathlib import Path
import struct
import sys
import tarfile
import zipfile

repo = Path(__file__).resolve().parents[3]
downloads = Path(sys.argv[1]).resolve()
text_files = {
    "LICENSE.txt": repo / "LICENSE",
    "THIRD-PARTY-NOTICES.txt": repo / "THIRD-PARTY-NOTICES.txt",
}


def normalized(data):
    return data.decode("utf-8-sig").replace("\r\n", "\n").rstrip()


lines = (downloads / "SHA256SUMS.txt").read_text(encoding="utf-8-sig").splitlines()
assert len(lines) == 3
for line in lines:
    digest, name = line.split()
    assert Path(name).name == name
    package = downloads / name
    assert hashlib.sha256(package.read_bytes()).hexdigest() == digest, name
    if name.endswith(".zip"):
        with zipfile.ZipFile(package) as archive:
            files = {n: archive.read(n) for n in archive.namelist() if not n.endswith("/")}
        binary = "InfoPC.exe"
        leame = repo / "LEIA-ME.txt"
    else:
        with tarfile.open(package, "r:gz") as archive:
            members = archive.getmembers()
            assert all(m.isdir() or m.isfile() for m in members), "unexpected link"
            files = {m.name: archive.extractfile(m).read() for m in members if m.isfile()}
        binary = "InfoPC"
        leame = repo / "linux/LEIA-ME.txt"
    expected = {"InfoPC/" + n for n in [binary, "LEIA-ME.txt", *text_files]}
    assert set(files) == expected, (name, sorted(files))
    for n, reference in {"LEIA-ME.txt": leame, **text_files}.items():
        assert normalized(files["InfoPC/" + n]) == normalized(reference.read_bytes()), (name, n)
    data = files["InfoPC/" + binary]
    if binary.endswith(".exe"):
        assert data[:2] == b"MZ"
        pe = struct.unpack_from("<I", data, 0x3C)[0]
        assert data[pe:pe + 4] == b"PE\0\0"
        assert struct.unpack_from("<H", data, pe + 4)[0] == 0x8664
    else:
        assert data[:6] == b"\x7fELF\x02\x01"
        machine = struct.unpack_from("<H", data, 18)[0]
        assert machine == (183 if "arm64" in name else 62)
    print(f"OK {name}: SHA-256, four files, notices, README and executable architecture")
