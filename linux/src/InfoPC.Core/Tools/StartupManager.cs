using System.Text;

namespace InfoPC;

public enum StartupKind { Desktop, SystemdUser }

public sealed class StartupEntry
{
    public string Name { get; init; } = "";
    public string Command { get; init; } = "";
    public string Comment { get; init; } = "";
    /// <summary>Nome do arquivo .desktop ou da unidade systemd.</summary>
    public string File { get; init; } = "";
    public StartupKind Kind { get; init; }
    public string Source { get; init; } = "";
    /// <summary>Vem de /etc/xdg/autostart (do sistema): para desligar, o programa cria uma cópia sua com Hidden=true.</summary>
    public bool System { get; init; }
    public bool Enabled { get; set; }
    public string Path { get; init; } = "";
    /// <summary>Cópia do usuário de uma entrada do sistema (a que o InfoPC cria para desligar uma do sistema).</summary>
    public bool OverridesSystem { get; init; }
}

/// <summary>
/// Programas que abrem com a sessão: autostart do XDG (~/.config/autostart e /etc/xdg/autostart) e unidades systemd do usuário que o próprio
/// usuário ativou. Desativar e remover têm desfazer; nada do sistema é alterado (só arquivos da conta do usuário).
/// </summary>
public static class StartupManager
{
    private static string UserDir => Sys.Join(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : Sys.Join(Sys.Home, ".config"), "autostart");
    private static string SystemDir => "/etc/xdg/autostart";
    private static string UnitsDir => Sys.Join(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : Sys.Join(Sys.Home, ".config"), "systemd/user");

    // ---------------- .desktop ----------------

