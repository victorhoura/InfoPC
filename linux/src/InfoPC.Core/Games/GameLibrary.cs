using System.Diagnostics;
using System.Text.Json;

namespace InfoPC;

/// <summary>Jogo instalado (de qualquer loja) ou adicionado à mão.</summary>
public sealed class LibGame
{
    public string Store { get; init; } = "";
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string InstallDir { get; init; } = "";
    public string? ExePath { get; set; }
    public string? LaunchUri { get; init; }
    public string Args { get; init; } = "";
    /// <summary>Roda pelo Proton (Steam com compatdata) ou pelo Wine.</summary>
    public bool Windows { get; init; }
    public string Key => Store + ":" + Id;
    public string Subtitle => Store + (Windows ? " · Windows (Proton/Wine)" : "");
    public bool IsSteam => Store == "Steam";
}

/// <summary>Procura jogos instalados: Steam, Lutris, Heroic (Epic, GOG, Amazon) e os adicionados manualmente. Só lê arquivos.</summary>
public static class GameLibrary
{
    public static List<LibGame> Scan(IEnumerable<CustomGame> custom)
    {
        var all = new List<LibGame>();
        try { all.AddRange(Steam()); } catch { }
        try { all.AddRange(Lutris()); } catch { }
        try { all.AddRange(Heroic()); } catch { }
        foreach (var c in custom)
        {
            if (!File.Exists(c.Exe)) continue;
            all.Add(new LibGame
            {
                Store = "Manual", Id = c.Exe, Name = string.IsNullOrWhiteSpace(c.Name) ? Path.GetFileNameWithoutExtension(c.Exe) : c.Name,
                InstallDir = Path.GetDirectoryName(c.Exe) ?? "", ExePath = c.Exe, Args = c.Args, Windows = c.Exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase),
            });
        }
        return all.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ---------------- Steam ----------------

    public static IEnumerable<LibGame> Steam()
    {
        string? steam = SteamInfo.SteamPath();
        if (steam == null) yield break;
        foreach (var lib in SteamInfo.LibraryFolders(steam))
        {
            string apps = Sys.Join(lib, "steamapps");
            if (!Directory.Exists(Sys.P(apps))) continue;
            foreach (var acf in Directory.EnumerateFiles(Sys.P(apps), "appmanifest_*.acf"))
            {
                LibGame? g = ParseManifest(File.ReadAllText(acf), apps);
                if (g != null) yield return g;
            }
        }
    }

