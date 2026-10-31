using Xunit;

namespace InfoPC.Tests;

public class DriverInfoTests
{
    private const string PciIds = "10de  NVIDIA Corporation\n\t2786  AD104 [GeForce RTX 4070]\n8086  Intel Corporation\n\t15f3  Ethernet Controller I225-V\n\t7e40  Meteor Lake-P [Intel Arc Graphics]\n";

    [Fact]
    public void Pci_devices_get_names_drivers_and_missing_ones_come_first()
    {
        if (!TempRoot.ColonsOk) return;
        using var t = new TempRoot();
        t.Write("/usr/share/hwdata/pci.ids", PciIds);
        void Dev(string slot, string cls, string ven, string dev) { t.Write($"/sys/bus/pci/devices/{slot}/class", cls); t.Write($"/sys/bus/pci/devices/{slot}/vendor", ven); t.Write($"/sys/bus/pci/devices/{slot}/device", dev); }
        Dev("0000:01:00.0", "0x030000", "0x10de", "0x2786");
        Dev("0000:03:00.0", "0x020000", "0x8086", "0x15f3");
        Dev("0000:00:1c.0", "0x060400", "0x8086", "0x7e40");     // ponte PCI: não aparece
        var list = DriverInfo.PciDevices();
        Assert.Equal(2, list.Count);
        Assert.All(list, d => Assert.True(d.Missing));            // sem links de driver nas fixtures: ambos "sem driver"
        var gpu = list.Single(d => d.Category == "Vídeo");
        Assert.Equal("AD104 [GeForce RTX 4070]", gpu.Name);
        Assert.Equal("NVIDIA", gpu.Vendor);
        Assert.Equal("Ethernet Controller I225-V", list.Single(d => d.Category == "Ethernet").Name);
    }

    [Fact]
    public void Pci_ids_lookup_many_reads_the_file_once_and_survives_unknown_ids()
    {
        using var t = new TempRoot();
        t.Write("/usr/share/hwdata/pci.ids", PciIds);
        var r = Sys.Read("/usr/share/hwdata/pci.ids") != null ? PciIdsLookup() : null;
        Assert.Equal("Intel Corporation", r!["8086:15f3"].Vendor);
        Assert.Equal("Meteor Lake-P [Intel Arc Graphics]", r["8086:7e40"].Device);
        Assert.Null(r["8086:ffff"].Device);
        Assert.Null(r["dead:beef"].Vendor);
    }

    private static Dictionary<string, (string? Vendor, string? Device)> PciIdsLookup() =>
        InfoPC.PciIds.LookupMany(new[] { ("8086", "15F3"), ("8086", "7E40"), ("8086", "FFFF"), ("DEAD", "BEEF") });

    [Fact]
    public void Missing_firmware_files_are_extracted_from_the_kernel_log()
    {
        const string log = """
        iwlwifi 0000:03:00.0: Direct firmware load for iwlwifi-ty-a0-gf-a0-77.ucode failed with error -2
        iwlwifi 0000:03:00.0: Direct firmware load for iwlwifi-ty-a0-gf-a0-76.ucode failed with error -2
        i915 0000:00:02.0: Direct firmware load for i915/adlp_dmc_ver2_16.bin failed with error -2
        amdgpu 0000:03:00.0: firmware: failed to load amdgpu/navi10_mes.bin (-2)
        usb 1-1: new device
        """;
        var fw = DriverInfo.MissingFirmware(log);
        Assert.Equal(4, fw.Count);
        Assert.Contains("iwlwifi-ty-a0-gf-a0-77.ucode", fw);
        Assert.Contains("amdgpu/navi10_mes.bin", fw);
        Assert.Empty(DriverInfo.MissingFirmware("tudo certo"));
    }

    private static OsDetails Os(string id, string like) =>
        new("X", id, like, "1", "6.8", "", "x86_64", "h", TimeSpan.Zero, null, null, null, "UEFI", null, null, null, "glibc");

    [Fact]
    public void Gpu_advice_follows_vendor_driver_and_distribution()
    {
        var nvOk = new GpuDevice { Vendor = GpuVendor.Nvidia, Driver = "nvidia", VendorId = "10DE", DeviceId = "2786" };
        Assert.True(DriverInfo.Advice(nvOk, Os("ubuntu", "debian")).Ok);

        var nouveau = new GpuDevice { Vendor = GpuVendor.Nvidia, Driver = "nouveau", VendorId = "10DE", DeviceId = "2786" };
        var ub = DriverInfo.Advice(nouveau, Os("ubuntu", "debian"), "nvidia-driver-550");
        Assert.False(ub.Ok);
        Assert.Equal("DRIVER LIVRE", ub.Status);
        Assert.True(ub.CanAutoInstall);
        Assert.Contains("nvidia-driver-550", ub.Text);
        Assert.Equal("sudo ubuntu-drivers autoinstall", ub.Command);

        Assert.Contains("akmod-nvidia", DriverInfo.Advice(nouveau, Os("fedora", "")).Command);
        Assert.Contains("pacman -S nvidia", DriverInfo.Advice(nouveau, Os("arch", "")).Command);
        Assert.Contains("nvidia-driver-G06", DriverInfo.Advice(nouveau, Os("opensuse-tumbleweed", "opensuse suse")).Command);
        Assert.False(DriverInfo.Advice(nouveau, Os("fedora", "")).CanAutoInstall);

        Assert.True(DriverInfo.Advice(new GpuDevice { Vendor = GpuVendor.Amd, Driver = "amdgpu" }, Os("arch", "")).Ok);
        Assert.False(DriverInfo.Advice(new GpuDevice { Vendor = GpuVendor.Amd, Driver = "radeon" }, Os("arch", "")).Ok);
        Assert.True(DriverInfo.Advice(new GpuDevice { Vendor = GpuVendor.Intel, Driver = "xe" }, Os("arch", "")).Ok);
        Assert.False(DriverInfo.Advice(new GpuDevice { Vendor = GpuVendor.Intel }, Os("arch", "")).Ok);
        Assert.True(DriverInfo.Advice(new GpuDevice { Vendor = GpuVendor.Other, VendorId = "1414", DeviceId = "0006" }, Os("ubuntu", "")).Ok);   // virtual
    }

    [Fact]
    public void Ubuntu_recommended_driver_is_parsed()
    {
        Shell.Override = (f, _) => f == "ubuntu-drivers" ? new ProcResult(0, """
            == /sys/devices/pci0000:00/0000:00:01.0/0000:01:00.0 ==
            modalias : pci:v000010DEd00002786sv00001043sd00008929bc03sc00i00
            vendor   : NVIDIA Corporation
            driver   : nvidia-driver-535 - distro non-free
            driver   : nvidia-driver-550 - distro non-free recommended
            driver   : xserver-xorg-video-nouveau - distro free builtin
            """, "") : ProcResult.Missing(f);
        try { Assert.Equal("nvidia-driver-550", DriverInfo.UbuntuRecommended()); }
        finally { Shell.Override = null; }
    }

    [Fact]
    public void Fwupd_json_is_parsed_and_empty_results_are_explained()
    {
        const string json = """
        {"Devices":[{"Name":"UEFI dbx","Vendor":"UEFI:Microsoft","Version":"77","Releases":[{"Version":"217","Summary":"Atualiza a lista de revogação"}]},
                    {"Name":"Sem release","Version":"1"}]}
        """;
        var (list, msg) = DriverInfo.ParseFwupd(json, 0);
        var u = Assert.Single(list);
        Assert.Equal("UEFI dbx", u.Device);
        Assert.Equal("217", u.Available);
        Assert.Contains("1 atualização", msg);
        Assert.Contains("Nenhuma atualização", DriverInfo.ParseFwupd("", 2).Message);
        Assert.Contains("não devolveu", DriverInfo.ParseFwupd("", 1).Message);
        Assert.Contains("Não entendi", DriverInfo.ParseFwupd("{ quebrado", 0).Message);
    }

    [Fact]
    public void Export_text_lists_devices_and_modules()
    {
        using var t = new TempRoot();
        t.Write("/proc/modules", "nvidia 56000000 142 - Live 0x0\nsnd_hda_intel 61440 3 - Live 0x0\n");
        t.Write("/sys/module/snd_hda_intel/version", "1.0");
        string text = DriverInfo.ExportText(Os("ubuntu", "debian"));
        Assert.Contains("== Módulos do kernel carregados ==", text);
        Assert.Contains("nvidia", text);
        Assert.Contains("versão 1.0", text);
        Assert.Equal(2, DriverInfo.Modules().Count);
    }
}

