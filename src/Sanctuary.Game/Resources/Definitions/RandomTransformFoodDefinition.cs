namespace Sanctuary.Game.Resources.Definitions;

// A consumable that applies a random transformation from TransformAbilityIds
// when used (e.g. the Jack-O-Lantern rolls one of the boss transformations).
public class RandomTransformFoodDefinition
{
    public int ItemId { get; set; }
    public int[] TransformAbilityIds { get; set; } = [];
}