    public static LibGame? ParseManifest(string acfText, string appsDir)
    {
        try
        {
            var st = Vdf.Parse(acfText).Child("AppState");
            if (st == null) return null;
            string? id = st.Get("appid"), name = st.Get("name"), dir = st.Get("installdir");
            if (id == null || name == null || dir == null) return null;
            if (long.TryParse(st.Get("StateFlags"), out long flags) && (flags & 4) == 0) return null; // 4 = instalado por completo
            // ferramentas da Steam que aparecem como "apps": runtimes, Proton e redistribuíveis
            if (id is "228980" || name.StartsWith("Steamworks Common", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Proton", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Steam Linux Runtime", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Steam Runtime", StringComparison.OrdinalIgnoreCase)) return null;
            string full = Sys.Join(appsDir, "common", dir);
            if (!Directory.Exists(Sys.P(full))) return null;
            bool proton = Directory.Exists(Sys.P(Sys.Join(appsDir, "compatdata", id)));   // existe quando o jogo roda pelo Proton
            return new LibGame { Store = "Steam", Id = id, Name = name, InstallDir = full, Windows = proton };
        }
        catch { return null; }
    }

    // ---------------- Lutris ----------------

    public static IEnumerable<LibGame> Lutris()
    {
        if (!Shell.Has("lutris")) return Array.Empty<LibGame>();
        var r = Shell.Run("lutris", new[] { "--list-games", "--json" }, 10000);
        return r.Ok ? ParseLutris(r.Out) : Array.Empty<LibGame>();
    }

    public static List<LibGame> ParseLutris(string json)
    {
        var list = new List<LibGame>();
        try
        {
            // a saída do lutris pode trazer linhas de aviso antes do JSON
            int start = json.IndexOf('[');
            if (start < 0) return list;
            using var doc = JsonDocument.Parse(json[start..]);
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.TryGetProperty("installed", out var inst) && inst.ValueKind is JsonValueKind.Number && inst.GetInt32() == 0) continue;
                string id = e.TryGetProperty("id", out var i) ? i.ToString() : "";
                string name = e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (id.Length == 0 || name.Length == 0) continue;
                string runner = e.TryGetProperty("runner", out var r) ? r.GetString() ?? "" : "";
                string dir = e.TryGetProperty("directory", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : "";
                list.Add(new LibGame { Store = "Lutris", Id = id, Name = name, InstallDir = dir, LaunchUri = "lutris:rungameid/" + id, Windows = runner is "wine" or "proton" });
            }
        }
        catch (JsonException) { }
        return list;
    }

    // ---------------- Heroic (Epic, GOG, Amazon) ----------------

    private static IEnumerable<string> HeroicDirs()
    {
        string home = Sys.Home;
        string cfg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : Sys.Join(home, ".config");
        yield return Sys.Join(cfg, "heroic");
        yield return Sys.Join(home, ".var", "app", "com.heroicgameslauncher.hgl", "config", "heroic");
    }

    public static IEnumerable<LibGame> Heroic()
    {
        foreach (var root in HeroicDirs())
        {
            foreach (var (rel, runner, label) in new[]
            {
                ("legendaryConfig/legendary/installed.json", "legendary", "Epic (Heroic)"),
                ("gog_store/installed.json", "gog", "GOG (Heroic)"),
                ("nile_config/nile/installed.json", "nile", "Amazon (Heroic)"),
            })
            {
                string f = Sys.P(Sys.Join(root, rel));
                if (!File.Exists(f)) continue;
                foreach (var g in ParseHeroic(File.ReadAllText(f), runner, label)) yield return g;
            }
        }
    }

    public static List<LibGame> ParseHeroic(string json, string runner, string label)
    {
        var list = new List<LibGame>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            // legendary/nile: objeto { appName: {...} }; gog: { "installed": [ {...} ] }
            IEnumerable<(string App, JsonElement E)> items = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("installed", out var arr) && arr.ValueKind == JsonValueKind.Array
                ? arr.EnumerateArray().Select(e => (e.TryGetProperty("appName", out var a) ? a.GetString() ?? "" : "", e))
                : doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.EnumerateObject().Select(p => (p.Name, p.Value)) : Array.Empty<(string, JsonElement)>();
            foreach (var (app, e) in items)
            {
                if (app.Length == 0 || e.ValueKind != JsonValueKind.Object) continue;
                if (e.TryGetProperty("is_dlc", out var dlc) && dlc.ValueKind == JsonValueKind.True) continue;
                string path = e.TryGetProperty("install_path", out var p) ? p.GetString() ?? "" : "";
                string title = e.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
                if (title.Length == 0) title = path.Length > 0 ? Path.GetFileName(path.TrimEnd('/')) : app;
                bool win = !(e.TryGetProperty("platform", out var pl) && pl.GetString() is "linux" or "Linux");
                list.Add(new LibGame { Store = label, Id = app, Name = title, InstallDir = path, LaunchUri = $"heroic://launch/{runner}/{app}", Windows = win });
            }
        }
        catch (JsonException) { }
        return list;
    }
}

