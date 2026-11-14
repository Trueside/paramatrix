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

        [SerializeField]
        [Tooltip("Left edge of what the location draws, world X. The camera never shows beyond it.")]
        private float viewMinX = -10f;

        [SerializeField]
        [Tooltip("Right edge of what the location draws, world X. One screen wide (20 units) = a fixed screen; wider = scrolling.")]
        private float viewMaxX = 10f;

        [SerializeField]
        [Tooltip("How many grid pixels of the picture lie below the floor line (where feet stand), counted from the bottom of the screen.")]
        private int floorDepth = 72;

        [SerializeField]
        [Tooltip("Conversation line (Р10): grid pixels from the bottom of the screen to the bottom of avatars and bubbles. " +
                 "Above the heads of standing figures; avatar and bubble must fit above it. 0 = not set (an error).")]
        private int dialogueLine;

        public string LocationId => locationId;

        /// <summary>Grid pixels from the bottom of the screen to where avatars and conversation bubbles stand; 0 = not set.</summary>
        public int DialogueLine => dialogueLine;

        /// <summary>Grid pixels from the bottom of the screen up to the floor line; the camera stands so that world y = 0 is there.</summary>
        public int FloorDepth => floorDepth;

        public float FloorMinX => floorMinX;

        public float FloorMaxX => floorMaxX;

        public float ViewMinX => viewMinX;

        public float ViewMaxX => viewMaxX;

        /// <summary>World X the camera starts from: the middle of the view; parallax layers are placed for it.</summary>
        public float ViewCenterX => (viewMinX + viewMaxX) / 2f;
    }
}
