using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace InfoPC;

// =====================================================================================================================
// Corsair iCUE (usa CorsairSdk.cs)
// =====================================================================================================================
public sealed class CorsairBackend : ILightBackend
{
    private readonly CorsairSdk _sdk;
    public CorsairBackend(CorsairSdk sdk) => _sdk = sdk;

    public string Name => "Corsair iCUE";
    public int MinIntervalMs => 0;

    public List<LightDevice> Wrap(IEnumerable<CorsairDevice> devs) => devs.Select(d => new LightDevice
    {
        Backend = this, Id = d.Id,
        Name = string.IsNullOrWhiteSpace(d.Model) ? d.TypeName : d.Model,
        Detail = $"Corsair · {d.TypeName} · {d.LedCount} LEDs",
        LedX = d.LedX, Native = d,
    }).ToList();

    public int Acquire(LightDevice d, bool exclusive) => _sdk.RequestControl((CorsairDevice)d.Native!, exclusive ? 1 : 0);
    public int Send(LightDevice d, ReadOnlySpan<(byte R, byte G, byte B)> colors) => _sdk.SetColors((CorsairDevice)d.Native!, colors);
    public void Release(LightDevice d) { _sdk.ReleaseControl((CorsairDevice)d.Native!); }
    public void EndSession() { }
    public string ErrorText(int e) => CorsairSdk.ErrorText(e);
    public void Shutdown() { }
}

// =====================================================================================================================
// Razer Chroma (API REST local do Razer Synapse, porta 54235). Cor única por categoria de dispositivo.
// =====================================================================================================================
public sealed class RazerChromaBackend : ILightBackend
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private string? _uri;
    private long _lastBeat;
    private int _errors;

    private static readonly (string Key, string Name)[] Kinds =
    {
        ("keyboard", "Teclado"), ("mouse", "Mouse"), ("headset", "Headset"), ("mousepad", "Mousepad"),
        ("keypad", "Keypad"), ("chromalink", "Chroma Link (iluminação do case)"),
    };

    public string Name => "Razer Chroma";
    public int MinIntervalMs => 100;
    public int Errors => _errors;

    private async Task<string?> InitAsync()
    {
        var body = JsonSerializer.Serialize(new
        {
            title = "InfoPC",
            description = "Controle de iluminação do InfoPC",
            author = new { name = "InfoPC", contact = "InfoPC" },
            device_supported = Kinds.Select(k => k.Key).ToArray(),
            category = "application",
        });
        using var resp = await Http.PostAsync("http://localhost:54235/razer/chromasdk", new StringContent(body, Encoding.UTF8, "application/json"));
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("uri", out var u) ? u.GetString() : null;
    }

    public async Task<(List<LightDevice> Devices, string Status)> ConnectAsync()
    {
        var list = new List<LightDevice>();
        try
        {
            _uri = await InitAsync();
            if (string.IsNullOrEmpty(_uri)) return (list, "o Razer Synapse respondeu, mas não abriu a sessão do Chroma. Ative o Chroma Connect no Synapse.");
            _lastBeat = Environment.TickCount64;
            foreach (var (key, name) in Kinds)
                list.Add(new LightDevice { Backend = this, Id = key, Name = name, Detail = "Razer Chroma · cor única (se você tiver este tipo de dispositivo)", LedX = new[] { 0.0 } });
            return (list, "conectado ao Razer Synapse (Chroma). A lista mostra as categorias; as que você não tem simplesmente não reagem.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (list, "Razer Chroma não encontrado. Instale o Razer Synapse (com o módulo Chroma) e deixe-o aberto.");
        }
        catch (Exception ex) { return (list, "falha no Razer Chroma: " + ex.Message); }
    }

    public int Acquire(LightDevice d, bool exclusive)
    {
        if (_uri != null) return 0;
        try { _uri = Task.Run(InitAsync).GetAwaiter().GetResult(); } catch { _uri = null; }
        _lastBeat = Environment.TickCount64;
        return _uri != null ? 0 : 1;
    }

    public int Send(LightDevice d, ReadOnlySpan<(byte R, byte G, byte B)> colors)
    {
        if (_uri == null || colors.Length == 0) return 1;
        int color = colors[0].R | (colors[0].G << 8) | (colors[0].B << 16); // Chroma usa BGR
        string uri = _uri, kind = d.Id;
        _ = Task.Run(async () =>
        {
            try
            {
                var json = "{\"effect\":\"CHROMA_STATIC\",\"param\":{\"color\":" + color + "}}";
                using var r = await Http.PutAsync($"{uri}/{kind}", new StringContent(json, Encoding.UTF8, "application/json"));
            }
            catch { Interlocked.Increment(ref _errors); }
        });
        long now = Environment.TickCount64;
        if (now - _lastBeat > 8000)
        {
            _lastBeat = now;
            _ = Task.Run(async () => { try { using var r = await Http.PutAsync(uri + "/heartbeat", new StringContent("")); } catch { } });
        }
        return 0;
    }

    public void Release(LightDevice d) { }

    /// <summary>Fecha a sessão: o Synapse volta às luzes dele na hora.</summary>
    public void EndSession()
    {
        var uri = _uri;
        _uri = null;
        if (uri == null) return;
        try { Task.Run(async () => { using var r = await Http.DeleteAsync(uri); }).Wait(1500); } catch { }
    }

    public string ErrorText(int e) => e == 0 ? "ok" : "sessão do Razer Chroma indisponível";
    public void Shutdown() => EndSession();
}