public class MakerTests
{
    private static void Usb(TempRoot t, string id, string vid, string pid, string product, string? tty = null, bool acm = false)
    {
        t.Write($"/sys/bus/usb/devices/{id}/idVendor", vid);
        t.Write($"/sys/bus/usb/devices/{id}/idProduct", pid);
        t.Write($"/sys/bus/usb/devices/{id}/manufacturer", "Fabricante");
        t.Write($"/sys/bus/usb/devices/{id}/product", product);
        t.Mkdir($"/sys/bus/usb/devices/{id}/{id}:1.0");
        if (tty != null) t.Mkdir(acm ? $"/sys/bus/usb/devices/{id}/{id}:1.0/tty/{tty}" : $"/sys/bus/usb/devices/{id}/{id}:1.0/{tty}");
    }

    private static void ShellFor(string stat, string groups) =>
        Shell.Override = (f, a) => f switch
        {
            "stat" => new ProcResult(0, stat + "\n", ""),
            "id" => new ProcResult(0, groups + "\n", ""),
            "modinfo" => a.Count > 0 && a[^1] == "ch341" ? new ProcResult(0, "/lib/modules/6.8/kernel/drivers/usb/serial/ch341.ko.zst\n", "") : new ProcResult(1, "", "not found"),
            "systemctl" => new ProcResult(0, "inactive\n", ""),
            _ => ProcResult.Missing(f),
        };

    [Fact]
    public void Known_chips_are_recognised_by_vendor_id()
    {
        Assert.Equal("CH340/CH341", Maker.ChipByVid("1a86")!.Name);
        Assert.Equal("cp210x", Maker.ChipByVid("10C4")!.Module);
        Assert.Equal("cdc_acm", Maker.ChipByVid("303A")!.Module);
        Assert.Null(Maker.ChipByVid("DEAD"));
    }

    [Fact]
    public void A_working_board_in_the_right_group_is_reported_as_functioning()
    {
        if (!TempRoot.ColonsOk) return;
        using var t = new TempRoot();
        Usb(t, "1-2", "1a86", "7523", "USB Serial", "ttyUSB0");
        ShellFor("dialout|660", "tester users dialout");
        var d = Assert.Single(Maker.Devices());
        Assert.Equal("CH340/CH341", d.Chip!.Name);
        Assert.Equal("/dev/ttyUSB0", d.Node);
        Assert.True(d.Ok);
        Assert.Equal("FUNCIONANDO", d.Verdict);
        Assert.Equal("1A86", d.Vid);
        Assert.Equal("7523", d.Pid);
    }

    [Fact]
    public void Missing_dialout_membership_is_explained_with_the_fix()
    {
        if (!TempRoot.ColonsOk) return;
        using var t = new TempRoot();
        Usb(t, "1-2", "2341", "0043", "Arduino Uno", "ttyACM0", acm: true);
        ShellFor("dialout|660", "tester users");
        var d = Assert.Single(Maker.Devices());
        Assert.False(d.Ok);
        Assert.Equal("SEM PERMISSÃO", d.Verdict);
        Assert.Equal("/dev/ttyACM0", d.Node);
        Assert.Contains(d.Advice, a => a.Contains("dialout") && a.Contains("ADICIONAR AO GRUPO"));
    }

    [Fact]
    public void Group_added_but_session_not_refreshed_asks_to_log_out_and_in()
    {
        if (!TempRoot.ColonsOk) return;
        using var t = new TempRoot();
        Usb(t, "1-2", "10c4", "ea60", "CP2102", "ttyUSB0");
        t.Write("/etc/group", "dialout:x:20:tester,outro\n");
        ShellFor("dialout|660", "tester users");
        Environment.SetEnvironmentVariable("USER", "tester");
        var d = Assert.Single(Maker.Devices());
        // o nome do usuário vem de Environment.UserName; no teste o grupo é achado pelo nome do usuário real da máquina
        Assert.True(Maker.GroupPending("dialout", "tester"));
        Assert.False(Maker.GroupPending("dialout", "fulano"));
        Assert.False(Maker.GroupPending("", "tester"));
        Assert.Equal("SEM PERMISSÃO", d.Verdict.Replace("SAIA E ENTRE", "SEM PERMISSÃO"));
    }

    [Fact]
    public void Open_mode_ports_do_not_need_the_group()
    {
        if (!TempRoot.ColonsOk) return;
        using var t = new TempRoot();
        Usb(t, "1-2", "0403", "6001", "FT232R", "ttyUSB0");
        ShellFor("tty|666", "tester users");
        Assert.True(Assert.Single(Maker.Devices()).Ok);
    }

    [Fact]
    public void A_ch340_without_a_port_points_to_the_module_and_to_brltty()
    {
        if (!TempRoot.ColonsOk) return;
        using var t = new TempRoot();
        Usb(t, "1-3", "1a86", "7523", "USB Serial");                      // sem ttyUSB e sem driver ligado
        t.Write("/usr/lib/udev/rules.d/85-brltty.rules", "# brltty");
        ShellFor("", "tester");
        var d = Assert.Single(Maker.Devices());
        Assert.Equal("SEM PORTA", d.Verdict);
        Assert.Contains(d.Advice, a => a.Contains("ch341") && a.Contains("modprobe"));
        Assert.Contains(d.Advice, a => a.Contains("brltty"));
        Assert.Equal("sem porta", d.Node);
    }

    [Fact]
    public void A_missing_kernel_module_points_to_extra_modules_and_the_vendor_driver()
    {
        if (!TempRoot.ColonsOk) return;
        using var t = new TempRoot();
        Usb(t, "1-3", "10c4", "ea60", "CP2102");                          // cp210x: o modinfo do teste não conhece
        ShellFor("", "tester");
        var d = Assert.Single(Maker.Devices());
        Assert.Contains(d.Advice, a => a.Contains("cp210x") && a.Contains("modules-extra"));
    }

    [Fact]
    public void Unknown_usb_devices_and_interfaces_are_ignored()
    {
        if (!TempRoot.ColonsOk) return;
        using var t = new TempRoot();
        Usb(t, "1-4", "046d", "c52b", "Receptor do mouse");
        t.Write("/sys/bus/usb/devices/usb1/idVendor", "1d6b");          // hub raiz
        ShellFor("", "tester");
        Assert.Empty(Maker.Devices());
    }

    [Fact]
    public void Udev_rule_text_ignores_modemmanager_and_grants_uaccess_for_every_known_vendor()
    {
        string rule = Maker.RuleText();
        foreach (var vid in new[] { "1a86", "10c4", "0403", "067b", "2341", "2a03", "303a" })
        {
            Assert.Contains($"ATTR{{idVendor}}==\"{vid}\"", rule);
            Assert.Contains($"ATTRS{{idVendor}}==\"{vid}\", ENV{{ID_MM_DEVICE_IGNORE}}=\"1\", TAG+=\"uaccess\"", rule);
        }
        Assert.Contains("apague este arquivo", rule);
        Assert.DoesNotContain("\r", rule);
    }

    [Fact]
    public void Fixes_validate_names_and_pass_data_as_arguments_not_inside_scripts()
    {
        using var t = new TempRoot();
        t.Write("/etc/group", "dialout:x:20:\n");
        var calls = new List<(string F, string[] A)>();
        Elevate.Override = (f, a) => { calls.Add((f, a.ToArray())); return new ProcResult(0, "", ""); };
        try
        {
            var s = new AppSettings();
            Assert.False(Maker.LoadModule("ch341; rm -rf /").Ok);
            Assert.False(Maker.AddUserToGroup("dialout; reboot", s, () => { }, "tester").Ok);
            Assert.False(Maker.AddUserToGroup("dialout", s, () => { }, "te ster").Ok);
            Assert.Empty(calls);                                                   // nada inválido chega a pedir senha
            Assert.Empty(s.GroupsAdded);

            Assert.True(Maker.LoadModule("ch341").Ok);
            Assert.Equal("modprobe", calls[0].F);
            Assert.Equal(new[] { "ch341" }, calls[0].A);
            Assert.True(Maker.AddUserToGroup("dialout", s, () => { }, "tester").Ok);
            Assert.Equal("usermod", calls[1].F);
            Assert.Equal(new[] { "-aG", "dialout", "tester" }, calls[1].A);

            Assert.True(Maker.InstallRule().Ok);
            var (f, a) = calls[2];
            Assert.Equal("sh", f);
            Assert.DoesNotContain("1a86", a[1]);                                   // o texto da regra vai como argumento, não dentro do roteiro
            Assert.Equal(Maker.RuleText(), a[3]);
            Assert.Equal(Maker.RuleFile, a[4]);
        }
        finally { Elevate.Override = null; }
    }