/// <summary>Informações da instalação da Steam e edição das "Opções de inicialização" (localconfig.vdf).</summary>
public static class SteamInfo
{
    public static List<string> Roots()
    {
        string home = Sys.Home;
        string data = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x ? x : Sys.Join(home, ".local", "share");
        return new List<string>
        {
            Sys.Join(home, ".steam", "steam"), Sys.Join(data, "Steam"),
            Sys.Join(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
            Sys.Join(home, "snap", "steam", "common", ".local", "share", "Steam"),
        };
    }

    public static string? SteamPath() => Roots().FirstOrDefault(r => Directory.Exists(Sys.P(Sys.Join(r, "steamapps"))));

    public static List<string> LibraryFolders(string steam)
    {
        var list = new List<string> { steam };
        try
        {
            foreach (var rel in new[] { "steamapps/libraryfolders.vdf", "config/libraryfolders.vdf" })
            {
                string f = Sys.P(Sys.Join(steam, rel));
                if (!File.Exists(f)) continue;
                var root = Vdf.Parse(File.ReadAllText(f)).Child("libraryfolders");
                if (root == null) continue;
                foreach (var (_, c) in root.Children)
                {
                    string? p = c.Get("path");
                    if (p != null && Directory.Exists(Sys.P(p)) && !list.Contains(p)) list.Add(p);
                }
                break;
            }
        }
        catch { }
        return list;
    }

    public static bool IsRunning => Process.GetProcessesByName("steam").Length > 0;

    /// <summary>localconfig.vdf mais recente (a conta que usou a Steam por último).</summary>
    private static string? LocalConfig()
    {
        string? steam = SteamPath();
        if (steam == null) return null;
        string ud = Sys.P(Sys.Join(steam, "userdata"));
        if (!Directory.Exists(ud)) return null;
        return Directory.GetDirectories(ud).Select(d => Sys.Join(d, "config", "localconfig.vdf")).Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }

    private static VdfNode? AppsNode(VdfNode root, bool create)
    {
        VdfNode? n = root.Child("UserLocalConfigStore");
        foreach (var key in new[] { "Software", "Valve", "Steam", "apps" })
        {
            if (n == null) return null;
            n = create ? n.GetOrAddChild(key) : n.Child(key);
        }
        return n;
    }

    private const string SteamOpen = "A Steam está aberta. Feche-a para gravar as opções (ela sobrescreve o arquivo ao sair).";
    private const string NoConfig = "Não achei o localconfig.vdf da Steam (entre na Steam ao menos uma vez neste PC).";

    private static string? ReadOptions(string file, string appId) => AppsNode(Vdf.Parse(File.ReadAllText(file)), false)?.Child(appId)?.Get("LaunchOptions");

    public static string? GetLaunchOptions(string appId)
    {
        try
        {
            string? f = LocalConfig();
            return f == null ? null : ReadOptions(f, appId);
        }
        catch { return null; }
    }

    /// <summary>Grava as opções de inicialização do jogo na conta que usou a Steam por último. A Steam precisa estar fechada (senão ela sobrescreve ao sair). Devolve null se deu certo, ou o erro.</summary>
    public static string? SetLaunchOptions(string appId, string options, bool steamRunning)
    {
        if (steamRunning) return SteamOpen;
        string? f = LocalConfig();
        if (f == null) return NoConfig;
        return WriteOptions(f, appId, options);
    }

    private static string? WriteOptions(string f, string appId, string options)
    {
        try
        {
            var root = Vdf.Parse(File.ReadAllText(f));
            var app = AppsNode(root, true)!.GetOrAddChild(appId);
            if (string.IsNullOrWhiteSpace(options)) app.Remove("LaunchOptions"); else app.Set("LaunchOptions", options.Trim());
            if (!File.Exists(f + ".infopc.bak")) File.Copy(f, f + ".infopc.bak"); // o arquivo de antes da 1ª gravação do InfoPC; não é sobrescrito
            File.WriteAllText(f, Vdf.Serialize(root), new System.Text.UTF8Encoding(false));
            return null;
        }
        catch (Exception ex) { return "Não consegui gravar: " + ex.Message; }
    }

    /// <summary>
    /// Grava as opções na conta que usou a Steam por último. Na primeira vez em cada conta, guarda no perfil as opções que o jogo tinha
    /// nela e grava o perfil no disco com <paramref name="persist"/> antes de mexer na Steam: se não der para guardar, não altera nada.
    /// Devolve null se deu certo, ou o erro.
    /// </summary>
    public static string? ApplyLaunchOptions(string appId, string options, GameProfile p, bool steamRunning, Action persist)
    {
        if (steamRunning) return SteamOpen;
        string? f = LocalConfig();
        if (f == null) return NoConfig;
        string? before;
        try { before = ReadOptions(f, appId); }
        catch (Exception ex) { return "Não consegui ler o localconfig.vdf: " + ex.Message; }
        bool added = p.SteamOriginals.TryAdd(f, before ?? "");
        if (added)
        {
            try { persist(); }
            catch (Exception ex) { p.SteamOriginals.Remove(f); return $"Não consegui guardar as opções originais ({ex.Message}). Nada foi alterado na Steam."; }
        }
        string? err = WriteOptions(f, appId, options);
        if (err != null && added) { p.SteamOriginals.Remove(f); try { persist(); } catch { } } // nada mudou; se este gravar falhar, o original no disco é o valor atual
        return err;
    }

    /// <summary>
    /// Devolve as opções que o jogo tinha antes do primeiro APLICAR, no mesmo localconfig.vdf (instalação e conta) em que foram trocadas.
    /// Só esquece o original que conseguiu devolver. Devolve null se deu certo, ou o erro.
    /// </summary>
    public static string? RestoreLaunchOptions(string appId, GameProfile p, bool steamRunning, Action persist)
    {
        if (p.SteamOriginals.Count == 0) return "Nada a restaurar: o InfoPC não alterou as opções deste jogo.";
        if (steamRunning) return SteamOpen;
        var errors = new List<string>();
        foreach (var (f, orig) in p.SteamOriginals.ToList())
        {
            string? err = File.Exists(f) ? WriteOptions(f, appId, orig) : $"Não achei {f} (a conta ou a pasta da Steam mudou); o original continua guardado.";
            if (err == null) p.SteamOriginals.Remove(f); else errors.Add(err);
        }
        try { persist(); } catch { } // se não gravar, o original continua no disco e devolvê-lo de novo não muda nada
        return errors.Count == 0 ? null : string.Join(" ", errors);
    }

    /// <summary>Pede à Steam para fechar (steam -shutdown) e espera até 25 s.</summary>
    public static async Task<bool> ShutdownAsync()
    {
        string? steam = Shell.Which("steam");
        if (steam != null) Shell.Run(steam, new[] { "-shutdown" }, 5000);
        else if (Shell.Has("flatpak")) Shell.Run("flatpak", new[] { "run", "com.valvesoftware.Steam", "-shutdown" }, 5000);
        for (int i = 0; i < 50 && IsRunning; i++) await Task.Delay(500);
        return !IsRunning;
    }
}
