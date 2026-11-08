using Paramatrix.Content;
using Paramatrix.State;
using EntityId = Paramatrix.Content.EntityId;

namespace Paramatrix.Unity
{
    /// <summary>An interactive object: always present in its scene; State-dependent visuals are added by bindings later.</summary>
    public sealed class ObjectView : EntityView
    {
        public override EntityKind Kind => EntityKind.Object;

        internal override void Apply(IStateReader state, EntityId sceneLocation)
        {
            SetShown(true);
        }
    }
}
