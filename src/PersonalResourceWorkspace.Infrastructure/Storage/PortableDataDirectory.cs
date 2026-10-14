using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PersonalResourceWorkspace.Application.Abstractions;

namespace PersonalResourceWorkspace.Infrastructure.Storage;

public sealed class PortableDataDirectory : IPortableDataDirectory
{
    public const string EnvironmentVariable = "PERSONAL_RESOURCE_WORKSPACE_DATA_DIR";
    private readonly string _baseDirectory;
    private readonly string _configurationPath;
    private readonly string _root;

    public string Source { get; }
    public bool IsOverridden => Source is "command line" or "environment";

    public PortableDataDirectory() : this(AppContext.BaseDirectory, Environment.GetCommandLineArgs().Skip(1).ToArray(), Environment.GetEnvironmentVariable(EnvironmentVariable)) { }

    public PortableDataDirectory(string baseDirectory, string[] arguments, string? environmentValue)
    {
        _baseDirectory = Path.GetFullPath(baseDirectory);
        _configurationPath = Path.Combine(_baseDirectory, "portable-config.json");
        string? selected = null;
        for (var index = 0; index < arguments.Length; index++)
        {
            if (arguments[index] != "--data-dir") continue;
            if (++index >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index]))
                throw new ArgumentException("--data-dir requires a directory path.");
            selected = arguments[index];
        }

        if (selected is not null) Source = "command line";
        else if (!string.IsNullOrWhiteSpace(environmentValue)) { selected = environmentValue; Source = "environment"; }
        else if (File.Exists(_configurationPath))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_configurationPath));
            if (!document.RootElement.TryGetProperty("dataDirectory", out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                throw new InvalidDataException("portable-config.json must contain a nonempty dataDirectory string.");
            selected = value.GetString();
            Source = "JSON";
        }
        else Source = "default";

        _root = Resolve(selected ?? "content");
        if (File.Exists(_root)) throw new IOException($"Data directory is a file: {_root}");
    }

    public string GetLocalDataRoot() => _root;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PersonalResourceWorkspace");
        if ((!Directory.Exists(_root) || !Directory.EnumerateFileSystemEntries(_root).Any()) &&
            Directory.Exists(legacy) && File.Exists(Path.Combine(legacy, "Data", "workspace.db")) &&
            !string.Equals(_root, legacy, StringComparison.OrdinalIgnoreCase))
            await CopyWorkspaceAsync(legacy, _root, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(_root);
        await CheckWritableAsync(_root, cancellationToken).ConfigureAwait(false);
    }

    public async Task RelocateAsync(string destination, CancellationToken cancellationToken)
    {
        if (IsOverridden) throw new InvalidOperationException("The command line or environment overrides the saved data directory.");
        var target = Resolve(destination);
        if (string.Equals(target, _root, StringComparison.OrdinalIgnoreCase)) return;
        if (target.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            _root.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The destination must not contain the current data directory or be inside it.");
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            throw new IOException("The destination already contains files. Choose an empty directory.");
        if (Directory.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The destination cannot be a symbolic link.");

        ValidateNoLinks(_root);
        var sourceBytes = Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);
        var targetDrive = new DriveInfo(Path.GetPathRoot(target)!);
        if (targetDrive.AvailableFreeSpace < sourceBytes)
            throw new IOException("The destination does not have enough free space.");

        await CopyWorkspaceAsync(_root, target, cancellationToken).ConfigureAwait(false);
        var temporary = _configurationPath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new { dataDirectory = target }), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, _configurationPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private string Resolve(string value) => Path.GetFullPath(Path.IsPathFullyQualified(value) ? value : Path.Combine(_baseDirectory, value));

    private static async Task CheckWritableAsync(string directory, CancellationToken token)
    {
        var probe = Path.Combine(directory, $".write-probe-{Guid.NewGuid():N}");
        try { await File.WriteAllTextAsync(probe, string.Empty, token).ConfigureAwait(false); }
        finally { if (File.Exists(probe)) File.Delete(probe); }
    }

    private static async Task CopyWorkspaceAsync(string source, string destination, CancellationToken token)
    {
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new IOException("The destination is not empty.");
        var staging = destination + $".copy-{Guid.NewGuid():N}";
        try
        {
            Directory.CreateDirectory(staging);
            await CheckWritableAsync(staging, token).ConfigureAwait(false);
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The data directory cannot be a symbolic link.");
            ValidateNoLinks(source);
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(source, file);
                if (relative.Split(Path.DirectorySeparatorChar).Any(segment => segment is "." or ".."))
                    throw new IOException("Unsafe source path.");
                var info = new FileInfo(file);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Symbolic links are not supported in the data directory: {file}");
                var target = Path.Combine(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (relative.Equals(Path.Combine("Data", "workspace.db"), StringComparison.OrdinalIgnoreCase))
                {
                    using var input = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
                    using var output = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = target, Pooling = false }.ToString());
                    input.Open(); output.Open(); input.BackupDatabase(output);
                }
                else if (!file.EndsWith("-wal", StringComparison.OrdinalIgnoreCase) && !file.EndsWith("-shm", StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(file, target);
                    using var originalStream = File.OpenRead(file);
                    using var copiedStream = File.OpenRead(target);
                    if (!SHA256.HashData(originalStream).SequenceEqual(SHA256.HashData(copiedStream)))
                        throw new IOException($"File verification failed: {relative}");
                }
            }
            var database = Path.Combine(staging, "Data", "workspace.db");
            if (File.Exists(Path.Combine(source, "Data", "workspace.db")) && !File.Exists(database))
                throw new IOException("Database copy is missing.");
            if (File.Exists(database))
            {
                using var verified = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
                verified.Open();
                using var check = verified.CreateCommand();
                check.CommandText = "PRAGMA quick_check;";
                if (!string.Equals(check.ExecuteScalar() as string, "ok", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Database verification failed.");
            }
            if (Directory.Exists(destination)) Directory.Delete(destination);
            Directory.Move(staging, destination);
        }
        catch
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            throw;
        }
    }

    private static void ValidateNoLinks(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Symbolic links are not supported in the data directory: {entry}");
            if (Directory.Exists(entry)) ValidateNoLinks(entry);
        }
    }
}
