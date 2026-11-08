using UnityEngine;

namespace Paramatrix.Unity
{
    /// <summary>Named place where the hero appears when entering the scene. Exactly one per scene is the default (Р9).</summary>
    public sealed class EntryPoint : MonoBehaviour
    {
        [SerializeField]
        private string entryName = "default";

        [SerializeField]
        private bool isDefault;

        public string EntryName => entryName;

        public bool IsDefault => isDefault;
    }
}
