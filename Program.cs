using PaifuViewer;
using PaifuViewer.Components;

// ボットどうしの自動対戦で成績を測る（例：dotnet run -- --selfplay 4000）
if (args.Length > 0 && args[0] == "--selfplay")
{
    int rounds = args.Length > 1 ? int.Parse(args[1]) : 2000;
    // 3 つ目以降の引数で BotParams のしきい値を変えて試せる（例：PushHan1=2.5）
    var bp = new BotParams();
    foreach (var a in args.Skip(2))
    {
        var kv = a.Split('=');
        var f = typeof(BotParams).GetField(kv[0]) ?? throw new ArgumentException("不明なパラメータ: " + kv[0]);
        f.SetValue(bp, Convert.ChangeType(kv[1], f.FieldType, System.Globalization.CultureInfo.InvariantCulture));
    }
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    Console.Write(SelfPlay.Report(SelfPlay.Run(rounds, 1, bp)));
    return;
}

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
