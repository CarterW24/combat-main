using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Logging;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Game.Zones;

namespace Sanctuary.Game;

public class ZoneManager : IZoneManager
{
    private readonly ILogger _logger;
    private readonly IResourceManager _resourceManager;
    private readonly IServiceProvider _serviceProvider;

    private static int _uniqueId = 1;

    private readonly ConcurrentDictionary<int, IZone> _zones = new();

    private const int StartingZoneDefinitionId = 1;
    public StartingZone StartingZone { get; private set; } = null!;

    // INSTANCE (Frostfang Fury): lazily-created shared arena zone (per-party instancing later).
    private FrostfangArenaZone? _frostfangArena;
    private readonly object _frostfangArenaLock = new();

    public ZoneManager(
        ILoggerFactory loggerFactory,
        IResourceManager resourceManager,
        IServiceProvider serviceProvider)
    {
        _logger = loggerFactory.CreateLogger<ZoneManager>();

        _resourceManager = resourceManager;
        _serviceProvider = serviceProvider;
    }

    public bool Load()
    {
        if (!TryCreateStartingZone(StartingZoneDefinitionId, out var startingZone))
            return false;

        StartingZone = startingZone;

        return true;
    }

    public bool TryGetPlayer(ulong guid, [MaybeNullWhen(false)] out Player player)
    {
        player = default;

        foreach (var zone in _zones)
        {
            if (zone.Value.TryGetPlayer(guid, out player))
                return true;
        }

        return false;
    }

    public bool TryGetPlayer(string name, [MaybeNullWhen(false)] out Player player)
    {
        player = default;

        foreach (var zone in _zones)
        {
            foreach (var zonePlayer in zone.Value.Players)
            {
                if (zonePlayer.Name.FullName == name)
                {
                    player = zonePlayer;
                    return true;
                }
            }
        }

        return false;
    }

    public FrostfangArenaZone GetOrCreateFrostfangArena()
    {
        lock (_frostfangArenaLock)
        {
            if (_frostfangArena is null)
            {
                _frostfangArena = new FrostfangArenaZone(_serviceProvider)
                {
                    Id = _uniqueId++
                };

                _zones.TryAdd(_frostfangArena.Id, _frostfangArena);

                _logger.LogInformation("Created Frostfang arena zone {name} ({id}).", _frostfangArena.Name, _frostfangArena.Id);
            }

            return _frostfangArena;
        }
    }

    private bool TryCreateStartingZone(int definitionId, [MaybeNullWhen(false)] out StartingZone zone)
    {
        zone = default;

        if (!_resourceManager.Zones.TryGetValue(definitionId, out var zoneDefinition))
            return false;

        if (zoneDefinition is not StartingZoneDefinition startingZoneDefinition)
            return false;

        zone = new StartingZone(startingZoneDefinition, _serviceProvider)
        {
            Id = _uniqueId++
        };

        // zone.OnStart();

        return _zones.TryAdd(zone.Id, zone);
    }
}