    [Fact]
    public void Chip_statuses_report_loaded_available_and_connected_boards()
    {
        if (!TempRoot.ColonsOk) return;
        using var t = new TempRoot();
        t.Mkdir("/sys/module/cp210x");
        t.Write("/sys/module/cp210x/version", "v1.0");
        Usb(t, "1-2", "10c4", "ea60", "CP2102", "ttyUSB0");
        ShellFor("dialout|660", "tester dialout");
        var st = Maker.ChipStatuses(Maker.Devices());
        var cp = st.Single(s => s.Chip.Module == "cp210x");
        Assert.True(cp.Loaded);
        Assert.Equal("v1.0", cp.Version);
        Assert.Single(cp.Devices);
        Assert.False(st.Single(s => s.Chip.Module == "ch341").Loaded);
        Assert.True(st.Single(s => s.Chip.Module == "ch341").Available);        // o modinfo falso conhece o ch341
        Assert.Equal(Maker.Chips.Length, st.Count);
    }
}

/// <summary>Desfazer do Maker (R09) e das instalações de pacotes (R10, brltty).</summary>
public class UndoTests
{
    [Fact]
    public void Adding_to_a_group_is_recorded_first_and_can_be_undone()
    {
        using var t = new TempRoot();
        var calls = new List<string>();
        Elevate.Override = (f, a) =>
        {
            calls.Add(f + " " + string.Join(' ', a));
            if (f == "usermod") t.Write("/etc/group", "dialout:x:20:outro,tester\n");
            if (f == "gpasswd") t.Write("/etc/group", "dialout:x:20:outro\n");
            return new ProcResult(0, "", "");
        };
        t.Write("/etc/group", "dialout:x:20:outro\n");
        var s = new AppSettings();

        var r = Maker.AddUserToGroup("dialout", s, () => throw new IOException("disco cheio"), "tester");
        Assert.False(r.Ok);
        Assert.Contains("Nada foi alterado", r.Message);
        Assert.Empty(calls);                                                      // sem o desfazer no disco, o usermod nem roda
        Assert.Empty(s.GroupsAdded);

        var savedBefore = new List<int>();
        Assert.True(Maker.AddUserToGroup("dialout", s, () => savedBefore.Add(calls.Count), "tester").Ok);
        Assert.Equal(new[] { 0 }, savedBefore);                                   // gravou antes do usermod
        Assert.Equal(new[] { "dialout" }, s.GroupsAdded.ToArray());

        Assert.False(Maker.RemoveUserFromGroup("video", s, () => { }, "tester").Ok);  // só desfaz o que o InfoPC fez
        Assert.True(Maker.RemoveUserFromGroup("dialout", s, () => { }, "tester").Ok);
        Assert.Equal("gpasswd -d tester dialout", calls.Last());
        Assert.Equal("dialout:x:20:outro\n", t.Read("/etc/group"));                // os outros membros continuam
        Assert.Empty(s.GroupsAdded);
    }

    [Fact]
    public void A_failed_usermod_leaves_nothing_to_undo_and_a_user_who_left_is_just_forgotten()
    {
        using var t = new TempRoot();
        t.Write("/etc/group", "dialout:x:20:\n");
        Elevate.Override = (_, _) => new ProcResult(126, "", "cancelado");
        var s = new AppSettings();
        Assert.False(Maker.AddUserToGroup("dialout", s, () => { }, "tester").Ok);
        Assert.Empty(s.GroupsAdded);

        s.GroupsAdded.Add("dialout");                                             // o usuário saiu do grupo por fora
        Elevate.Override = (_, _) => throw new InvalidOperationException("não deveria pedir senha");
        Assert.True(Maker.RemoveUserFromGroup("dialout", s, () => { }, "tester").Ok);
        Assert.Empty(s.GroupsAdded);
    }

    [Fact]
    public void A_group_the_user_was_already_in_is_not_recorded_and_undo_cannot_take_it_away()
    {
        // R09-C: já em /etc/group, mas a sessão ainda não vê o grupo. O usermod não acrescentaria nada, e o desfazer tiraria uma associação anterior
        using var t = new TempRoot();
        t.Write("/etc/group", "dialout:x:20:tester,outro\n");
        Elevate.Override = (_, _) => throw new InvalidOperationException("não deveria pedir senha");
        var s = new AppSettings();
        var saves = 0;

        var r = Maker.AddUserToGroup("dialout", s, () => saves++, "tester");
        Assert.False(r.Ok);
        Assert.Contains("Saia da sessão", r.Message);
        Assert.Empty(s.GroupsAdded);
        Assert.Equal(0, saves);
        Assert.True(Maker.GroupPending("dialout", "tester"));
        Assert.False(Maker.CanAddToGroup("dialout", "tester"));                   // o botão nem aparece

        Assert.False(Maker.RemoveUserFromGroup("dialout", s, () => { }, "tester").Ok);
        Assert.Equal("dialout:x:20:tester,outro\n", t.Read("/etc/group"));
    }

    [Fact]
    public void Membership_that_cannot_be_read_is_not_taken_as_absence()
    {
        using var t = new TempRoot();                                             // sem /etc/group
        Elevate.Override = (_, _) => throw new InvalidOperationException("não deveria pedir senha");
        var s = new AppSettings();
        Assert.Null(Maker.InGroupFile("dialout", "tester"));

        var add = Maker.AddUserToGroup("dialout", s, () => { }, "tester");
        Assert.False(add.Ok);
        Assert.Contains("/etc/group", add.Message);
        Assert.Empty(s.GroupsAdded);

        s.GroupsAdded.Add("dialout");
        var remove = Maker.RemoveUserFromGroup("dialout", s, () => { }, "tester");
        Assert.False(remove.Ok);
        Assert.Equal(new[] { "dialout" }, s.GroupsAdded.ToArray());               // o desfazer não é esquecido por uma leitura que falhou
    }

    /// <summary>Configurações numa pasta temporária, para os testes que salvam e reabrem de verdade.</summary>
    private sealed class TempSettings : IDisposable
    {
        private readonly string _old = AppSettings.Folder;
        private readonly string _dir = Directory.CreateTempSubdirectory("infopc-cfg").FullName;
        public TempSettings() => AppSettings.Folder = _dir;
        public void Dispose() { AppSettings.Folder = _old; Directory.Delete(_dir, true); }
    }

    /// <summary>Simula o dpkg-query: cada pacote é "nome", "nome:arq" ou "nome:arq=versão" (o padrão é amd64 e versão 1.0).</summary>
    private static void Dpkg(TempRoot t, Func<string[]> installed) =>
        Shell.Override = (f, a) => f == "dpkg-query"
            ? new ProcResult(0, string.Concat(installed().Select(p => $"install ok installed\t{Row(p)}\n")) + "deinstall ok config-files\tantigo\tamd64\t0.9\n", "")
            : ProcResult.Missing(f);

    private static string Row(string p)
    {
        var (name, version) = p.Split('=') is { Length: 2 } v ? (v[0], v[1]) : (p, "1.0");
        var (pkg, arch) = name.Split(':') is { Length: 2 } n ? (n[0], n[1]) : (name, "amd64");
        return $"{pkg}\t{arch}\t{version}";
    }

    private static string[] Ids(IEnumerable<PackageId> ids) => ids.Select(p => p.ToString()).ToArray();

