using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;

namespace Sanctuary.Game.ChatCommands;

public class HpChatCommand : IChatCommand
{
    public string KeyWord => "hp";
    public string Usage => "<health>";
    public string Description => "Sets your current health, for combat testing.";
    public ChatCommandRole RequiredRole => ChatCommandRole.Admin;

    public bool Handle(Player invoker, string[] args)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out var health))
            return false;

        invoker.SetHealth(health);

        ChatHelper.SendSystemMessage(invoker, $"Health set to {invoker.Health}/{invoker.MaxHealth}.");
        return true;
    }
}
