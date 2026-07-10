using System.Xml.Linq;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Sanctuary.WebAPI.Endpoints;

/// <summary>
/// Serves the launcher's server manifest at GET /servermanifest.xml (the OSFRLauncher fetches
/// &lt;serverUrl&gt;/servermanifest.xml). Values come from the optional "ServerManifest" config section
/// (appsettings/gateway config) so the name/description/addresses can be edited without recompiling;
/// sensible defaults are used when the section is absent.
/// </summary>
public static class ManifestEndpoints
{
    public static void MapManifestEndpoints(this WebApplication app)
    {
        app.MapGet("/servermanifest.xml", (IConfiguration config) =>
        {
            var section = config.GetSection("ServerManifest");

            var name = section["Name"] ?? "Sul Server";
            var description = section["Description"]
                ?? "Sul's test server for special people - now with a full quest system (objectives, live tracker & \"Take Me There\" breadcrumb), collect-and-return quests, job leveling with XP, stars & full-screen level-up celebrations, working health/mana, and synced boombox dances.";
            var webApiUrl = section["WebApiUrl"] ?? "http://35.232.22.63:5055";
            var loginServer = section["LoginServer"] ?? "35.232.22.63:20042";

            // XElement handles XML escaping (e.g. & -> &amp;) automatically.
            var manifest = new XElement("ServerManifest",
                new XAttribute("version", 2),
                new XElement("Name", name),
                new XElement("Description", description),
                new XElement("WebApiUrl", webApiUrl),
                new XElement("LoginServer", loginServer));

            return Results.Content(manifest.ToString(), "application/xml");
        });
    }
}
