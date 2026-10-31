namespace InfoPC;

/// <summary>
/// Veredito de saúde de cada disco a partir do S.M.A.R.T. (quando lido) e do sysfs. Regras conservadoras: só vira
/// CRÍTICO o que o próprio disco declara ou o que indica falha iminente; o resto é ATENÇÃO. Nada é escrito no disco.
/// </summary>
public static class DiskHealthEvaluator
{
    public static List<DiskHealthCard> Cards(IReadOnlyList<PhysicalDisk> disks, IReadOnlyList<SmartInfo>? smart)
    {
        var cards = new List<DiskHealthCard>();
        int i = 0;
        foreach (var d in disks)
        {
            var s = smart?.FirstOrDefault(x => x.Device.EndsWith("/" + d.Name, StringComparison.Ordinal));
            cards.Add(Card(d, s, i++));
        }
        if (cards.Count == 0)
            cards.Add(new DiskHealthCard { Title = "Nenhum disco encontrado", Verdict = DiskVerdict.Unknown, Reasons = "O sistema não listou discos em /sys/block (comum em contêiner ou máquina virtual restrita)." });
        return cards;
    }

    public static DiskHealthCard Card(PhysicalDisk d, SmartInfo? s, int index = 0)
    {
        string meta = string.Join(" · ", new[] { d.Bus, d.MediaType, Fmt.Capacity(d.SizeBytes), d.DevPath }.Where(x => x.Length > 0));
        var m = new List<KeyValuePair<string, string>>();
        void M(string k, string? v) { if (!string.IsNullOrWhiteSpace(v)) m.Add(new(k, v)); }

        M("Modelo", d.Title);
        M("Interface", d.Bus);
        M("Tipo", d.MediaType);
        M("Firmware", s?.Firmware is { Length: > 0 } f ? f : d.Firmware);
        double? temp = s?.TempC ?? d.TempC;
        if (temp != null) M("Temperatura", $"{temp:0} °C");
        if (d.Mounts.Count > 0) M("Montado em", string.Join(", ", d.Mounts.Select(x => x.MountPoint)));

        if (s == null || s.Error != null)
        {
            string why = s?.Error != null
                ? $"O smartctl não leu este disco: {s.Error}"
                : "Para ver o estado de saúde (S.M.A.R.T.), clique em LER S.M.A.R.T. O Linux só libera esses dados para o administrador, então o programa pede a senha uma vez.";
            if (temp is >= 70) why += $" A temperatura de {temp:0} °C está alta.";
            return new DiskHealthCard { Index = index, Title = d.Title, Meta = meta, Serial = d.Serial, Verdict = temp is >= 70 ? DiskVerdict.Warning : DiskVerdict.Unknown, Reasons = why, Metrics = m };
        }

        var critical = new List<string>();
        var warn = new List<string>();
        if (s.Model.Length > 0) M("Modelo (S.M.A.R.T.)", s.Model);
        if (s.PowerOnHours != null) M("Tempo ligado", $"{s.PowerOnHours:N0} h (≈ {s.PowerOnHours / 24.0:N0} dias)");
        if (s.PowerCycles != null) M("Ciclos de energia", $"{s.PowerCycles:N0}");

        if (s.Passed == false) critical.Add("o disco reportou falha no autodiagnóstico S.M.A.R.T. — faça backup agora");

        if (s.IsNvme)
        {
            if (s.CriticalWarning is > 0) critical.Add($"aviso crítico do NVMe (código {s.CriticalWarning})");
            if (s.PercentUsed != null)
            {
                M("Vida útil usada", $"{s.PercentUsed} %");
                if (s.PercentUsed >= 100) critical.Add($"a vida útil estimada acabou ({s.PercentUsed}% usada)");
                else if (s.PercentUsed >= 90) warn.Add($"{s.PercentUsed}% da vida útil já foi usada");
            }
            if (s.AvailableSpare != null)
            {
                M("Reserva disponível", $"{s.AvailableSpare} %");
                if (s.SpareThreshold != null && s.AvailableSpare < s.SpareThreshold) critical.Add($"a reserva de células ({s.AvailableSpare}%) caiu abaixo do limite do fabricante ({s.SpareThreshold}%)");
            }
            if (s.MediaErrors != null) { M("Erros de mídia", $"{s.MediaErrors:N0}"); if (s.MediaErrors > 0) warn.Add($"{s.MediaErrors:N0} erro(s) de integridade de dados"); }
            if (s.ErrorLogEntries is > 0) M("Entradas no log de erros", $"{s.ErrorLogEntries:N0}");
            if (s.UnsafeShutdowns != null) M("Desligamentos bruscos", $"{s.UnsafeShutdowns:N0}");
            if (s.WrittenBytes != null) M("Total gravado", $"{s.WrittenBytes / 1e12:0.##} TB");
            if (s.ReadBytes != null) M("Total lido", $"{s.ReadBytes / 1e12:0.##} TB");
            if (temp >= 80) critical.Add($"temperatura de {temp:0} °C"); else if (temp >= 70) warn.Add($"temperatura alta ({temp:0} °C)");
        }
        else
        {
            bool ssd = s.RotationRate == 0 || (!d.Rotational && s.RotationRate == null);
            var realloc = s.Attr(5); var pending = s.Attr(197); var uncorr = s.Attr(198); var crc = s.Attr(199); var reportedUncorr = s.Attr(187);
            if (realloc != null) { M("Setores realocados", $"{realloc.Raw:N0}"); if (realloc.Raw >= 100) critical.Add($"{realloc.Raw:N0} setores realocados"); else if (realloc.Raw > 0) warn.Add($"{realloc.Raw:N0} setor(es) realocado(s)"); }
            if (pending != null) { M("Setores pendentes", $"{pending.Raw:N0}"); if (pending.Raw > 0) warn.Add($"{pending.Raw:N0} setor(es) pendente(s) de realocação"); }
            if (uncorr != null) { M("Setores incorrigíveis", $"{uncorr.Raw:N0}"); if (uncorr.Raw > 0) critical.Add($"{uncorr.Raw:N0} setor(es) incorrigível(is)"); }
            if (reportedUncorr != null && reportedUncorr.Raw > 0) warn.Add($"{reportedUncorr.Raw:N0} erro(s) incorrigível(is) reportado(s)");
            if (crc != null) { M("Erros de CRC no cabo", $"{crc.Raw:N0}"); if (crc.Raw > 0) warn.Add($"{crc.Raw:N0} erro(s) de CRC (confira o cabo SATA)"); }
            // desgaste de SSD (valor normalizado: 100 = novo): 177 (Wear Leveling), 231 (SSD Life Left), 233 (Media Wearout)
            foreach (int id in new[] { 231, 233, 177 })
                if (s.Attr(id) is { } w && w.Value is > 0 and <= 100)
                {
                    M("Vida restante (S.M.A.R.T. " + id + ")", $"{w.Value} %");
                    if (w.Value < 10) critical.Add($"restam apenas {w.Value}% de vida útil"); else if (w.Value < 20) warn.Add($"restam {w.Value}% de vida útil");
                    break;
                }
            if (ssd && s.Attr(241) is { } wr) M("Total gravado (LBAs)", $"{wr.Raw:N0}");
            double limit = ssd ? 70 : 55;
            if (temp > limit + 10) critical.Add($"temperatura de {temp:0} °C"); else if (temp > limit) warn.Add($"temperatura alta ({temp:0} °C)");
        }

        DiskVerdict v = critical.Count > 0 ? DiskVerdict.Critical : warn.Count > 0 ? DiskVerdict.Warning : DiskVerdict.Good;
        string reasons = v switch
        {
            DiskVerdict.Critical => "Atenção: " + string.Join("; ", critical.Concat(warn)) + ". Faça backup dos dados importantes e considere trocar o disco.",
            DiskVerdict.Warning => "Pontos de atenção: " + string.Join("; ", warn) + ". Acompanhe e mantenha backup em dia.",
            _ => s.Passed == true ? "O autodiagnóstico S.M.A.R.T. passou e nenhum contador de falha está elevado." : "Nenhum contador de falha está elevado."
        };
        return new DiskHealthCard { Index = index, Title = d.Title, Meta = meta, Serial = s.Serial.Length > 0 ? s.Serial : d.Serial, Verdict = v, Reasons = reasons, Metrics = m };
    }
}
