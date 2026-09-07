using Microsoft.AspNetCore.Http.Features;
using PdfSharp.Fonts;
using PdfToolPlatform.Core.Fonts;
using PdfToolPlatform.Core.Services;
using PdfToolPlatform.Web.Services;

// PDFsharp核心套件不會自動使用系統安裝的字型,必須在任何XFont被建立之前
// 指定自訂的字型解析器,否則文字浮水印會拋出"No appropriate font found..."例外。
GlobalFontSettings.FontResolver = new WindowsFontResolver();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

// 合併功能需要「分次上傳、可刪除、可調順序」的工作區狀態,用Session追蹤目前暫存了哪些檔案。
// 單機小規模使用(1-5人),記憶體內的Session存放即可,不需要外部Redis等分散式快取。
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// 四個Core服務都是無狀態的,可以放心用Singleton共用
builder.Services.AddSingleton<PdfMergeService>();
builder.Services.AddSingleton<PdfSplitService>();
builder.Services.AddSingleton<WatermarkService>();
builder.Services.AddSingleton<PdfToImageService>();

builder.Services.AddHostedService<TempCleanupService>();

// PDF檔案可能較大(掃描件、多頁合併),預設Kestrel/表單大小限制偏保守,這裡放寬到500MB
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 500 * 1024 * 1024;
});
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 500 * 1024 * 1024;
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();
app.MapRazorPages();

app.Run();
