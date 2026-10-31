using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace InfoPC.Linux;

/// <summary>
/// Janela "que ignora cliques" no X11: troca a região de entrada da janela por uma vazia (extensão XShape). No Wayland isso não existe
/// (o compositor não deixa um programa ficar acima dos outros nem deixar o mouse passar), então o overlay nativo só funciona no X11/XWayland.
/// </summary>
public static class X11
{
    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XFlush(IntPtr display);
    [DllImport("libXext.so.6")] private static extern void XShapeCombineRectangles(IntPtr display, IntPtr window, int destKind, int xOff, int yOff, IntPtr rectangles, int count, int op, int ordering);

    private const int ShapeInput = 2, ShapeSet = 0;

    /// <summary>Há um servidor X acessível (X11 puro ou XWayland). O Avalonia no Linux roda sobre ele.</summary>
    public static bool Available => OperatingSystem.IsLinux() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));

    /// <summary>Sessão Wayland: um jogo em tela cheia nativa Wayland fica acima de qualquer janela do X.</summary>
    public static bool IsWayland => string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase)
                                    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

    /// <summary>Faz a janela deixar o mouse passar. Devolve false se não deu (Wayland, biblioteca ausente...).</summary>
    public static bool MakeClickThrough(Window w)
    {
        try
        {
            var h = w.TryGetPlatformHandle();
            if (h == null || h.HandleDescriptor != "XID") return false;
            IntPtr dpy = XOpenDisplay(IntPtr.Zero);
            if (dpy == IntPtr.Zero) return false;
            try
            {
                XShapeCombineRectangles(dpy, h.Handle, ShapeInput, 0, 0, IntPtr.Zero, 0, ShapeSet, 0); // zero retângulos = nenhuma área recebe entrada
                XFlush(dpy);
                return true;
            }
            finally { XCloseDisplay(dpy); }
        }
        catch { return false; }
    }
}
