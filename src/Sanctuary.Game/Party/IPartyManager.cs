using Sanctuary.Game.Entities;

namespace Sanctuary.Game.Party;

public interface IPartyManager
{
    Party? GetParty(Player player);

    Party? Invite(Player inviter, Player target);

    Party? Accept(Player target);

    void Decline(Player target);

    void Leave(Player player);

    void Kick(Player leader, ulong memberGuid);

    Party? RemoveMember(Player player);

    void DisbandParty(Party party);
}
