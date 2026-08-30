using Sanctuary.Game.Entities;
using Sanctuary.Packet;

namespace Sanctuary.Game;

public interface ICombatManager
{
    bool SendToolbar(Player player);

    int ResolveWieldType(Player player, int itemClassWieldType);

    bool TryExecuteAbility(Player player, AbilityPacketClientRequestStartAbility request);

    bool TrySendAbilityDefinition(Player player, int abilityDefinitionId);
}
