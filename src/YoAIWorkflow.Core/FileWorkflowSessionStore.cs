using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YoAIWorkflow.Abstractions;

namespace YoAIWorkflow.Core;

/// <summary>
/// Local file storage for a single host's workflow sessions. A lock file serializes writers
/// across processes on the same filesystem; snapshots are written then atomically renamed.
/// </summary>
public sealed class FileWorkflowSessionStore<TState> : IWorkflowSessionStore<TState>
{
    private readonly string _directory;
    private readonly JsonSerializerOptions _options;

    public FileWorkflowSessionStore(string directory, JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(_directory);
        _options = options ?? new JsonSerializerOptions();
    }

    public async Task<WorkflowSession<TState>?> LoadAsync(
        string workflowId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        var path = PathFor(workflowId);
        using var fileLock = await LockAsync(path, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(path, workflowId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> TrySaveAsync(
        WorkflowSession<TState> session, long expectedRevision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.WorkflowId);
        if (expectedRevision < -1 || session.Revision != checked(expectedRevision + 1))
            throw new ArgumentException("Session revision must be one greater than expectedRevision.", nameof(session));

        var path = PathFor(session.WorkflowId);
        using var fileLock = await LockAsync(path, cancellationToken).ConfigureAwait(false);
        var current = await ReadAsync(path, session.WorkflowId, cancellationToken).ConfigureAwait(false);
        if ((current?.Revision ?? -1) != expectedRevision) return false;

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, session, _options, cancellationToken)
                    .ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
            return true;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private async Task<WorkflowSession<TState>?> ReadAsync(
        string path, string workflowId, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var session = await JsonSerializer.DeserializeAsync<WorkflowSession<TState>>(
            stream, _options, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The session file contains no workflow session.");
        if (session.WorkflowId != workflowId || session.ProcessedInputIds is null || session.Actions is null)
            throw new InvalidDataException("The session file contains an invalid workflow session.");
        return session;
    }

    private async Task<FileStream> LockAsync(string path, CancellationToken cancellationToken)
    {
        var lockPath = path + ".lock";
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private string PathFor(string workflowId) => Path.Combine(_directory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(workflowId))) + ".json");
}