    [Fact]
    public void Gpu_driver_install_records_what_came_in_and_undo_reverses_it_in_one_apt_transaction()
    {
        using var t = new TempRoot();
        var pkgs = new List<string> { "bash", "nvidia-driver-535" };
        Dpkg(t, () => pkgs.ToArray());
        var calls = new List<string[]>();
        Elevate.Override = (f, a) =>
        {
            calls.Add(a.Prepend(f).ToArray());
            if (f == "ubuntu-drivers") { pkgs.Remove("nvidia-driver-535"); pkgs.Add("nvidia-driver-550"); pkgs.Add("nvidia-utils-550"); }
            return new ProcResult(0, "", "");
        };
        var s = new AppSettings();
        var (r, warning) = DriverInfo.InstallUbuntuDrivers(s, () => { });
        Assert.True(r.Ok);
        Assert.Null(warning);
        var c = Assert.Single(s.PackageChanges);
        Assert.Equal("gpu-driver", c.Kind);
        Assert.False(c.Pending);
        Assert.Null(c.Before);                                                    // a lista inteira só fica no disco enquanto é preciso
        Assert.Equal(new[] { "nvidia-driver-550:amd64 1.0", "nvidia-utils-550:amd64 1.0" }, Ids(c.Added));
        Assert.Equal(new[] { "nvidia-driver-535:amd64 1.0" }, Ids(c.Removed));
        Assert.DoesNotContain(c.Removed, p => p.Name == "antigo");                // pacote só com configuração não conta como instalado

        Assert.True(Packages.Undo(c, s, () => { }).Ok);
        Assert.Equal(new[] { "apt-get", "install", "-y", "nvidia-driver-535:amd64=1.0", "nvidia-driver-550:amd64-", "nvidia-utils-550:amd64-" }, calls.Last());
        Assert.Empty(s.PackageChanges);
    }

    [Fact]
    public void Nothing_is_installed_without_the_list_of_packages_from_before()
    {
        using var t = new TempRoot();
        Shell.Override = (f, _) => f == "dpkg-query" ? new ProcResult(1, "", "falhou") : ProcResult.Missing(f);
        Elevate.Override = (_, _) => throw new InvalidOperationException("não deveria instalar sem poder desfazer");
        var s = new AppSettings();
        var (r, _) = DriverInfo.InstallUbuntuDrivers(s, () => { });
        Assert.False(r.Ok);
        Assert.Contains("Nada foi alterado", r.Message);
        Assert.Empty(s.PackageChanges);
    }

    [Fact]
    public void Nothing_is_installed_when_the_list_of_packages_from_before_cannot_be_saved()
    {
        using var t = new TempRoot();
        Dpkg(t, () => new[] { "bash" });
        Elevate.Override = (_, _) => throw new InvalidOperationException("não deveria instalar sem o desfazer no disco");
        var s = new AppSettings();
        var (r, _) = DriverInfo.InstallUbuntuDrivers(s, () => throw new IOException("disco cheio"));
        Assert.False(r.Ok);
        Assert.Contains("Nada foi alterado", r.Message);
        Assert.Empty(s.PackageChanges);
    }

    [Fact]
    public void The_list_from_before_is_on_disk_before_the_action_runs()
    {
        using var t = new TempRoot();
        var pkgs = new List<string> { "bash" };
        Dpkg(t, () => pkgs.ToArray());
        var s = new AppSettings();
        var events = new List<string>();
        Elevate.Override = (f, _) => { events.Add("ação"); pkgs.Add("nvidia-driver-550"); return new ProcResult(0, "", ""); };
        DriverInfo.InstallUbuntuDrivers(s, () => events.Add(s.PackageChanges.Single().Pending ? "gravou pendente" : "gravou final"));
        Assert.Equal(new[] { "gravou pendente", "ação", "gravou final" }, events.ToArray());
    }

    [Fact]
    public void After_list_failure_keeps_the_operation_pending_and_reopening_completes_the_undo()
    {
        // R10-A: a ação rodou, a lista de depois falhou. Antes a lista de antes estava só numa variável local e a mudança ficava sem desfazer
        using var t = new TempRoot();
        using var cfg = new TempSettings();
        NoInstaller(t);
        var pkgs = new List<string> { "bash", "nvidia-driver-535" };
        bool listWorks = true;
        Shell.Override = (f, _) => f != "dpkg-query" ? ProcResult.Missing(f)
            : listWorks ? new ProcResult(0, string.Concat(pkgs.Select(p => $"install ok installed\t{Row(p)}\n")), "") : new ProcResult(1, "", "falhou");
        var calls = new List<string[]>();
        Elevate.Override = (f, a) =>
        {
            calls.Add(a.Prepend(f).ToArray());
            if (f == "ubuntu-drivers") { pkgs.Remove("nvidia-driver-535"); pkgs.Add("nvidia-driver-550"); listWorks = false; }
            return new ProcResult(0, "", "");
        };
        var s = new AppSettings();
        s.Save();
        var (r, warning) = DriverInfo.InstallUbuntuDrivers(s, s.Save);
        Assert.True(r.Ok);
        Assert.Contains("lista de antes está guardada", warning);

        // o InfoPC fecha; o que vale é o que foi para o disco
        var reopened = AppSettings.Load();
        Assert.Null(reopened.LoadError);
        var c = Assert.Single(reopened.PackageChanges);
        Assert.True(c.Pending);
        Assert.Contains(new PackageId("nvidia-driver-535", "amd64", "1.0"), c.Before!);

        // sem a lista final não há o que desfazer: não pode "dar certo" e esquecer o registro
        Assert.False(Packages.Undo(c, reopened, reopened.Save).Ok);
        Assert.Single(reopened.PackageChanges);

        // a lista ainda não funciona: continua pendente, e uma nova ação não se mistura com esta
        Packages.CompletePending(reopened, reopened.Save);
        Assert.True(c.Pending);
        var (again, _) = DriverInfo.InstallUbuntuDrivers(reopened, reopened.Save);
        Assert.False(again.Ok);
        Assert.Single(calls);

        listWorks = true;
        Packages.CompletePending(reopened, reopened.Save);
        var done = Assert.Single(AppSettings.Load().PackageChanges);              // e isso também foi para o disco
        Assert.False(done.Pending);
        Assert.True(done.Recovered);                                              // a tela avisa para conferir a lista
        Assert.Equal(new[] { "nvidia-driver-550:amd64 1.0" }, Ids(done.Added));
        Assert.Equal(new[] { "nvidia-driver-535:amd64 1.0" }, Ids(done.Removed));

        Assert.True(Packages.Undo(reopened.PackageChanges[0], reopened, reopened.Save).Ok);
        Assert.Equal(new[] { "apt-get", "install", "-y", "nvidia-driver-535:amd64=1.0", "nvidia-driver-550:amd64-" }, calls.Last());
        Assert.Empty(AppSettings.Load().PackageChanges);
    }

    [Fact]
    public void Undo_record_that_cannot_be_saved_comes_back_as_a_manual_command_and_stays_pending_on_disk()
    {
        using var t = new TempRoot();
        using var cfg = new TempSettings();
        NoInstaller(t);
        var pkgs = new List<string> { "bash" };
        Dpkg(t, () => pkgs.ToArray());
        Elevate.Override = (f, _) => { if (f == "ubuntu-drivers") pkgs.Add("nvidia-driver-550"); return new ProcResult(0, "", ""); };
        var s = new AppSettings();
        int saves = 0;
        var (r, warning) = DriverInfo.InstallUbuntuDrivers(s, () =>
        {
            if (++saves > 1) throw new IOException("disco cheio");
            s.Save();                                                             // só a gravação de antes da ação chega ao disco
        });
        Assert.True(r.Ok);
        Assert.Contains("sudo apt-get install nvidia-driver-550:amd64-", warning);

        var reopened = AppSettings.Load();
        Assert.True(Assert.Single(reopened.PackageChanges).Pending);
        Packages.CompletePending(reopened, () => { });
        Assert.Equal(new[] { "nvidia-driver-550:amd64 1.0" }, Ids(reopened.PackageChanges[0].Added));
    }

    /// <summary>Um /proc em que dá para ver os processos do sistema, sem nenhum instalador.</summary>
    private static void NoInstaller(TempRoot t) => t.Write("/proc/1/comm", "systemd\n");

    /// <summary>Uma operação no disco, como o Track grava antes da ação, e as configurações reabertas.</summary>
    private static AppSettings ClosedDuringAction(params string[] before)
    {
        var s = new AppSettings();
        s.PackageChanges.Add(new PackageChange { Kind = "gpu-driver", What = "driver", Manager = "apt", When = DateTime.Now, Pending = true,
                                                 Before = before.Select(p => new PackageId(p, "amd64", "1.0")).ToList() });
        s.Save();
        return AppSettings.Load();
    }

