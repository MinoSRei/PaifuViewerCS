using PaifuViewer.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    // ドラッグ＆ドロップで送る牌譜 JSON が既定の 32KB を超えるため拡張する
    .AddHubOptions(o => o.MaximumReceiveMessageSize = 10 * 1024 * 1024);

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
