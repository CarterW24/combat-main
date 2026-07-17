namespace Sanctuary.Core.Configuration;

public sealed class DatabaseOptions
{
    public const string Section = "Database";

    public required DatabaseProvider Provider { get; set; }

    public string? VersionString { get; set; }
    public required string ConnectionString { get; set; }
}