    [Fact]
    public void Reopening_before_the_installer_changes_anything_keeps_the_list_from_before()
    {
        // R10-C: a lista igual à de antes parecia "não mudou nada", e o registro era apagado com o apt ainda baixando os pacotes
        using var t = new TempRoot();
        using var cfg = new TempSettings();
        NoInstaller(t);
        t.Write("/proc/4242/comm", "apt-get\n");
        var pkgs = new List<string> { "bash" };
        Dpkg(t, () => pkgs.ToArray());
        var reopened = ClosedDuringAction("bash");

        Packages.CompletePending(reopened, reopened.Save);
        var saved = Assert.Single(AppSettings.Load().PackageChanges);
        Assert.True(saved.Pending);
        Assert.Single(saved.Before!);

        pkgs.Add("nvidia-driver-550");
        File.Delete(Path.Combine(t.Dir, "proc", "4242", "comm"));
        Packages.CompletePending(reopened, reopened.Save);
        var done = Assert.Single(AppSettings.Load().PackageChanges);
        Assert.False(done.Pending);
        Assert.True(done.Recovered);
        Assert.Equal(new[] { "nvidia-driver-550:amd64 1.0" }, Ids(done.Added));
    }

    [Fact]
    public void Reopening_between_two_changes_of_the_installer_does_not_record_only_part_of_them()
    {
        // R10-C: concluir com o primeiro pacote descartava a lista de antes, e o segundo ficava sem desfazer
        using var t = new TempRoot();
        using var cfg = new TempSettings();
        NoInstaller(t);
        t.Write("/proc/4242/comm", "dpkg\n");
        var pkgs = new List<string> { "bash", "nvidia-driver-550" };
        Dpkg(t, () => pkgs.ToArray());
        var reopened = ClosedDuringAction("bash");

        Packages.CompletePending(reopened, reopened.Save);
        Assert.True(reopened.PackageChanges.Single().Pending);

        pkgs.Add("nvidia-utils-550");
        File.Delete(Path.Combine(t.Dir, "proc", "4242", "comm"));
        Packages.CompletePending(reopened, reopened.Save);
        Assert.Equal(new[] { "nvidia-driver-550:amd64 1.0", "nvidia-utils-550:amd64 1.0" }, Ids(AppSettings.Load().PackageChanges.Single().Added));
    }

    [Theory]
    [InlineData("pkexec")]          // esperando a senha: depois dela, vira o instalador
    [InlineData("ubuntu-drivers")]
    [InlineData("apt")]
    [InlineData("unattended-upgr")]
    [InlineData("dnf")]
    [InlineData("zypper")]
    [InlineData("pacman")]
    public void Every_installer_keeps_the_operation_pending(string comm)
    {
        using var t = new TempRoot();
        NoInstaller(t);
        t.Write("/proc/31337/comm", comm + "\n");
        Assert.Equal(InstallerState.Active, Packages.InstallerActivity());
    }

    [Fact]
    public void An_installer_that_renames_itself_is_still_found_by_its_command_line()
    {
        // visto no openSUSE real: o zypper passa a aparecer como "Zypp-main", e a operação era concluída com ele rodando
        using var t = new TempRoot();
        NoInstaller(t);
        t.Write("/proc/4242/comm", "Zypp-main\n");
        t.Write("/proc/4242/cmdline", "/usr/bin/zypper\0--non-interactive\0install\0tree\0");
        Assert.Equal(InstallerState.Active, Packages.InstallerActivity());
    }

    [Fact]
    public void A_script_is_found_by_its_comm_even_with_the_interpreter_in_the_command_line()
    {
        using var t = new TempRoot();
        NoInstaller(t);
        t.Write("/proc/4242/comm", "ubuntu-drivers\n");
        t.Write("/proc/4242/cmdline", "/usr/bin/python3\0/usr/bin/ubuntu-drivers\0autoinstall\0");
        Assert.Equal(InstallerState.Active, Packages.InstallerActivity());
    }

    [Fact]
    public void Other_programs_do_not_count_as_an_installer()
    {
        using var t = new TempRoot();
        NoInstaller(t);
        t.Write("/proc/100/comm", "dpkg-query\n");
        t.Write("/proc/101/comm", "apt-cache\n");
        t.Write("/proc/102/comm", "packagekitd\n");
        t.Write("/proc/103/comm", "vim\n");
        t.Write("/proc/103/cmdline", "vim\0/etc/apt/apt.conf\0apt\0");         // só o programa conta, não os argumentos
        t.Write("/proc/acpi/comm", "apt\n");                                     // só as pastas numeradas são processos
        Assert.Equal(InstallerState.Idle, Packages.InstallerActivity());
    }

    [Fact]
    public void With_the_installer_gone_and_nothing_changed_the_record_is_removed()
    {
        // fechou antes da senha, ou a senha foi recusada: não há o que desfazer
        using var t = new TempRoot();
        using var cfg = new TempSettings();
        NoInstaller(t);
        Dpkg(t, () => new[] { "bash" });
        var reopened = ClosedDuringAction("bash");
        Packages.CompletePending(reopened, reopened.Save);
        Assert.Empty(AppSettings.Load().PackageChanges);
    }

    [Fact]
    public void A_hidden_proc_keeps_the_operation_pending_until_the_user_confirms()
    {
        // hidepid: sem /proc/1/comm não há evidência de que o instalador terminou
        using var t = new TempRoot();
        using var cfg = new TempSettings();
        var pkgs = new List<string> { "bash", "nvidia-driver-550" };
        Dpkg(t, () => pkgs.ToArray());
        var reopened = ClosedDuringAction("bash");
        Assert.Equal(InstallerState.Unknown, Packages.InstallerActivity());

        Packages.CompletePending(reopened, reopened.Save);
        Assert.True(AppSettings.Load().PackageChanges.Single().Pending);

        Packages.ConfirmFinished(reopened, reopened.Save);
        var done = Assert.Single(AppSettings.Load().PackageChanges);
        Assert.False(done.Pending);
        Assert.True(done.Recovered);
        Assert.Equal(new[] { "nvidia-driver-550:amd64 1.0" }, Ids(done.Added));
    }

    [Fact]
    public void Confirming_does_not_conclude_while_an_installer_is_visible()
    {
        using var t = new TempRoot();
        using var cfg = new TempSettings();
        NoInstaller(t);
        t.Write("/proc/4242/comm", "apt-get\n");
        Dpkg(t, () => new[] { "bash", "nvidia-driver-550" });
        var reopened = ClosedDuringAction("bash");
        Packages.ConfirmFinished(reopened, reopened.Save);
        Assert.True(AppSettings.Load().PackageChanges.Single().Pending);
    }

    [Fact]
    public void An_installer_that_starts_during_the_reading_does_not_conclude_the_operation()
    {
        using var t = new TempRoot();
        using var cfg = new TempSettings();
        NoInstaller(t);
        Shell.Override = (f, _) =>
        {
            if (f != "dpkg-query") return ProcResult.Missing(f);
            t.Write("/proc/4242/comm", "apt-get\n");
            return new ProcResult(0, $"install ok installed\t{Row("bash")}\n", "");
        };
        var reopened = ClosedDuringAction("bash");
        Packages.CompletePending(reopened, reopened.Save);
        Assert.True(AppSettings.Load().PackageChanges.Single().Pending);
    }

    [Fact]
    public void After_a_timeout_the_installer_can_still_be_running_so_the_operation_stays_pending()
    {
        // o pkexec morre com o tempo esgotado, mas o usuário comum não mata o instalador, que roda como root
        using var t = new TempRoot();
        NoInstaller(t);
        var pkgs = new List<string> { "bash" };
        Dpkg(t, () => pkgs.ToArray());
        Elevate.Override = (_, _) =>
        {
            pkgs.Add("nvidia-driver-550");
            t.Write("/proc/4242/comm", "apt-get\n");
            return new ProcResult(-1, "", "", TimedOut: true);
        };
        var s = new AppSettings();
        var (r, warning) = DriverInfo.InstallUbuntuDrivers(s, () => { });
        Assert.False(r.Ok);
        Assert.Contains("ainda pode estar rodando", warning);
        var c = Assert.Single(s.PackageChanges);
        Assert.True(c.Pending);
        Assert.Single(c.Before!);
    }

