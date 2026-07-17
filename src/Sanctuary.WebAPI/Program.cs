using System.Globalization;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using NLog.Extensions.Logging;

using Sanctuary.Core.Configuration;
using Sanctuary.Database;
using Sanctuary.WebAPI.Endpoints;
using Sanctuary.WebAPI.Options;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("database.json", optional: true);

builder.WebHost.UseUrls();

if (!builder.Environment.IsDevelopment())
    ((IConfigurationBuilder)builder.Configuration).AddJsonFile("database.json", optional: true);

var forwardedHeaderSection = builder.Configuration.GetSection("ForwardedHeadersOptions");

if (forwardedHeaderSection is not null)
    builder.Services.Configure<ForwardedHeadersOptions>(forwardedHeaderSection);

builder.Services.AddOptionsWithValidateOnStart<DatabaseOptions>()
    .BindConfiguration(DatabaseOptions.Section);

builder.Services.AddOptionsWithValidateOnStart<WebAPIOptions>()
    .BindConfiguration(WebAPIOptions.Section);

builder.Services.AddDatabase(builder.Configuration);

builder.Logging.ClearProviders();

#if DEBUG

builder.Logging.SetMinimumLevel(LogLevel.Debug);

builder.Services.AddHttpLogging(logging =>
{
    logging.LoggingFields = HttpLoggingFields.All;
});

#endif

var nlogConfigFile = builder.Environment.IsDevelopment()
    ? "NLog.Development.config"
    : "NLog.config";

builder.Logging.AddNLog(nlogConfigFile);

var app = builder.Build();

#if DEBUG

app.UseHttpLogging();

#endif

app.MapAuthEndpoints();
app.MapPortraitEndpoints();
app.MapManifestEndpoints();

app.MapGet("/servermanifest.xml", () => Microsoft.AspNetCore.Http.Results.Content(
    """
    <?xml version="1.0"?>
    <ServerManifest version="2">
      <Name>Local Dev</Name>
      <Description>Local Sanctuary combat dev server</Description>
      <WebApiUrl>http:
      <LoginServer>127.0.0.1:20042</LoginServer>
    </ServerManifest>
    """, "text/xml"));

app.Run();
