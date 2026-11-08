using System;
using System.IO;
using System.Text;
using Paramatrix.Gameplay;

namespace Paramatrix.Unity
{
    /// <summary>
    /// Save files in &lt;persistentDataPath&gt;/saves/&lt;slot&gt;.json (T21). Writes through a temporary file so that
    /// an interrupted write never leaves a half-written save in place.
    /// </summary>
    public sealed class FileSaveStorage : ISaveStorage
    {
        private readonly string _folder;

        public FileSaveStorage(string persistentDataPath)
        {
            _folder = Path.Combine(persistentDataPath, "saves");
        }

        public bool TryWrite(string slot, string json, out string error)
        {
            if (!IsValidSlot(slot, out error))
            {
                return false;
            }

            try
            {
                Directory.CreateDirectory(_folder);
                var target = PathOf(slot);
                var temporary = target + ".tmp";
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                if (File.Exists(target))
                {
                    File.Replace(temporary, target, null);
                }
                else
                {
                    File.Move(temporary, target);
                }

                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                error = exception.Message;
                return false;
            }
        }

        public bool TryRead(string slot, out string json, out string error)
        {
            json = null;
            if (!IsValidSlot(slot, out error))
            {
                return false;
            }

            var path = PathOf(slot);
            if (!File.Exists(path))
            {
                error = $"no save '{slot}'";
                return false;
            }

            try
            {
                json = File.ReadAllText(path, Encoding.UTF8);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                error = exception.Message;
                return false;
            }
        }

        private string PathOf(string slot) => Path.Combine(_folder, slot + ".json");

        private static bool IsValidSlot(string slot, out string error)
        {
            if (string.IsNullOrEmpty(slot) || slot.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || slot.Contains(".."))
            {
                error = $"invalid save slot name '{slot}'";
                return false;
            }

            error = null;
            return true;
        }
    }
}
