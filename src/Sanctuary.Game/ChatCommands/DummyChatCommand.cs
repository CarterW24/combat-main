using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;

namespace Sanctuary.Game.ChatCommands;

public class DummyChatCommand : IChatCommand
{
    private const int DummyModelId = 4;
    private const int DummyMaxHealth = 50000;
    private const int MaxDummies = 5;
    private const float SpawnDistance = 5f;
    private const float SpawnSpacing = 3f;

    private readonly object _lock = new();
    private readonly List<Npc> _dummies = [];

    public string KeyWord => "dummy";
    public string Usage => "[count]";
    public string Description => "Spawns (or moves) training dummies in front of you, for combat testing.";
    public ChatCommandRole RequiredRole => ChatCommandRole.Admin;

    public bool Handle(Player invoker, string[] args)
    {
        var count = args.Length > 0 && int.TryParse(args[0], out var requested) ? Math.Clamp(requested, 1, MaxDummies) : 1;

        var forward = invoker.Forward;
        var right = new Vector3(forward.Z, 0f, -forward.X);
        var placed = new List<Npc>();

        lock (_lock)
        {
            _dummies.RemoveAll(dummy => dummy.Zone != invoker.Zone);

            for (var i = 0; i < count; i++)
            {
                if (i >= _dummies.Count)
                {
                    if (!invoker.Zone.TryCreateNpc(null, out var npc))
                    {
                        ChatHelper.SendSystemMessage(invoker, "Failed to spawn the training dummy.");
                        return true;
                    }

                    npc.ModelId = DummyModelId;
                    npc.Name = "Training Dummy";
                    npc.Disposition = 0;
                    npc.Scale = 1f;
                    npc.IsInteractable = false;
                    npc.CursorId = 11;

                    npc.MaxHealth = DummyMaxHealth;
                    npc.Health = DummyMaxHealth;
                    npc.ShowHealthBar = true;
                    npc.RestoreOnDeath = true;

                    _dummies.Add(npc);
                }

                var lateral = (i - (count - 1) / 2f) * SpawnSpacing;
                var position = new Vector4(
                    invoker.Position.X + forward.X * SpawnDistance + right.X * lateral,
                    invoker.Position.Y,
                    invoker.Position.Z + forward.Z * SpawnDistance + right.Z * lateral,
                    1f);

                var dummy = _dummies[i];
                dummy.Health = dummy.MaxHealth;
                dummy.UpdatePosition(position, invoker.Rotation);
                dummy.Visible = true;
                placed.Add(dummy);
            }
        }

        var zone = invoker.Zone;
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            foreach (var player in zone.Players)
                foreach (var dummy in placed)
                    zone.SendNpcHealth(player, dummy);
        });

        ChatHelper.SendSystemMessage(invoker, count == 1 ? "Training dummy ready." : $"{count} training dummies ready.");
        return true;
    }
}
