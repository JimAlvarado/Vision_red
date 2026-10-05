using System.Text;

// Shared access to eventos-monitor-*.jsonl: the monitor appends while the CSV journal reads.
public static class JournalFile
{
    private const FileShare Shared = FileShare.ReadWrite | FileShare.Delete;

    public static async Task AppendAsync(string path, string line, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(line);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, Shared);
                await stream.WriteAsync(bytes, token);
                return;
            }
            catch (IOException) when (attempt < 5) { await Task.Delay(50 * attempt, token); }
        }
    }

    public static IEnumerable<string> ReadLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, Shared);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line) yield return line;
    }
}
