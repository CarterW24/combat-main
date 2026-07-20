namespace Sanctuary.Game.Combat;

// In-chat combat diagnostics ("!combatdbg" toggle). The harness gateway's NLog file logging is
// unreliable (shared-file quirk), so the decisive wire facts are echoed to the tester's chat
// window instead. Test tooling only — never ships in a PR.
public static class CombatDebug
{
    public static volatile bool Verbose;
}
