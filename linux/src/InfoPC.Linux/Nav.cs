namespace InfoPC.Linux;

/// <summary>Lista do menu. A ordem e os nomes são os do menu da edição Windows (D-007, D-015).</summary>
public static class Nav
{
    public static List<NavEntry> Entries() => new()
    {
        new("dash", "", "Visão geral", () => new DashboardPage()),

        new("system", "HARDWARE", "Sistema", () => new SystemPage()),
        new("sensors", "HARDWARE", "Sensores", () => new SensorsPage()),
        new("gpu", "HARDWARE", "Placa de vídeo", () => new GpuPage()),
        new("disks", "HARDWARE", "Discos", () => new DisksPage()),
        new("energy", "HARDWARE", "Energia", () => new EnergyPage()),
        new("net", "HARDWARE", "Rede", () => new NetPage()),

        new("launcher", "JOGOS", "Biblioteca", () => new LauncherPage()),
        new("games", "JOGOS", "FPS estimado", () => new GamesPage()),
        new("bottleneck", "JOGOS", "Gargalo", () => new BottleneckPage()),
        new("overlay", "JOGOS", "Overlay", () => new OverlayPage()),

        new("bench", "DESEMPENHO", "Benchmark", () => new BenchPage()),
        new("stress", "DESEMPENHO", "Stress test", () => new StressPage()),
        new("oc", "DESEMPENHO", "Overclock", () => new OcPage()),

        new("optimize", "FERRAMENTAS", "Otimização", () => new OptimizePage()),
        new("startup", "FERRAMENTAS", "Inicialização", () => new StartupPage()),
        new("apps", "FERRAMENTAS", "Instalar apps", () => new AppsPage()),
        new("drivers", "FERRAMENTAS", "Drivers", () => new DriversPage()),
        new("integ", "FERRAMENTAS", "Integração", () => new IntegPage()),

        new("serial", "MAKER", "Portas seriais", () => new SerialPortsPage()),
        new("serialdrv", "MAKER", "Drivers seriais", () => new SerialDriversPage()),
    };
}