    [Fact]
    public void With_a_hidden_proc_the_normal_end_of_the_action_is_enough_but_a_timeout_is_not()
    {
        using var t = new TempRoot();
        var pkgs = new List<string> { "bash" };
        Dpkg(t, () => pkgs.ToArray());
        bool timedOut = false;
        Elevate.Override = (_, _) => { pkgs.Add("nvidia-driver-550"); return new ProcResult(timedOut ? -1 : 0, "", "", TimedOut: timedOut); };

        var s = new AppSettings();
        DriverInfo.InstallUbuntuDrivers(s, () => { });
        Assert.False(s.PackageChanges.Single().Pending);                          // o processo saiu: a ação terminou

        pkgs.Clear(); pkgs.Add("bash");
        timedOut = true;
        var other = new AppSettings();
        DriverInfo.InstallUbuntuDrivers(other, () => { });
        Assert.True(other.PackageChanges.Single().Pending);
    }

    [Fact]
    public void Nothing_runs_while_another_installer_is_running()
    {
        using var t = new TempRoot();
        NoInstaller(t);
        t.Write("/proc/4242/comm", "apt\n");
        Dpkg(t, () => new[] { "bash" });
        Elevate.Override = (_, _) => throw new InvalidOperationException("as mudanças do outro instalador entrariam neste desfazer");
        var s = new AppSettings();
        var (r, _) = DriverInfo.InstallUbuntuDrivers(s, () => { });
        Assert.False(r.Ok);
        Assert.Contains("outro instalador", r.Message);
        Assert.Empty(s.PackageChanges);
    }

    [Fact]
    public void The_real_package_manager_goes_through_recovery_and_undo()
    {
        // Instala e remove de verdade: só no contêiner descartável do CI, que liga esta variável (linux.yml). Nunca na máquina de alguém
        if (Environment.GetEnvironmentVariable("INFOPC_TEST_REAL_PACKAGES") != "1") return;
        Assert.True(Elevate.IsRoot);
        using var t = new TempRoot();
        using var cfg = new TempSettings();
        Sys.Root = "/";
        Shell.Override = null;
        string manager = OsInfo.PackageManager()!;
        var (install, remove) = manager switch
        {
            "apt" => (new[] { "apt-get", "install", "-y", "tree" }, new[] { "apt-get", "remove", "-y", "tree" }),
            "dnf" => (new[] { "dnf", "install", "-y", "tree" }, new[] { "dnf", "remove", "-y", "tree" }),
            "pacman" => (new[] { "pacman", "-S", "--noconfirm", "tree" }, new[] { "pacman", "-R", "--noconfirm", "tree" }),
            "zypper" => (new[] { "zypper", "--non-interactive", "install", "tree" }, new[] { "zypper", "--non-interactive", "remove", "tree" }),
            _ => throw new InvalidOperationException(manager),
        };
        PackageId? Tree() => Packages.Installed(manager)!.FirstOrDefault(p => p.Name == "tree");
        Assert.Null(Tree());

        // R10-C com um instalador de verdade: o InfoPC reabre com ele rodando
        var s = new AppSettings();
        s.PackageChanges.Add(new PackageChange { Kind = "test", What = "tree", Manager = manager, When = DateTime.Now, Pending = true, Before = Packages.Installed(manager)!.ToList() });
        s.Save();
        using (var p = ProcessRunner.Start(Shell.Which(install[0])!, install.Skip(1))!)
        {
            // durante toda a instalação, e não só no começo: o programa pode mudar de nome ou passar por outro no meio
            while (true)
            {
                string seen = $"comm={Sys.Read($"/proc/{p.Id}/comm")} cmdline={Sys.Read($"/proc/{p.Id}/cmdline")?.Replace('\0', ' ')}";
                Packages.CompletePending(s, s.Save);
                bool pending = AppSettings.Load().PackageChanges.SingleOrDefault()?.Pending == true;
                if (p.HasExited) break;                                           // ainda rodava depois de ler: a pendência tinha de continuar
                Assert.True(pending, seen);
                Thread.Sleep(20);
            }
            Assert.Equal(0, p.ExitCode);
        }
        Packages.CompletePending(s, s.Save);
        var c = s.PackageChanges.Single();
        Assert.False(c.Pending);
        var tree = Tree();
        Assert.NotNull(tree);
        Assert.Contains(tree, c.Added);

        // o desfazer remove o que entrou
        var undo = Packages.Undo(c, s, s.Save);
        Assert.True(undo.Ok, undo.Message);
        Assert.Null(Tree());
        Assert.Empty(s.PackageChanges);

        // a remoção pelo Track: o desfazer devolve a mesma versão, com o formato exato de cada gerenciador
        var installed = Elevate.Run(install[0], install.Skip(1), 600000);
        Assert.True(installed.Ok, installed.Message);
        var (r, warning) = Packages.Track("test", "remoção do tree", manager, s, s.Save, () => Elevate.Run(remove[0], remove.Skip(1), 600000));
        Assert.True(r.Ok, r.Message);
        Assert.Null(warning);
        Assert.Contains(tree, s.PackageChanges.Single().Removed);
        Assert.Null(Tree());
        undo = Packages.Undo(s.PackageChanges.Single(), s, s.Save);
        Assert.True(undo.Ok, undo.Message);
        Assert.Equal(tree, Tree());
        Assert.Empty(s.PackageChanges);
    }

    /// <summary>Como o teste de pacotes acima: age de verdade, então só no contêiner do CI.</summary>
    private static bool RealPackagesAllowed()
    {
        if (Environment.GetEnvironmentVariable("INFOPC_TEST_REAL_PACKAGES") != "1") return false;
        Assert.True(Elevate.IsRoot);
        return true;
    }

    [Fact]
    public void The_real_package_manager_goes_back_to_an_older_version()
    {
        // --allow-downgrades e --oldpackage, com o repositório de criar-repo-teste.sh. O pacman só instala a versão atual, e o desfazer recusa
        if (!RealPackagesAllowed()) return;
        using var t = new TempRoot();
        using var cfg = new TempSettings();
        Sys.Root = "/";
        Shell.Override = null;
        string manager = OsInfo.PackageManager()!;
        if (manager == "pacman") return;
        string[] Install(string version) => manager switch
        {
            "apt" => new[] { "apt-get", "install", "-y", $"infopc-teste={version}" },
            "dnf" => new[] { "dnf", "install", "-y", $"infopc-teste-{version}-1" },
            _ => new[] { "zypper", "--non-interactive", "install", $"infopc-teste={version}-1" },
        };
        PackageId? Test() => Packages.Installed(manager)!.FirstOrDefault(p => p.Name == "infopc-teste");

        var first = Elevate.Run(Install("1.0")[0], Install("1.0").Skip(1), 600000);
        Assert.True(first.Ok, first.Message);
        var old = Test();
        Assert.NotNull(old);

        var s = new AppSettings();
        var (r, warning) = Packages.Track("test", "troca de versão", manager, s, s.Save, () => Elevate.Run(Install("2.0")[0], Install("2.0").Skip(1), 600000));
        Assert.True(r.Ok, r.Message);
        Assert.Null(warning);
        var c = s.PackageChanges.Single();
        Assert.Contains(old, c.Removed);
        var updated = Test();
        Assert.NotEqual(old, updated);
        Assert.Contains(updated, c.Added);

        var undo = Packages.Undo(c, s, s.Save);
        Assert.True(undo.Ok, undo.Message);
        Assert.Equal(old, Test());                                                // voltou, em vez de remover o pacote
        Assert.Empty(s.PackageChanges);
    }

    [Fact]
    public void The_real_group_commands_add_and_remove_only_what_was_added()
    {
        if (!RealPackagesAllowed()) return;
        using var t = new TempRoot();
        using var cfg = new TempSettings();
        Sys.Root = "/";
        Shell.Override = null;
        const string group = "infopcteste";
        var created = Elevate.Run("groupadd", new[] { group });
        Assert.True(created.Ok, created.Message);
        string user = Environment.UserName;

        var s = new AppSettings();
        Assert.False(Maker.InGroupFile(group, user));
        var add = Maker.AddUserToGroup(group, s, s.Save);
        Assert.True(add.Ok, add.Message);
        Assert.True(Maker.InGroupFile(group, user));
        Assert.Contains(group, AppSettings.Load().GroupsAdded);

        // já estava no grupo: não registra de novo
        var again = new AppSettings();
        Assert.False(Maker.AddUserToGroup(group, again, () => { }).Ok);
        Assert.Empty(again.GroupsAdded);

        var remove = Maker.RemoveUserFromGroup(group, s, s.Save);
        Assert.True(remove.Ok, remove.Message);
        Assert.False(Maker.InGroupFile(group, user));
        Assert.Empty(AppSettings.Load().GroupsAdded);
    }

