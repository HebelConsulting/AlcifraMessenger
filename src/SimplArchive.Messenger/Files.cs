using SimplArchive.ModuleAbi;

namespace SimplArchive.Messenger;

/// <summary>
/// Files sent in a conversation: the client seals a file to the members' certificates (a CMS envelope, .p7m) and uploads
/// it straight to storage; the module files it as a Message Object in the conversation's folder, and for a one-to-one chat
/// places a reference in both members' "My Messages/&lt;counterpart&gt;". The server never sees the file in the clear.
/// </summary>
public sealed class Files(IModuleArchiveFacade archive, Conversations conversations)
{
    /// <summary>The largest file a member may send.</summary>
    public const long MaxBytes = 100L * 1024 * 1024;

    /// <summary>An address to upload the sealed file to, valid briefly.</summary>
    public async Task<ModuleUpload> BeginAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        await conversations.MemberViewAsync(conversationId, cancellationToken);
        return await archive.BeginUploadAsync(MaxBytes, TimeSpan.FromMinutes(15), cancellationToken);
    }

    /// <summary>Files the upload in the conversation; returns the document a message then names.</summary>
    /// <remarks>
    /// The archive name is a fresh id, not the file's own name: that is sealed inside the message, and a name in the clear
    /// would tell the server what was sent.
    /// </remarks>
    public async Task<Guid> CompleteAsync(Guid conversationId, string uploadId, CancellationToken cancellationToken)
    {
        var view = await conversations.MemberViewAsync(conversationId, cancellationToken);
        var sender = (await conversations.IdentityAsync(cancellationToken))?.Email ?? string.Empty;
        var documentId = Guid.Empty;
        await archive.InTransactionAsync(async () =>
        {
            documentId = await archive.FileUploadAsync(uploadId, conversationId, MessengerMasks.MessageObject, $"{Guid.NewGuid():N}", ".p7m",
                new Dictionary<string, string> { [MessengerMasks.SenderField] = sender }, cancellationToken: cancellationToken);
            if (view.IsDirect)
            {
                foreach (var member in view.Members)
                {
                    if (await conversations.ChatFolderAsync(member, conversationId, cancellationToken) is { } folder)
                    {
                        await archive.CreateReferenceAsync(documentId, folder, cancellationToken);
                    }
                }
            }
        }, cancellationToken);
        return documentId;
    }

    /// <summary>A short-lived address of the sealed file, for a member; null where the installation will not hand it out.</summary>
    public async Task<Uri?> DownloadAsync(Guid conversationId, Guid documentId, CancellationToken cancellationToken)
    {
        await conversations.MemberViewAsync(conversationId, cancellationToken);
        if ((await archive.GetDocumentAsync(documentId, cancellationToken))?.ParentId != conversationId)
        {
            throw MessengerErrors.NoSuchMessage();
        }

        return await archive.GetDocumentContentUrlAsync(documentId, TimeSpan.FromMinutes(5), cancellationToken);
    }

    /// <summary>Whether <paramref name="documentId"/> is a file of this conversation: what a message may name.</summary>
    public async Task<bool> BelongsAsync(Guid conversationId, Guid documentId, CancellationToken cancellationToken) =>
        await archive.GetDocumentAsync(documentId, cancellationToken) is { MaskId: var mask, ParentId: var parent }
            && mask == MessengerMasks.MessageObject && parent == conversationId;

    /// <summary>Delete for everyone: the file goes, and every reference to it with it.</summary>
    public Task DeleteForEveryoneAsync(Guid documentId, CancellationToken cancellationToken) =>
        archive.DeleteDocumentAsync(documentId, cancellationToken);

    /// <summary>Delete for me in a one-to-one chat: only my reference goes; the other member keeps theirs.</summary>
    public async Task HideForMeAsync(Guid conversationId, Guid documentId, CancellationToken cancellationToken)
    {
        if (await conversations.ChatFolderAsync(conversations.Me, conversationId, cancellationToken) is { } folder)
        {
            await archive.DeleteReferenceAsync(documentId, folder, cancellationToken);
        }
    }
}
