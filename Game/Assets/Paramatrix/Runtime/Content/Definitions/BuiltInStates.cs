namespace Paramatrix.Content
{
    /// <summary>
    /// State properties the loader generates from required fields (T14).
    /// Character <c>location</c>: a location ID or <see cref="None"/>.
    /// Item <c>holder</c>: a location ID (lies there in the world), <see cref="Inventory"/>, <see cref="Consumed"/> or a character ID.
    /// </summary>
    public static class BuiltInStates
    {
        public const string Location = "location";
        public const string Holder = "holder";

        public const string None = "none";
        public const string Inventory = "inventory";
        public const string Consumed = "consumed";
    }
}
