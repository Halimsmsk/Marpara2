using Morpara.Extensions;
using Morpara.Notifications;
using Morpara.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Umbraco.Cms.Core.Notifications;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Configure ForwardedHeaders for reverse proxy support (nginx, load balancer etc.)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddDeliveryApi()
    .AddComposers() // Bu composer'ları otomatik olarak keşfeder ve yükler
    .Build();

// ⚡ NoOpDocumentUrlService REMOVED: URL'lerin düzgün gelmesi için gerçek DocumentUrlService kullanılıyor
// Daha önce SqlBulkCopy timeout sorununu çözmek için NoOp kullanılıyordu
// Şimdi Hangfire background jobs ile çözüldü, URL'ler için gerçek service gerekli
// builder.Services.AddSingleton<Umbraco.Cms.Core.Services.IDocumentUrlService, Morpara.Services.NoOpDocumentUrlService>();

// Register Morpara services
builder.Services.AddMorparaServices();

// HttpClient factory for sitemap refresh job
builder.Services.AddHttpClient();

WebApplication app = builder.Build();

await app.BootUmbracoAsync();

// Schedule sitemap cache refresh job (every 3 hours)
SitemapCacheRefreshJob.ScheduleRecurringJob();

// Enable ForwardedHeaders middleware - must be early in the pipeline
app.UseForwardedHeaders();

app.UseHttpsRedirection();

app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

await app.RunAsync();
