using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SimplArchive.Messenger.Client.Api;

namespace SimplArchive.Messenger.Client.Backup;

/// <summary>
/// A user's conversations in one file, and back (core #1813). <b>A backup is sealed only</b>: it holds the ciphertext
/// exactly as delivered, the user's own sealed copy of each epoch's key, and each file as the envelope it was sent as —
/// nothing is decrypted on the way out, so a backup opens only with a key whose certificate it was sealed to.
/// </summary>
/// <remarks>
/// The layout (see <c>docs/format.md</c>), a zip: <c>manifest.json</c> naming every other entry with its SHA-256;
/// <c>conversations/&lt;id&gt;/conversation.json</c> (members, the owner's id there, the owner's keys per epoch) and
/// <c>messages.jsonl</c>; <c>blobs/&lt;sha256&gt;</c> for the files. Ids are the ones the ciphertext is bound to, so a
/// restore keeps them, and restoring the same backup twice changes nothing.
/// </remarks>
public static class MessengerBackup
{
    /// <summary>The manifest's format name.</summary>
    public const string Format = "simplarchive-messenger-backup";

    /// <summary>The layout version this library writes and reads.</summary>
    public const int Version = 1;

    private const string ManifestPath = "manifest.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Writes every conversation the caller is in to <paramref name="output"/>. <paramref name="instance"/> is the
    /// archive's address as the caller knows it, kept so a restore can say where the history came from.
    /// </summary>
    public static async Task<BackupSummary> WriteAsync(MessengerApi api, Stream output, string instance, CancellationToken cancellationToken = default)
    {
        var entries = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var conversations = (await api.ConversationsAsync(cancellationToken)).Items;
        BackupMember? owner = null;
        int messageCount = 0, fileCount = 0;
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var conversation in conversations)
            {
                var me = conversation.Members.FirstOrDefault(m => m.UserId == conversation.MyUserId);
                owner ??= me is null ? null : new BackupMember(me.UserId, me.Email, me.DisplayName);
                var folder = $"conversations/{conversation.SealedUnder:N}";
                var lines = new StringBuilder();
                foreach (var message in await AllMessagesAsync(api, conversation, cancellationToken))
                {
                    // Every file the message carries, as the envelope it was sent as: the first as `file`, as before, the
                    // further ones (several files and a caption are one message) as `files`.
                    var blobs = new List<string>();
                    if (message.AttachmentDocumentId is not null && message.DeletedAt is null)
                    {
                        for (var index = 0; index < Math.Max(1, message.Files.Count); index++)
                        {
                            var bytes = await api.DownloadFileAsync(message, index, cancellationToken);
                            var blob = $"blobs/{Hash(bytes)}";
                            if (!entries.ContainsKey(blob))
                            {
                                await AddAsync(zip, entries, blob, bytes, cancellationToken);
                                fileCount++;
                            }

                            blobs.Add(blob);
                        }
                    }

                    lines.AppendLine(JsonSerializer.Serialize(new Line(
                        message.Id, message.Epoch, message.SenderUserId, message.SentAt, message.EditedAt, message.DeletedAt,
                        message.ReplyToId, message.Ciphertext, blobs.FirstOrDefault()) { Files = blobs.Count > 1 ? blobs[1..] : null }, Json));
                    messageCount++;
                }

                var record = new ConversationRecord(
                    conversation.SealedUnder, Title(conversation), conversation.IsDirect,
                    [.. conversation.Members.Select(m => new BackupMember(m.UserId, m.Email, m.DisplayName))],
                    conversation.MyUserId,
                    [.. conversation.MyKeys.OrderBy(k => k.Epoch).Select(k => new KeyRecord(k.Epoch, k.Key))]);
                await AddAsync(zip, entries, $"{folder}/conversation.json", JsonSerializer.SerializeToUtf8Bytes(record, Json), cancellationToken);
                await AddAsync(zip, entries, $"{folder}/messages.jsonl", Encoding.UTF8.GetBytes(lines.ToString()), cancellationToken);
            }

