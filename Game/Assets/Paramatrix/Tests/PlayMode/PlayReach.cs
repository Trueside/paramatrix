using Paramatrix.Content;
using Paramatrix.Unity;
using EntityId = Paramatrix.Content.EntityId;

namespace Paramatrix.PlayTests
{
    /// <summary>
    /// The player walks the hero himself (Р11): a left click counts only within the hero's reach. Tests that are not
    /// about walking put the hero next to the target first.
    /// </summary>
    internal static class PlayReach
    {
        /// <summary>The hero stands where a scene would walk someone to the view (its approach point): within reach.</summary>
        public static void StandBy(this GameBootstrap game, EntityView view) => game.Hero.Walker.PlaceAt(view.ApproachX);

        public static void StandBy(this GameBootstrap game, string id) => game.StandBy(game.Representations.Find(EntityId.Parse(id)));
    }
}
