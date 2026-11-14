using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Paramatrix.Unity;
using UnityEngine;
using UnityEngine.TestTools;

namespace Paramatrix.PlayTests
{
    /// <summary>Walking on the pixel grid: whole-pixel steps, even at cruising speed, with speed-up and slow-down.</summary>
    public sealed class WalkerPlayTests
    {
        private const float Tick = 1f / Walker.TicksPerSecond;
        private GameObject _body;
        private Walker _walker;

        [SetUp]
        public void Create()
        {
            _body = new GameObject("Walker");
            _walker = _body.AddComponent<Walker>();
            _walker.PlaceAt(0f);
        }

        [TearDown]
        public void Destroy() => Object.Destroy(_body);

        private static float Pixels(float units) => units * PixelGrid.PixelsPerUnit;

        [Test]
        public void FreeWalkSpeedsUpThenStepsEvenly()
        {
            var positions = new List<float> { Pixels(_walker.X) };
            for (var i = 0; i < Walker.StartTicks + 20; i++)
            {
                _walker.Step(1f, -100f, 100f, Tick);
                positions.Add(Pixels(_walker.X));
            }

            var steps = positions.Zip(positions.Skip(1), (a, b) => b - a).ToList();
            Assert.That(positions.All(p => Mathf.Approximately(p, Mathf.Round(p))), Is.True, "always on a whole grid pixel");
            Assert.That(steps.Take(Walker.StartTicks / 2).Sum(), Is.LessThan(Walker.StartTicks / 2 * Walker.CruisePixelsPerTick / 2f),
                "the first half of the start covers less than half the cruising distance: a gentle start");
            Assert.That(steps.Skip(Walker.StartTicks + 2), Is.All.EqualTo(Walker.CruisePixelsPerTick), "every cruising step is the same size");
        }

        [Test]
        public void FreeWalkStopsAtTheFloorEdge()
        {
            for (var i = 0; i < 600; i++)
            {
                _walker.Step(-1f, -2f, 2f, Tick);
            }

            Assert.That(_walker.X, Is.EqualTo(-2f));
        }

        [UnityTest]
        public IEnumerator TargetWalkSlowsDownAndArrivesExactly()
        {
            var target = PixelGrid.Units(301f);
            var arrived = false;
            var positions = new List<float>();
            _walker.WalkTo(target, () => arrived = true);
            for (var frame = 0; frame < 600 && !arrived; frame++)
            {
                yield return null;
                positions.Add(Pixels(_walker.X));
            }

            Assert.That(arrived, Is.True);
            Assert.That(_walker.X, Is.EqualTo(target));
            var steps = positions.Zip(positions.Skip(1), (a, b) => b - a).Where(s => s > 0f).ToList();
            Assert.That(steps.Max(), Is.LessThanOrEqualTo(Walker.CruisePixelsPerTick * 3), "no jumps");
            Assert.That(steps.Last(), Is.LessThan(Walker.CruisePixelsPerTick), "slows down before the target");
        }
    }
}
