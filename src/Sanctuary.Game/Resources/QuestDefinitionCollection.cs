using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Sanctuary.Game.Resources.Definitions;

namespace Sanctuary.Game.Resources;

/// <summary>
/// Loads quest definitions from Resources/Quests.json and builds the lookups the quest manager needs:
/// by quest id, by giver NPC guid, and by target NPC guid.
/// </summary>
public class QuestDefinitionCollection
{
    private readonly ILogger _logger;

    /// <summary>questId -&gt; definition.</summary>
    public ConcurrentDictionary<int, QuestDefinition> Quests { get; } = new();

    /// <summary>giver NPC guid -&gt; quest ids that NPC offers.</summary>
    public ConcurrentDictionary<ulong, List<int>> ByGiver { get; } = new();

    /// <summary>target NPC guid -&gt; quest ids that use the NPC as a talk-to / turn-in target.</summary>
    public ConcurrentDictionary<ulong, List<int>> ByTarget { get; } = new();

    public QuestDefinitionCollection(ILogger logger)
    {
        _logger = logger;
    }

    public bool TryGet(int questId, out QuestDefinition definition) => Quests.TryGetValue(questId, out definition!);

    public bool Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Quest file not found: \"{file}\". No quests will be loaded.", filePath);
            return true;
        }

        try
        {
            using var fileStream = File.OpenRead(filePath);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            var quests = JsonSerializer.Deserialize<List<QuestDefinition>>(fileStream, options);

            if (quests is null)
            {
                _logger.LogError("No entries found in file \"{file}\".", filePath);
                return false;
            }

            foreach (var quest in quests)
            {
                if (!Quests.TryAdd(quest.QuestId, quest))
                {
                    _logger.LogWarning("Duplicate quest id {id} in \"{file}\".", quest.QuestId, filePath);
                    continue;
                }

                if (quest.GiverGuid != 0)
                    ByGiver.GetOrAdd(quest.GiverGuid, _ => new List<int>()).Add(quest.QuestId);

                if (quest.TargetGuid != 0)
                    ByTarget.GetOrAdd(quest.TargetGuid, _ => new List<int>()).Add(quest.QuestId);

                // Index every goal's target NPC too, so multi-goal quests can point intermediate goals at
                // NPCs that aren't the giver/turn-in - otherwise those NPCs wouldn't get a quest interaction
                // (IsQuestNpc gates the interact action at spawn on ByGiver/ByTarget).
                var goalNameIds = new HashSet<int>();
                foreach (var goal in quest.EffectiveGoals)
                {
                    if (goal.TargetGuid != 0 && goal.TargetGuid != quest.TargetGuid
                        && !ByTarget.GetOrAdd(goal.TargetGuid, _ => new List<int>()).Contains(quest.QuestId))
                        ByTarget[goal.TargetGuid].Add(quest.QuestId);

                    // Goal NameIds double as the client's objective identity (QuestObjectiveAdded body
                    // int0 -> row hash key) - a duplicate makes goals indistinguishable client-side.
                    if (!goalNameIds.Add(goal.NameId))
                        _logger.LogWarning("Quest {id}: duplicate goal NameId {nameId} - goals will collide client-side (checkmarks/advance won't render correctly).", quest.QuestId, goal.NameId);
                }
            }

            _logger.LogInformation("Loaded {count} quest definitions from \"{file}\".", Quests.Count, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse file \"{file}\".", filePath);
            return false;
        }

        return true;
    }
}
