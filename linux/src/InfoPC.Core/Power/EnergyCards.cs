namespace InfoPC;

/// <summary>Cartões da página Energia: uma por bateria (desgaste, ciclos, autonomia) e um da fonte e do perfil de energia.</summary>
public static class EnergyCards
{
    public static List<DiskHealthCard> Build(SensorHub? hub = null)
    {
        var cards = new List<DiskHealthCard>();
        var bats = BatteryReader.Read();
        bool? ac = BatteryReader.AcOnline();
        bool onAc = ac ?? bats.Count == 0;

        for (int i = 0; i < bats.Count; i++)
        {
            var b = bats[i];
            double? wear = b.HealthPct != null ? Math.Max(0, 100 - b.HealthPct.Value) : null;
            var reasons = new List<string>();
            var verdict = DiskVerdict.Good;
            string label = "SAUDÁVEL";
            if (wear == null) { verdict = DiskVerdict.Unknown; label = "SEM DADOS"; reasons.Add("O sistema não informou a capacidade de projeto ou a atual, então o desgaste não pôde ser calculado."); }
            else if (wear >= 50) { verdict = DiskVerdict.Critical; label = "DESGASTADA"; reasons.Add($"Perdeu {wear:0}% da capacidade original: a autonomia é metade do que era. Vale trocar a bateria."); }
            else if (wear >= 30) { verdict = DiskVerdict.Warning; label = "ATENÇÃO"; reasons.Add($"Perdeu {wear:0}% da capacidade original. Ainda funciona, mas a autonomia já caiu bastante."); }
            else if (wear >= 15) reasons.Add($"Desgaste de {wear:0}%, normal para uma bateria com uso.");
            else reasons.Add($"Desgaste de apenas {wear:0}%: bateria em ótimo estado.");
            if (b.Cycles is >= 1000 && verdict == DiskVerdict.Good) { verdict = DiskVerdict.Warning; label = "ATENÇÃO"; }
            if (b.Cycles is >= 800) reasons.Add($"{b.Cycles} ciclos de carga: perto do fim da vida útil típica das baterias de íon-lítio (500 a 1000).");
            if (b.Percent is < 20 && !onAc) reasons.Add("Carga baixa e fora da tomada.");

            var m = new List<KeyValuePair<string, string>>();
            void Add(string k, string? v) { if (!string.IsNullOrEmpty(v)) m.Add(new(k, v)); }
            Add("Carga atual", b.Percent != null ? $"{b.Percent}%" : null);
            Add("Estado", StatusName(b.Status));
            if (b.TimeRemaining is { } t && t.TotalHours < 48)
                Add(b.Charging ? "Tempo até carregar" : "Autonomia estimada", $"{(int)t.TotalHours}h {t.Minutes:00}min");
            Add("Capacidade de projeto", b.EnergyDesignWh != null ? $"{b.EnergyDesignWh:0.0} Wh" : null);
            Add("Capacidade máxima atual", b.EnergyFullWh != null ? $"{b.EnergyFullWh:0.0} Wh" : null);
            Add("Desgaste", wear != null ? $"{wear:0.0}%" : null);
            Add("Ciclos de carga", b.Cycles?.ToString());
            if (b.VoltageV is > 0) Add("Tensão", $"{b.VoltageV:0.00} V");
            if (b.PowerW is > 0.05) Add(b.Charging ? "Potência de carga" : "Consumo atual", $"{b.PowerW:0.0} W");
            Add("Fabricante", b.Manufacturer);
            Add("Modelo", b.Model);
            Add("Química", b.Technology);

            cards.Add(new DiskHealthCard
            {
                Title = bats.Count > 1 ? $"Bateria {i + 1}" : "Bateria",
                Meta = b.Manufacturer + (b.Percent != null ? (b.Manufacturer.Length > 0 ? " · " : "") + $"{b.Percent}% de carga" : ""),
                Verdict = verdict, Label = label, Reasons = string.Join(" ", reasons), Metrics = m,
            });
        }

        string profile = BatteryReader.PowerProfile();
        var em = new List<KeyValuePair<string, string>>
        {
            new("Fonte de energia", bats.Count == 0 ? "Tomada (PC de mesa)" : onAc ? "Tomada" : "Bateria"),
            new("Perfil de energia", profile),
        };
        if (hub != null)
            foreach (var s in hub.Rows.Where(x => x.Kind == SensorKind.Power && x.RawValue is > 0).Take(6))
                em.Add(new($"{s.Hardware} · {s.Name}", s.Value));
        string advice = profile.Contains("Economia", StringComparison.Ordinal) || profile.Contains("powersave", StringComparison.Ordinal)
            ? " Para jogos e benchmarks, o perfil Desempenho (página Otimização) evita que o PC diminua o clock." : "";
        cards.Add(new DiskHealthCard
        {
            Title = "Energia",
            Meta = bats.Count == 0 ? "Nenhuma bateria detectada" : "Fonte, perfil e consumo",
            Verdict = DiskVerdict.Good, Label = "EM USO",
            Reasons = (profile == "—" ? "Perfil de energia não identificado (sem power-profiles-daemon nem cpufreq)." : $"Perfil de energia ativo: {profile}.") + advice
                      + (bats.Count == 0 ? " O consumo da fonte (PSU) não é medido pelo sistema; os valores abaixo são dos sensores de CPU e GPU." : ""),
            Metrics = em,
        });
        return cards;
    }

    private static string? StatusName(string s) => s.ToLowerInvariant() switch
    {
        "charging" => "Carregando", "discharging" => "Descarregando", "full" => "Carga completa", "not charging" => "Conectada, sem carregar",
        "unknown" or "" => null, var o => o
    };
}
