using UnityEngine;

namespace Paramatrix.Unity
{
    /// <summary>Marks a location scene: which location it represents and where the floor line ends.</summary>
    public sealed class LocationRoot : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Entity ID of the location, e.g. location.office.")]
        private string locationId;

        [SerializeField]
        private float floorMinX = -8f;

        [SerializeField]
        private float floorMaxX = 8f;

        public string LocationId => locationId;

        public float FloorMinX => floorMinX;

        public float FloorMaxX => floorMaxX;
    }
}