    [Fact]
    public void The_real_proc_shows_a_running_installer()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var t = new TempRoot();
        Sys.Root = "/";
        var baseline = Packages.InstallerActivity();
        Assert.NotEqual(InstallerState.Unknown, baseline);                        // o runner não usa hidepid

        // uma cópia do sleep com o nome de um instalador: o que importa é o nome em /proc/PID/comm
        string fake = Path.Combine(t.Dir, "pacman");
        File.Copy(File.Exists("/bin/sleep") ? "/bin/sleep" : "/usr/bin/sleep", fake);
        using var p = System.Diagnostics.Process.Start(fake, "60");
        try { Assert.Equal(InstallerState.Active, Packages.InstallerActivity()); }
        finally { p.Kill(); p.WaitForExit(); }
        Assert.Equal(baseline, Packages.InstallerActivity());
    }

    [Fact]
    public void An_action_that_changed_nothing_leaves_no_record_or_pending_operation()
    {
        using var t = new TempRoot();
        Dpkg(t, () => new[] { "bash" });
        Elevate.Override = (_, _) => new ProcResult(1, "", "sem rede");
        var s = new AppSettings();
        var (r, warning) = DriverInfo.InstallUbuntuDrivers(s, () => { });
        Assert.False(r.Ok);
        Assert.Null(warning);
        Assert.Empty(s.PackageChanges);
    }

    [Fact]
    public void Opening_the_page_during_the_action_does_not_conclude_it_early()
    {
        using var t = new TempRoot();
        var pkgs = new List<string> { "bash" };
        Dpkg(t, () => pkgs.ToArray());
        var s = new AppSettings();
        Elevate.Override = (_, _) =>
        {
            pkgs.Add("nvidia-driver-550");
            Packages.CompletePending(s, () => { });                               // a página foi aberta no meio da instalação
            Assert.True(s.PackageChanges.Single().Pending);
            Assert.False(Packages.Undo(s.PackageChanges.Single(), s, () => { }).Ok);
            pkgs.Add("nvidia-utils-550");
            return new ProcResult(0, "", "");
        };
        DriverInfo.InstallUbuntuDrivers(s, () => { });
        var c = Assert.Single(s.PackageChanges);
        Assert.False(c.Recovered);
        Assert.Equal(2, c.Added.Count);                                           // pegou também o que entrou depois da abertura
    }

    [Fact]
    public void A_new_architecture_next_to_an_existing_one_is_recorded_and_undo_removes_only_it()
    {
        // R10-B: amd64 já instalado + i386 novo. Pelo nome, os dois eram o mesmo pacote e nada ficava registrado
        using var t = new TempRoot();
        var pkgs = new List<string> { "libnvidia-gl-550:amd64=550.90" };
        Dpkg(t, () => pkgs.ToArray());
        var calls = new List<string[]>();
        Elevate.Override = (f, a) =>
        {
            calls.Add(a.Prepend(f).ToArray());
            if (f == "ubuntu-drivers") pkgs.Add("libnvidia-gl-550:i386=550.90");
            return new ProcResult(0, "", "");
        };
        var s = new AppSettings();
        DriverInfo.InstallUbuntuDrivers(s, () => { });
        var c = Assert.Single(s.PackageChanges);
        Assert.Equal(new[] { "libnvidia-gl-550:i386 550.90" }, Ids(c.Added));
        Assert.Empty(c.Removed);
        Assert.True(Packages.Undo(c, s, () => { }).Ok);
        Assert.Equal(new[] { "apt-get", "install", "-y", "libnvidia-gl-550:i386-" }, calls.Last());
    }

    [Fact]
    public void A_version_change_under_the_same_name_is_recorded_and_undo_goes_back_to_the_old_version()
    {
        // R10-B: 550.90 -> 550.120 no mesmo pacote. Remover o novo tiraria o driver em vez de voltar a versão
        using var t = new TempRoot();
        var pkgs = new List<string> { "nvidia-driver-550:amd64=550.90" };
        Dpkg(t, () => pkgs.ToArray());
        var calls = new List<string[]>();
        Elevate.Override = (f, a) =>
        {
            calls.Add(a.Prepend(f).ToArray());
            if (f == "ubuntu-drivers") pkgs[0] = "nvidia-driver-550:amd64=550.120";
            return new ProcResult(0, "", "");
        };
        var s = new AppSettings();
        DriverInfo.InstallUbuntuDrivers(s, () => { });
        var c = Assert.Single(s.PackageChanges);
        Assert.Equal(new[] { "nvidia-driver-550:amd64 550.120" }, Ids(c.Added));
        Assert.Equal(new[] { "nvidia-driver-550:amd64 550.90" }, Ids(c.Removed));
        Assert.Equal("sudo apt-get install --allow-downgrades nvidia-driver-550:amd64=550.90", Packages.ManualCommand(c));
        Assert.True(Packages.Undo(c, s, () => { }).Ok);
        Assert.Equal(new[] { "apt-get", "install", "-y", "--allow-downgrades", "nvidia-driver-550:amd64=550.90" }, calls.Last());
    }

    [Fact]
    public void Undo_that_fails_keeps_the_record_and_shows_the_command_without_the_version()
    {
        using var t = new TempRoot();
        Elevate.Override = (_, _) => new ProcResult(100, "", "E: Version '1.0' for 'brltty' was not found");
        var s = new AppSettings();
        var c = new PackageChange { Kind = "brltty", Manager = "apt", Removed = { new("brltty", "amd64", "1.0"), new("brltty-doc", "all", "1.0") } };
        s.PackageChanges.Add(c);
        var r = Packages.Undo(c, s, () => { });
        Assert.False(r.Ok);
        Assert.Contains("was not found", r.Message);
        Assert.Contains("sudo apt-get install brltty:amd64 brltty-doc", r.Message);
        Assert.Single(s.PackageChanges);
    }

    [Fact]
    public void Firmware_is_only_checked_never_installed()
    {
        // D-027: o InfoPC não instala firmware (não há como desfazer); a página mostra o comando
        Assert.Null(typeof(DriverInfo).GetMethod("FirmwareUpdateAll"));
        Assert.Equal("sudo fwupdmgr update", DriverInfo.FirmwareUpdateCommand);
    }

    [Fact]
    public void Brltty_removal_can_be_reinstalled_with_what_left_with_it()
    {
        using var t = new TempRoot();
        var pkgs = new List<string> { "bash", "brltty", "brltty-x11" };
        Dpkg(t, () => pkgs.ToArray());
        var calls = new List<string[]>();
        Elevate.Override = (f, a) =>
        {
            calls.Add(a.Prepend(f).ToArray());
            if (f == "apt-get" && a[0] == "remove") { pkgs.Remove("brltty"); pkgs.Remove("brltty-x11"); }
            return new ProcResult(0, "", "");
        };
        var s = new AppSettings();
        var (r, _) = Maker.RemoveBrltty(s, () => { }, "apt");
        Assert.True(r.Ok);
        Assert.Equal(new[] { "apt-get", "remove", "-y", "brltty" }, calls.Single());
        var c = Assert.Single(s.PackageChanges);
        Assert.Equal(new[] { "brltty:amd64 1.0", "brltty-x11:amd64 1.0" }, Ids(c.Removed));
        Assert.True(Packages.Undo(c, s, () => { }).Ok);
        Assert.Equal(new[] { "apt-get", "install", "-y", "brltty:amd64=1.0", "brltty-x11:amd64=1.0" }, calls.Last());
    }

    [Theory]
    [InlineData("--purge", "amd64", "1.0")]
    [InlineData("brltty", "--force", "1.0")]
    [InlineData("brltty", "amd64", "-1.0")]
    [InlineData("brltty", "amd64", "1.0 --force-yes")]
    [InlineData("brltty", "amd64", "1.0=2")]
    public void Undo_refuses_package_fields_that_would_be_read_as_options(string name, string arch, string version)
    {
        using var t = new TempRoot();
        Elevate.Override = (_, _) => throw new InvalidOperationException("não deveria pedir senha");
        var s = new AppSettings();
        var c = new PackageChange { Kind = "gpu-driver", Manager = "apt", Added = { new(name, arch, version) } };
        s.PackageChanges.Add(c);
        Assert.False(Packages.Undo(c, s, () => { }).Ok);
        Assert.Single(s.PackageChanges);
    }

    [Fact]
    public void Rpm_list_keeps_architecture_and_version_and_ignores_gpg_keys()
    {
        using var t = new TempRoot();
        Shell.Override = (f, _) => f == "rpm"
            ? new ProcResult(0, "brltty\tx86_64\t0:6.5-1.fc40\nglibc\ti686\t0:2.39-1\nglibc\tx86_64\t0:2.39-1\nvim\tx86_64\t2:9.1-1\ngpg-pubkey\t(none)\t0:abc-1\n", "")
            : ProcResult.Missing(f);
        var ids = Packages.Installed("dnf")!;
        Assert.Equal(4, ids.Count);
        Assert.Contains(new PackageId("glibc", "i686", "2.39-1"), ids);
        Assert.Contains(new PackageId("vim", "x86_64", "2:9.1-1"), ids);          // a época que não é 0 faz parte da versão
    }

    [Fact]
    public void Pacman_list_has_name_and_version()
    {
        using var t = new TempRoot();
        Shell.Override = (f, a) => f == "pacman" && a[0] == "-Q" ? new ProcResult(0, "brltty 6.5-1\nbash 5.2-2\n", "") : ProcResult.Missing(f);
        Assert.Contains(new PackageId("brltty", "", "6.5-1"), Packages.Installed("pacman")!);
    }

    [Fact]
    public void An_empty_or_failed_list_is_not_taken_as_no_packages()
    {
        using var t = new TempRoot();
        Shell.Override = (f, _) => new ProcResult(0, "\n", "");
        Assert.Null(Packages.Installed("apt"));
        Assert.Null(Packages.Installed("dnf"));
        Assert.Null(Packages.Installed("pacman"));
        Assert.Null(Packages.Installed("apk"));
        Assert.Null(Packages.Installed(null));
    }

    [Fact]
    public void The_real_package_list_of_this_distribution_is_read_and_passes_the_undo_validation()
    {
        // Só no Linux, e só lê. Confere o formato real da distribuição onde roda: o job de pacotes do CI cobre apt, dnf, pacman e zypper
        if (!OperatingSystem.IsLinux()) return;
        using var t = new TempRoot();
        Shell.Override = null;
        var manager = OsInfo.PackageManager();
        if (manager is not ("apt" or "dnf" or "pacman" or "zypper")) return;
        var ids = Packages.Installed(manager);
        Assert.NotNull(ids);
        Assert.NotEmpty(ids);
        Assert.All(ids, p => Assert.True(Packages.IsSafe(p), p.ToString()));
        Assert.All(ids, p => Assert.Equal(manager == "pacman", p.Arch.Length == 0));
    }

    [Fact]
    public void Other_package_managers_remove_then_install()
    {
        using var t = new TempRoot();
        var calls = new List<string>();
        Elevate.Override = (f, a) => { calls.Add(f + " " + string.Join(' ', a)); return new ProcResult(0, "", ""); };
        var s = new AppSettings();
        var c = new PackageChange { Manager = "dnf", Added = { new("akmod-nvidia", "x86_64", "550-1") }, Removed = { new("xorg-x11-drv-nouveau", "x86_64", "1:1.0-1") } };
        s.PackageChanges.Add(c);
        Assert.True(Packages.Undo(c, s, () => { }).Ok);
        Assert.Equal(new[] { "dnf remove -y akmod-nvidia-550-1.x86_64", "dnf install -y xorg-x11-drv-nouveau-1:1.0-1.x86_64" }, calls.ToArray());
        Assert.Equal("sudo dnf remove akmod-nvidia-550-1.x86_64 && sudo dnf install xorg-x11-drv-nouveau-1:1.0-1.x86_64", Packages.ManualCommand(c));

        calls.Clear();
        var z = new PackageChange { Manager = "zypper", Removed = { new("brltty", "x86_64", "6.5-1") } };
        s.PackageChanges.Add(z);
        Assert.True(Packages.Undo(z, s, () => { }).Ok);
        Assert.Equal("zypper --non-interactive install brltty.x86_64=6.5-1", calls.Single());

        calls.Clear();
        var p = new PackageChange { Manager = "pacman", Removed = { new("brltty", "", "6.5-1") } };
        s.PackageChanges.Add(p);
        Assert.True(Packages.Undo(p, s, () => { }).Ok);
        Assert.Equal("pacman -S --noconfirm brltty", calls.Single());             // o pacman só instala a versão atual do repositório
    }

    [Fact]
    public void Pacman_refuses_to_undo_a_version_change_it_cannot_reverse()
    {
        using var t = new TempRoot();
        Elevate.Override = (_, _) => throw new InvalidOperationException("não deveria pedir senha");
        var s = new AppSettings();
        var c = new PackageChange { Manager = "pacman", Added = { new("mesa", "", "2-1") }, Removed = { new("mesa", "", "1-1") } };
        s.PackageChanges.Add(c);
        Assert.False(Packages.Undo(c, s, () => { }).Ok);
        Assert.Single(s.PackageChanges);
    }
}

