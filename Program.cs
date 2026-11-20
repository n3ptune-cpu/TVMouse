using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using TvRemote;

const int Port = 8765;
const string TargetProcess = "stremio";   // window that key commands are sent to

// Bind only to the Tailscale address, so nothing on your LAN can reach it.
var ip = WaitForTailscaleIp(TimeSpan.FromSeconds(60));
if (ip is null)
{
    Console.Error.WriteLine("Tailscale IP not found. Make sure Tailscale is running and connected.");
    return 1;
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://{ip}:{Port}");
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var app = builder.Build();

string html;
using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("index.html")!)
using (var r = new StreamReader(s))
    html = r.ReadToEnd();

// name -> action. Add new buttons here and in index.html.
var commands = new Dictionary<string, Action>
{
    ["NEXT"]      = () => { Native.Focus(TargetProcess); Native.Tap(0x4E, shift: true); },                  // Shift+N
    ["SEEK_BACK"] = () => { Native.Focus(TargetProcess); Native.Tap(0x25, extended: true, shift: true); },  // Shift+Left
    ["SEEK_FWD"]  = () => { Native.Focus(TargetProcess); Native.Tap(0x27, extended: true, shift: true); },  // Shift+Right
    ["SPACE"]     = () => { Native.Focus(TargetProcess); Native.Tap(0x20); },
    ["BACK"]      = () => { Native.Focus(TargetProcess); Native.Tap(0x08); },
    // Global media keys, no focus needed
    ["VOL_DOWN"]  = () => Native.Tap(0xAE, extended: true),
    ["VOL_UP"]    = () => Native.Tap(0xAF, extended: true),
    ["MUTE"]      = () => Native.Tap(0xAD, extended: true),
};

app.MapGet("/", () => Results.Content(html, "text/html; charset=utf-8"));

// State-changing endpoints are POST, so a random web page can't trigger them with an <img> tag.
app.MapPost("/move",  (int dx, int dy) => { Native.MoveBy(dx, dy); return Results.Ok(); });
app.MapPost("/click", () => { Native.Focus(TargetProcess); Native.LeftClick();  return Results.Ok(); });
app.MapPost("/rclick",() => { Native.Focus(TargetProcess); Native.RightClick(); return Results.Ok(); });

app.MapPost("/cmd", (string k) =>
{
    if (!commands.TryGetValue(k, out var action)) return Results.BadRequest();
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] CMD: {k}");
    action();
    return Results.Ok();
});

app.MapPost("/type", (string q) =>
{
    if (string.IsNullOrWhiteSpace(q)) return Results.BadRequest();
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] TYPE: {q}");
    Native.Focus(TargetProcess);
    Native.TypeText(q);
    Native.Tap(Native.VK_RETURN);
    return Results.Ok();
});

Console.WriteLine($"TV Remote -> http://{ip}:{Port}");
app.Run();
return 0;

// ---------------------------------------------------------------------------
static string? WaitForTailscaleIp(TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    do
    {
        var ip = FindTailscaleIp();
        if (ip is not null) return ip;
        Thread.Sleep(2000);   // Tailscale may still be starting at logon
    } while (DateTime.UtcNow < deadline);
    return null;
}

static string? FindTailscaleIp()
{
    foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
    {
        if (nic.OperationalStatus != OperationalStatus.Up) continue;
        foreach (var ua in nic.GetIPProperties().UnicastAddresses)
        {
            if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
            var b = ua.Address.GetAddressBytes();
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return ua.Address.ToString();   // 100.64.0.0/10
        }
    }
    return null;
}
