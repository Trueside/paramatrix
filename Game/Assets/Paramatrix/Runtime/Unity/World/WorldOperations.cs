using Paramatrix.Content;
using Paramatrix.Scenario;
using UnityEngine;
using EntityId = Paramatrix.Content.EntityId;

namespace Paramatrix.Unity
{
    /// <summary>
    /// Scenario's <c>move</c> and <c>animation</c> actions executed on representations. Every started operation is
    /// completed exactly once; a missing target or clip fails at start (Р0).
    /// </summary>
    public sealed class WorldOperations : IMovementPort, IAnimationPort
    {
        private RepresentationRegistry _representations;
        private ScenarioRuntime _scenario;

        public void Bind(RepresentationRegistry representations, ScenarioRuntime scenario)
        {
            _representations = representations;
            _scenario = scenario;
        }

        public OperationStart MoveTo(OperationHandle handle, EntityId character, EntityId target)
        {
            var mover = _representations.Find(character) as CharacterView;
            if (mover == null || !mover.IsShown)
            {
                return OperationStart.Failed($"{character} is not shown in the current scene");
            }

            var destination = _representations.Find(target);
            if (destination == null || !destination.IsShown)
            {
                return OperationStart.Failed($"{target} is not shown in the current scene");
            }

            var root = _representations.SceneRoot;
            var x = root == null ? destination.ApproachX : Mathf.Clamp(destination.ApproachX, root.FloorMinX, root.FloorMaxX);
            mover.Walker.WalkTo(x, () => _scenario.CompleteOperation(handle, OperationOutcome.Completed));
            return OperationStart.Started;
        }

        public OperationStart Play(OperationHandle handle, EntityId target, string clip)
        {
            var view = _representations.Find(target);
            if (view == null || !view.IsShown)
            {
                return OperationStart.Failed($"{target} is not shown in the current scene");
            }

            var animations = view.GetComponent<EntityAnimations>();
            if (animations == null)
            {
                return OperationStart.Failed($"{target} has no EntityAnimations component");
            }

            return animations.TryPlay(clip, () => _scenario.CompleteOperation(handle, OperationOutcome.Completed), out var error)
                ? OperationStart.Started
                : OperationStart.Failed(error);
        }
    }
}