public class DeviceWatcherTests
{
    [Fact]
    public void First_poll_is_silent_then_plug_and_unplug_are_reported()
    {
        using var t = new TempRoot();
        t.Write("/sys/bus/usb/devices/usb1/idVendor", "1d6b");
        t.Write("/sys/bus/usb/devices/1-1/idVendor", "05e3");           // hub: ignorado
        t.Write("/sys/bus/usb/devices/1-1/bDeviceClass", "09");
        t.Write("/sys/bus/usb/devices/1-4/idVendor", "046d");
        t.Write("/sys/bus/usb/devices/1-4/idProduct", "c52b");
        t.Write("/sys/bus/usb/devices/1-4/product", "Receptor do mouse");
        var w = new DeviceWatcher();
        Assert.Empty(w.Poll());                                        // o que já estava plugado não avisa

        t.Write("/sys/bus/usb/devices/1-2/idVendor", "1a86");
        t.Write("/sys/bus/usb/devices/1-2/idProduct", "7523");
        t.Write("/sys/bus/usb/devices/1-2/product", "USB Serial");
        t.Write("/sys/bus/usb/devices/1-2/manufacturer", "wch.cn");
        t.Write("/sys/block/sdb/removable", "1");
        t.Write("/sys/block/sdb/size", "62914560");
        t.Write("/sys/block/sdb/device/vendor", "Kingston");
        t.Write("/sys/block/sdb/device/model", "DataTraveler 3.0");
        var added = w.Poll();
        Assert.Equal(2, added.Count);
        var maker = added.Single(c => c.Kind == DeviceChangeKind.Maker);
        Assert.True(maker.Added);
        Assert.Equal("USB Serial", maker.Title);
        Assert.Contains("VID 1A86", maker.Detail);
        Assert.Equal("Conectado · placa Maker", maker.Headline);
        var disk = added.Single(c => c.Kind == DeviceChangeKind.Storage);
        Assert.Equal("Kingston DataTraveler 3.0", disk.Title);
        Assert.Contains("32,2 GB", disk.Detail);
        Assert.Empty(w.Poll());                                        // nada mudou: nada de aviso repetido

        Directory.Delete(Path.Combine(t.Dir, "sys/bus/usb/devices/1-2"), true);
        var removed = Assert.Single(w.Poll());
        Assert.False(removed.Added);
        Assert.Equal("Removido · placa Maker", removed.Headline);
    }

    [Fact]
    public void Internal_disks_are_not_reported_as_removable()
    {
        using var t = new TempRoot();
        t.Write("/sys/block/sda/removable", "0");
        t.Write("/sys/block/sda/size", "1000000");
        t.Write("/sys/block/nvme0n1/removable", "0");
        Assert.Empty(DeviceWatcher.Snapshot());
    }
}