    /// <summary>Chaves do grupo [Desktop Entry] (a primeira ocorrência de cada uma; traduções "Name[pt_BR]" entram com a chave completa).</summary>
    public static Dictionary<string, string> ParseDesktop(string text)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        bool inEntry = false;
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('[')) { inEntry = line == "[Desktop Entry]"; continue; }
            if (!inEntry || line.Length == 0 || line.StartsWith('#')) continue;
            int i = line.IndexOf('=');
            if (i > 0) d.TryAdd(line[..i].Trim(), line[(i + 1)..].Trim());
        }
        return d;
    }

    public static bool IsEnabled(Dictionary<string, string> d) =>
        !(d.TryGetValue("Hidden", out var h) && h.Equals("true", StringComparison.OrdinalIgnoreCase))
        && !(d.TryGetValue("X-GNOME-Autostart-enabled", out var g) && g.Equals("false", StringComparison.OrdinalIgnoreCase));

    private static string LocalizedName(Dictionary<string, string> d, string fallback)
    {
        foreach (var k in new[] { "Name[pt_BR]", "Name[pt]" }) if (d.TryGetValue(k, out var v) && v.Length > 0) return v;
        return d.GetValueOrDefault("Name", fallback);
    }

    /// <summary>Entradas de inicialização. Com <paramref name="s"/>, inclui os serviços systemd que o InfoPC desativou (para religar).</summary>
    public static List<StartupEntry> List(AppSettings? s = null)
    {
        var list = new List<StartupEntry>();
        var userFiles = Sys.List(UserDir).Where(f => f.EndsWith(".desktop", StringComparison.Ordinal)).ToHashSet();

        foreach (var f in userFiles.OrderBy(f => f, StringComparer.Ordinal))
        {
            string path = Sys.Join(UserDir, f);
            string? text = Sys.Read(path);
            if (text == null) continue;
            var d = ParseDesktop(text);
            bool overridesSystem = Sys.FileExists(Sys.Join(SystemDir, f));
            list.Add(new StartupEntry
            {
                Name = LocalizedName(d, f), Command = d.GetValueOrDefault("Exec", ""), Comment = d.GetValueOrDefault("Comment", ""), File = f, Kind = StartupKind.Desktop,
                Source = overridesSystem ? "Sua conta (substitui a do sistema)" : "Sua conta · ~/.config/autostart", System = false, Enabled = IsEnabled(d), Path = path,
                OverridesSystem = overridesSystem,
            });
        }
        foreach (var f in Sys.List(SystemDir).Where(f => f.EndsWith(".desktop", StringComparison.Ordinal) && !userFiles.Contains(f)))
        {
            string path = Sys.Join(SystemDir, f);
            if (Sys.Read(path) is not { } text) continue;
            var d = ParseDesktop(text);
            list.Add(new StartupEntry
            {
                Name = LocalizedName(d, f), Command = d.GetValueOrDefault("Exec", ""), Comment = d.GetValueOrDefault("Comment", ""), File = f, Kind = StartupKind.Desktop,
                Source = "Sistema · /etc/xdg/autostart", System = true, Enabled = IsEnabled(d), Path = path,
            });
        }

        // unidades que o usuário ativou (links em ~/.config/systemd/user/*.wants); as do fabricante não aparecem aqui, de propósito
        foreach (var wants in Sys.List(UnitsDir).Where(n => n.EndsWith(".wants", StringComparison.Ordinal)))
            foreach (var u in Sys.List(Sys.Join(UnitsDir, wants)).Where(n => n.EndsWith(".service", StringComparison.Ordinal)))
            {
                if (list.Any(e => e.File == u)) continue;
                string desc = "";
                foreach (var line in Sys.Lines(Sys.Join(UnitsDir, wants, u))) if (line.StartsWith("Description=", StringComparison.Ordinal)) { desc = line["Description=".Length..].Trim(); break; }
                list.Add(new StartupEntry
                {
                    Name = u[..^8], Command = "systemctl --user start " + u, Comment = desc, File = u, Kind = StartupKind.SystemdUser,
                    Source = "Serviço da sua conta (systemd)", Enabled = true, Path = Sys.Join(UnitsDir, wants, u),
                });
            }
        // desativados pelo InfoPC: o disable apagou o link, mas eles continuam na lista para religar
        foreach (var (u, desc) in s?.SystemdDisabled ?? new Dictionary<string, string>())
            if (!list.Any(e => e.File == u))
                list.Add(new StartupEntry
                {
                    Name = u.EndsWith(".service", StringComparison.Ordinal) ? u[..^8] : u, Command = "systemctl --user start " + u, Comment = desc, File = u,
                    Kind = StartupKind.SystemdUser, Source = "Serviço da sua conta (systemd) · desativado pelo InfoPC", Enabled = false,
                });
        return list.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ---------------- ações ----------------

    /// <summary>
    /// Liga ou desliga a entrada. Do sistema: cria/remove a cópia do usuário com Hidden=true. Do usuário: ajusta Hidden. systemd: enable/disable --user;
    /// antes do disable, a unidade vai para <see cref="AppSettings.SystemdDisabled"/> no disco (<paramref name="persist"/>), senão ela sumiria da lista.
    /// </summary>
    public static string? SetEnabled(StartupEntry e, bool enabled, AppSettings s, Action persist)
    {
        try
        {
            if (e.Kind == StartupKind.SystemdUser)
            {
                bool added = !enabled && s.SystemdDisabled.TryAdd(e.File, e.Comment);
                if (added)
                {
                    try { persist(); }
                    catch (Exception ex)
                    {
                        s.SystemdDisabled.Remove(e.File);
                        return $"Nada foi alterado: não consegui guardar o serviço para religar depois ({ex.Message}).";
                    }
                }
                var r = Shell.Run("systemctl", new[] { "--user", enabled ? "enable" : "disable", e.File }, 15000);
                if (!r.Ok)
                {
                    if (added) { s.SystemdDisabled.Remove(e.File); try { persist(); } catch { } }
                    return "systemctl: " + (r.Err.Trim().Length > 0 ? r.Err.Trim() : "falhou");
                }
                if (enabled && s.SystemdDisabled.Remove(e.File)) { try { persist(); } catch { } } // se não gravar, o link de volta já a lista como ativada
                return null;
            }
            string userPath = Sys.Join(UserDir, e.File);
            if (e.System)
            {
                if (enabled) { File_Delete(userPath); return null; }          // volta a valer a do sistema
                Directory.CreateDirectory(Sys.P(UserDir));
                string text = Sys.Read(e.Path) ?? "[Desktop Entry]\nType=Application\n";
                File.WriteAllText(Sys.P(userPath), SetKey(text, "Hidden", "true"), new UTF8Encoding(false));
                return null;
            }
            if (enabled && e.OverridesSystem && SameAsSystem(e.File))
            {
                File_Delete(userPath);                                        // era só a cópia que escondia a do sistema: some, e a do sistema volta a valer
                return null;
            }
            string orig = ReadRaw(userPath);
            string next = SetKey(orig, "Hidden", enabled ? "false" : "true");
            next = SetKey(next, "X-GNOME-Autostart-enabled", enabled ? "true" : "false");
            File.WriteAllText(Sys.P(userPath), next, new UTF8Encoding(false));
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>
    /// Remove da inicialização guardando o conteúdo em <see cref="AppSettings.StartupRemoved"/> para restaurar depois. A cópia vai para o
    /// disco (<paramref name="persist"/>) antes de mexer: se não der para guardar, nada muda.
    /// </summary>
    public static string? Remove(StartupEntry e, AppSettings s, Action persist)
    {
        try
        {
            if (e.System) return SetEnabled(e, false, s, persist);           // arquivo do sistema: só esconde
            string userPath = Sys.Join(UserDir, e.File);
            var b = e.Kind == StartupKind.SystemdUser ? new StartupBackup { FileName = e.File, Content = "", Source = "systemd" }
                                                      : new StartupBackup { FileName = e.File, Content = ReadRaw(userPath), Source = "desktop" };
            s.StartupRemoved.Add(b);
            try { persist(); }
            catch (Exception ex)
            {
                s.StartupRemoved.Remove(b);
                return $"Nada foi removido: não consegui guardar a cópia para restaurar ({ex.Message}).";
            }
            if (e.Kind == StartupKind.SystemdUser)
            {
                var r = Shell.Run("systemctl", new[] { "--user", "disable", e.File }, 15000);
                if (!r.Ok)
                {
                    s.StartupRemoved.Remove(b);
                    try { persist(); } catch { }
                    return "systemctl: " + r.Err.Trim();
                }
                s.SystemdDisabled.Remove(e.File);                             // volta por "Restaurar removidos", não pela lista
            }
            else if (e.OverridesSystem)
                // a cópia do usuário substituía a do sistema: apagá-la faria a do sistema voltar a valer. Fica uma cópia desligada.
                File.WriteAllText(Sys.P(userPath), SetKey(ReadRaw(Sys.Join(SystemDir, e.File)), "Hidden", "true"), new UTF8Encoding(false));
            else File_Delete(userPath);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    public static string? RestoreRemoved(AppSettings s)
    {
        var errors = new List<string>();
        foreach (var b in s.StartupRemoved.ToList())
        {
            try
            {
                if (b.Source == "systemd")
                {
                    var r = Shell.Run("systemctl", new[] { "--user", "enable", b.FileName }, 15000);
                    if (!r.Ok) { errors.Add(b.FileName + ": " + r.Err.Trim()); continue; }
                }
                else
                {
                    Directory.CreateDirectory(Sys.P(UserDir));
                    File.WriteAllText(Sys.P(Sys.Join(UserDir, b.FileName)), b.Content, new UTF8Encoding(false));
                }
                s.StartupRemoved.Remove(b);
            }
            catch (Exception ex) { errors.Add(b.FileName + ": " + ex.Message); }
        }
        return errors.Count == 0 ? null : string.Join("; ", errors);
    }

    /// <summary>Texto do arquivo exatamente como está (sem aparar), para restaurar sem perder nada.</summary>
    private static string ReadRaw(string path) { try { return File.ReadAllText(Sys.P(path)); } catch { return ""; } }

    /// <summary>A cópia do usuário só difere da do sistema pelas chaves que ligam e desligam (Hidden e X-GNOME-Autostart-enabled).</summary>
    private static bool SameAsSystem(string file)
    {
        var u = ParseDesktop(ReadRaw(Sys.Join(UserDir, file))); var s = ParseDesktop(ReadRaw(Sys.Join(SystemDir, file)));
        foreach (var k in new[] { "Hidden", "X-GNOME-Autostart-enabled" }) { u.Remove(k); s.Remove(k); }
        return u.Count == s.Count && u.All(kv => s.TryGetValue(kv.Key, out var v) && v == kv.Value);
    }

    private static void File_Delete(string path) { try { File.Delete(Sys.P(path)); } catch (FileNotFoundException) { } }

    /// <summary>Troca (ou acrescenta) uma chave do grupo [Desktop Entry], preservando o resto do arquivo.</summary>
    public static string SetKey(string text, string key, string value)
    {
        var lines = text.Replace("\r", "").Split('\n').ToList();
        int start = lines.FindIndex(l => l.Trim() == "[Desktop Entry]");
        if (start < 0) { lines.Insert(0, "[Desktop Entry]"); start = 0; }
        int end = lines.FindIndex(start + 1, l => l.TrimStart().StartsWith('['));
        if (end < 0) end = lines.Count;
        for (int i = start + 1; i < end; i++)
            if (lines[i].StartsWith(key + "=", StringComparison.Ordinal)) { lines[i] = $"{key}={value}"; return string.Join("\n", lines).TrimEnd('\n') + "\n"; }
        lines.Insert(end, $"{key}={value}");
        return string.Join("\n", lines).TrimEnd('\n') + "\n";
    }
}
