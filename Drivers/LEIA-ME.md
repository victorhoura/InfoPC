# Driver serial incluído (Maker)

Driver oficial, sem modificação, embutido no `InfoPC.exe` e instalado pela aba
**MAKER › Drivers seriais › Drivers incluídos** (via `pnputil /add-driver /install`).
A pasta é um pacote `.inf` + `.cat` com a assinatura original do fabricante.

**Estes arquivos não fazem parte da licença MIT do InfoPC:** são da Arduino LLC, sob GPL versão 2 ou posterior, com a exceção
escrita no próprio `.inf`. O texto da GPL está em `THIRD-PARTY-NOTICES.txt`, na raiz do repositório.

| Pasta | Chip | Versão | Origem oficial | Assinatura |
|---|---|---|---|---|
| `Arduino` | Placas Arduino/Genuino (VID 2341) | arduino 1.2.3.0, genuino 1.0.3.0 | [github.com/arduino/Arduino · build/windows/dist/drivers](https://github.com/arduino/Arduino/tree/master/build/windows/dist/drivers) | Arduino LLC / Arduino srl |

Nenhum arquivo foi alterado, então as assinaturas continuam válidas.

CH340, CP210x e FTDI não ficam aqui: os termos desses fabricantes não permitem publicar os arquivos (D-030). A aba Maker aponta
para a página de cada um. Os pacotes `arduino-org` e `arduino_gemma` também saíram, porque não trazem licença escrita.

Para incluir outro driver: confira antes se a licença permite redistribuir, registre em `docs/UPSTREAM.md`, crie a pasta com os
arquivos do pacote oficial (sem o instalador `.exe`) e ligue-a em `MakerDrivers.Chips` (campo `Bundle`). O `.csproj` embute tudo em `Drivers\` automaticamente.
