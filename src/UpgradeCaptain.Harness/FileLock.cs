namespace UpgradeCaptain.Harness;

/// <summary>
/// Cerrojo entre procesos con un fichero .lock (la consola de David y la de María comparten state/).
/// Las escrituras van a un temporal y se renombran: nunca se ve un JSON a medias.
/// </summary>
internal static class FileLock
{
    public static IDisposable Acquire(string path, TimeSpan? timeout = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        DateTime deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (true)
        {
            try
            {
                return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(25);
            }
        }
    }

    public static void WriteAllTextAtomic(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = $"{path}.{Environment.ProcessId}.tmp";
        File.WriteAllText(temp, contents);
        File.Move(temp, path, overwrite: true);
    }
}
