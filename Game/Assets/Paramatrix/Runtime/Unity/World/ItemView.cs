using Paramatrix.Content;
using Paramatrix.State;
using EntityId = Paramatrix.Content.EntityId;

namespace Paramatrix.Unity
{
    /// <summary>An item lying in the world: shown only while its <c>holder</c> is this scene's location (T14).</summary>
    public sealed class ItemView : EntityView
    {
        public override EntityKind Kind => EntityKind.Item;

        internal override void Apply(IStateReader state, EntityId sceneLocation)
        {
            SetShown(sceneLocation.IsValid && ReadEnum(state, Id, BuiltInStates.Holder) == sceneLocation.Value);
        }
    }
}
