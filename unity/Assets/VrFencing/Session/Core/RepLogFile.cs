namespace VrFencing.Session
{
    /// <summary>
    /// The rep log: one JSON line per judged Rep (<see cref="RepLogEntry"/>), appended as the Drill runs.
    /// The caller supplies the path, so the Unity layer can pass a file under persistentDataPath and tests a temp folder.
    /// </summary>
    public static class RepLogFile
    {
        /// <summary>Not <c>reps.jsonl</c>: a throwaway prototype on the headset already writes that name with another schema.</summary>
        public const string FileName = "session-reps.jsonl";

        public static void Append(string path, RepLogEntry entry)
        {
            JsonLinesFile.Append(path, entry);
        }

        public static JsonLinesReadResult<RepLogEntry> Read(string path)
        {
            return JsonLinesFile.Read<RepLogEntry>(path);
        }
    }
}
