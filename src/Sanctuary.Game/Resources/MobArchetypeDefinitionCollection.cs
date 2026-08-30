using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Sanctuary.Core.Collections;
using Sanctuary.Game.Resources.Definitions.Combat;

namespace Sanctuary.Game.Resources;

public sealed class MobArchetypeDefinitionCollection : ObservableConcurrentDictionary<int, MobArchetypeDefinition>
{
    private readonly ILogger _logger;
    private readonly object _writeLock = new();

    public MobArchetypeDefinitionCollection(ILogger logger)
    {
        _logger = logger;
    }

    public bool Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogError("Failed to find file \"{file}\"", filePath);
            return false;
        }

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var entries = JsonSerializer.Deserialize<List<MobArchetypeDefinition>>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (entries is null || entries.Count == 0)
            {
                _logger.LogError("No mob archetype definitions found in \"{file}\".", filePath);
                return false;
            }

            var loaded = new Dictionary<int, MobArchetypeDefinition>();

            foreach (var entry in entries)
            {
                if (entry.Id <= 0 || entry.ModelId <= 0 || entry.MaxHealth <= 0)
                {
                    _logger.LogError("Invalid mob archetype definition {id} in \"{file}\".", entry.Id, filePath);
                    return false;
                }

                if (!loaded.TryAdd(entry.Id, entry))
                {
                    _logger.LogError("Duplicate mob archetype definition {id} in \"{file}\".", entry.Id, filePath);
                    return false;
                }
            }

            lock (_writeLock)
            {
                foreach (var entry in loaded)
                    this[entry.Key] = entry.Value;

                foreach (var key in Keys.Where(key => !loaded.ContainsKey(key)).ToArray())
                    Remove(key);
            }

            return true;
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Failed to parse file \"{file}\".", filePath);
            return false;
        }
    }
}
