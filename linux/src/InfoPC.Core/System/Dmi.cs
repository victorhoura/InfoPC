namespace InfoPC;

public sealed record DmiInfo(
    string SysVendor, string ProductName, string ProductVersion, string BoardVendor, string BoardName, string BoardVersion,
    string BiosVendor, string BiosVersion, string BiosDate, string ChassisType, bool IsLaptop)
{
    public bool HasData => SysVendor.Length + ProductName.Length + BoardName.Length + BiosVersion.Length > 0;
}

/// <summary>Placa-mãe, BIOS e tipo de gabinete (DMI/SMBIOS) pelo sysfs. Números de série ficam de fora (só root lê, e o relatório os omite).</summary>
public static class Dmi
{
    public static DmiInfo Read()
    {
        string D(string f) => Sys.Read("/sys/class/dmi/id/" + f) ?? "";
        string ct = D("chassis_type");
        return new DmiInfo(D("sys_vendor"), D("product_name"), D("product_version"), D("board_vendor"), D("board_name"), D("board_version"),
                           D("bios_vendor"), D("bios_version"), D("bios_date"), ChassisName(ct), IsLaptopChassis(ct));
    }

    /// <summary>Códigos de chassi da especificação SMBIOS.</summary>
    public static string ChassisName(string code) => (int)(Num.L(code) ?? 0) switch
    {
        1 => "Outro", 3 => "Desktop", 4 => "Desktop slim", 5 => "Pizza box", 6 => "Mini torre", 7 => "Torre", 8 => "Portátil",
        9 => "Notebook", 10 => "Notebook", 11 => "Handheld", 12 => "Docking station", 13 => "All-in-one", 14 => "Sub-notebook",
        15 => "Compacto", 16 => "Lunch box", 17 => "Servidor", 23 => "Servidor rack", 24 => "Servidor blade",
        30 => "Tablet", 31 => "Conversível", 32 => "Detachable", 35 => "Mini PC", 36 => "Stick PC", _ => ""
    };

    public static bool IsLaptopChassis(string code) => (int)(Num.L(code) ?? 0) is 8 or 9 or 10 or 14 or 30 or 31 or 32;
}