            var manifest = new Manifest(Format, Version, DateTimeOffset.UtcNow, instance, owner, entries);
            await AddAsync(zip, null, ManifestPath, JsonSerializer.SerializeToUtf8Bytes(manifest, Json), cancellationToken);
        }

        return new BackupSummary(conversations.Count, messageCount, fileCount);
    }

    /// <summary>
    /// Restores <paramref name="input"/> for the caller: each conversation as read-only history, joined where another
    /// member restored it already; messages and files already there are skipped. The backup must be the caller's own.
    /// </summary>
    /// <exception cref="BackupDamagedException">An entry is missing, unlisted or does not match its hash.</exception>
    public static async Task<RestoreReport> RestoreAsync(MessengerApi api, Stream input, CancellationToken cancellationToken = default)
    {
        using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        var manifest = JsonSerializer.Deserialize<Manifest>(await ReadRawAsync(zip, ManifestPath, cancellationToken), Json)
            ?? throw new BackupDamagedException("The manifest is empty.");
        if (manifest.Format != Format || manifest.Version != Version)
        {
            throw new BackupDamagedException($"This is not a version {Version} messenger backup ({manifest.Format} {manifest.Version}).");
        }

        // Every entry checked before the server hears of it: a damaged backup changes nothing, not half a conversation.
        foreach (var entry in zip.Entries.Where(e => e.FullName != ManifestPath))
        {
            await ReadAsync(zip, manifest, entry.FullName, cancellationToken);
        }

        foreach (var listed in manifest.Entries.Keys)
        {
            _ = zip.GetEntry(listed) ?? throw new BackupDamagedException($"{listed} is missing.");
        }

        var list = await api.ConversationsAsync(cancellationToken);
        var report = new RestoreReport();
        foreach (var path in manifest.Entries.Keys.Where(p => p.EndsWith("/conversation.json", StringComparison.Ordinal)))
        {
            var record = JsonSerializer.Deserialize<ConversationRecord>(await ReadAsync(zip, manifest, path, cancellationToken), Json)!;
            var folder = path[..path.LastIndexOf('/')];
            Conversation restored;
            try
            {
                restored = await api.RestoreAsync(list, record.Id, manifest.Instance, record.Name, record.IsDirect, record.Members, record.MyUserId,
                    record.Keys.ToDictionary(k => k.Epoch, k => k.Key), cancellationToken);
            }
            catch (MessengerApiException refused) when (refused.Code == "MESSENGER_STILL_HERE")
            {
                report.StillHere++;
                continue;
            }

            // What is here already is skipped without uploading its file again: restoring twice adds nothing.
            var present = (await AllMessagesAsync(api, restored, cancellationToken)).Select(m => m.Id).ToHashSet();
            var lines = Encoding.UTF8.GetString(await ReadAsync(zip, manifest, $"{folder}/messages.jsonl", cancellationToken))
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => JsonSerializer.Deserialize<Line>(l, Json)!)
                .Where(l => !present.Contains(l.Id))
                .ToList();
            foreach (var batch in lines.Chunk(200))
            {
                var messages = new List<BackupMessage>();
                foreach (var line in batch)
                {
                    Guid? file = null;
                    var further = new List<Guid>();
                    if (line.File is { } blob)
                    {
                        file = (await api.UploadFileAsync(restored, await ReadAsync(zip, manifest, blob, cancellationToken), cancellationToken)).DocumentId;
                        report.Files++;
                        foreach (var next in line.Files ?? [])
                        {
                            further.Add((await api.UploadFileAsync(restored, await ReadAsync(zip, manifest, next, cancellationToken), cancellationToken)).DocumentId);
                            report.Files++;
                        }
                    }

                    messages.Add(new BackupMessage(line.Id, line.Epoch, line.SenderUserId, line.SentAt, line.EditedAt, line.DeletedAt,
                        line.ReplyToId, file, line.Ciphertext) { FurtherDocumentIds = further });
                }

                var result = await api.ImportAsync(restored, messages, cancellationToken);
                (report.Imported, report.Skipped) = (report.Imported + result.Imported, report.Skipped + result.Skipped);
            }

            report.Skipped += present.Count;
            report.Conversations++;
        }

        return report;
    }

    // A conversation's title as its members read it: a group's name, or the other person's in a one-to-one chat.
    private static string Title(Conversation conversation) =>
        conversation.IsDirect ? conversation.CounterpartName ?? conversation.Name : conversation.Name;

    private static async Task<List<Message>> AllMessagesAsync(MessengerApi api, Conversation conversation, CancellationToken cancellationToken)
    {
        var all = new List<Message>();
        for (var page = await api.MessagesAsync(conversation, cancellationToken); page is { Items.Count: > 0 }; page = await api.NextAsync(page, cancellationToken))
        {
            all.AddRange(page.Items);
        }

        return all;
    }

    private static async Task AddAsync(ZipArchive zip, IDictionary<string, string>? entries, string path, byte[] bytes, CancellationToken cancellationToken)
    {
        // Stored, not compressed: ciphertext does not shrink, and the hash is of exactly these bytes.
        await using (var stream = zip.CreateEntry(path, CompressionLevel.NoCompression).Open())
        {
            await stream.WriteAsync(bytes, cancellationToken);
        }

        entries?.Add(path, Hash(bytes));
    }

    private static async Task<byte[]> ReadAsync(ZipArchive zip, Manifest manifest, string path, CancellationToken cancellationToken)
    {
        if (!manifest.Entries.TryGetValue(path, out var expected))
        {
            throw new BackupDamagedException($"{path} is not listed in the manifest.");
        }

        var bytes = await ReadRawAsync(zip, path, cancellationToken);
        return Hash(bytes) == expected ? bytes : throw new BackupDamagedException($"{path} does not match its hash: the backup was changed or damaged.");
    }

    private static async Task<byte[]> ReadRawAsync(ZipArchive zip, string path, CancellationToken cancellationToken)
    {
        var entry = zip.GetEntry(path) ?? throw new BackupDamagedException($"{path} is missing.");
        await using var stream = entry.Open();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed record Manifest(
        string Format, int Version, DateTimeOffset CreatedAt, string Instance, BackupMember? Owner, IDictionary<string, string> Entries);

    private sealed record ConversationRecord(
        Guid Id, string Name, bool IsDirect, List<BackupMember> Members, Guid MyUserId, List<KeyRecord> Keys);

    private sealed record KeyRecord(int Epoch, byte[] Key);

    private sealed record Line(
        Guid Id, int Epoch, Guid SenderUserId, DateTimeOffset SentAt, DateTimeOffset? EditedAt, DateTimeOffset? DeletedAt,
        Guid? ReplyToId, byte[] Ciphertext, string? File)
    {
        /// <summary>The further files' blobs after the first, in order; absent for none (and from an older backup).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Files { get; init; }
    }
}

/// <summary>What a backup holds.</summary>
public sealed record BackupSummary(int Conversations, int Messages, int Files);

/// <summary>What a restore did.</summary>
public sealed class RestoreReport
{
    /// <summary>Conversations restored or joined.</summary>
    public int Conversations { get; set; }

    /// <summary>Messages added.</summary>
    public int Imported { get; set; }

    /// <summary>Messages already there.</summary>
    public int Skipped { get; set; }

    /// <summary>Files uploaded again.</summary>
    public int Files { get; set; }

    /// <summary>Conversations still on this installation, left as they are.</summary>
    public int StillHere { get; set; }
}

/// <summary>A backup that is incomplete, altered or not a messenger backup at all.</summary>
public sealed class BackupDamagedException(string message) : Exception(message);
