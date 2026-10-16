using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SimplArchive.Messenger.Client;
using SimplArchive.Messenger.Client.Api;
using SimplArchive.Messenger.ReadModel;
using SimplArchive.ModuleAbi;
using ClientKey = SimplArchive.Messenger.Client.Api.WrappedKey;

namespace SimplArchive.Messenger.Tests;

// The client library against the REAL controllers, in process: every field name, rel and status the phone will meet is
// the one the server sends. The archive behind them is the in-memory fake; the core's root and its problem documents
// are reproduced as the core writes them (links; errorCode).
public sealed class WireTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly FakeArchive _archive = new();
    private readonly FakeCaller _caller = new(Guid.NewGuid());
    private readonly FakeNotices _notices = new();
    private readonly Guid _anna = Guid.NewGuid(), _tom = Guid.NewGuid();
    private readonly Dictionary<string, byte[]> _store = [];
    private WebApplication? _app;

    private async Task<(MessengerApi Api, HttpClient Http)> StartAsync()
    {
        _connection.Open();
        _archive.Settings[MessengerModule.RepositorySetting] = Guid.NewGuid().ToString();
        foreach (var (id, name) in new[] { (_anna, "Anna"), (_tom, "Tom") })
        {
            var email = $"{name.ToLowerInvariant()}@x.io";
            _archive.Users.Add(new ModuleUser(id, name, email));
            _caller.People[id] = (name, email);
            _archive.Certificates[id] = ["pem"];
            _archive.MyMessages[id] = Guid.NewGuid();
        }

        var module = new MessengerModule();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(MessengerModule).Assembly);
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, SignedIn>("Test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddDbContext<MessengerReadModelContext>(o => o.UseSqlite(_connection));
        builder.Services.AddSingleton<IModuleArchiveFacade>(_archive);
        builder.Services.AddSingleton<IModuleCallerContext>(_caller);
        builder.Services.AddSingleton<IModuleNotices>(_notices);
        module.ConfigureServices(builder.Services);

        _app = builder.Build();
        using (var scope = _app.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<MessengerReadModelContext>().Database.EnsureCreated();
        }

        _app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            catch (ModuleApiException refusal)   // as the core's handler writes it: RFC 7807 with errorCode
            {
                context.Response.StatusCode = refusal.StatusCode;
                await context.Response.WriteAsJsonAsync(new { title = refusal.Message, detail = refusal.Message, errorCode = refusal.ErrorCode });
            }
        });
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        _app.MapGet("/api", () => new { links = module.RootLinks.Select(l => new { rel = l.Rel, href = l.Path, method = l.Method }) });
        await _app.StartAsync();

        var http = _app.GetTestClient();
        var storage = new HttpClient(new FakeStore(_store));
        var api = new MessengerApi(http, storage);
        return (api, http);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        _connection.Dispose();
    }

    [Fact]
    public async Task A_conversation_round_trips_from_one_phone_to_another()
    {
        var (api, _) = await StartAsync();
        _caller.UserId = _anna;
        Assert.True(await api.DiscoverAsync());

        var tom = Assert.Single((await api.PeopleAsync("tom")).Items);
        Assert.Equal("Tom", tom.DisplayName);
        Assert.Single((await api.CertificatesAsync(tom)).CertificatesPem);

        var list = await api.ConversationsAsync();
        var chat = await api.OpenDirectAsync(list, tom, [new ClientKey(_anna, [1]), new ClientKey(_tom, [2])]);
        Assert.True(chat.IsDirect);
        Assert.Equal([1], chat.MyKey);
        Assert.Equal("Tom", chat.CounterpartName);

        var key = ConversationKeys.NewKey();
        var id = Guid.NewGuid();
        var page = await api.MessagesAsync(chat);
        var sent = await api.SendAsync(page, id, chat.Epoch,
            MessageFormat.Seal(key, new MessageContext(chat.Id, chat.Epoch, id, _anna), "Hello Tom"u8));
        Assert.Equal(_tom, Assert.Single(_notices.Signals).RecipientUserId);

        _caller.UserId = _tom;
        var hisView = Assert.Single((await api.ConversationsAsync()).Items);
        Assert.Equal(1, hisView.Unread);
        var received = Assert.Single((await api.MessagesAsync(hisView)).Items);
        var text = MessageFormat.Open(key, new MessageContext(hisView.Id, received.Epoch, received.Id, received.SenderUserId), received.Ciphertext);
        Assert.Equal("Hello Tom", Encoding.UTF8.GetString(text));
        await api.MarkReadAsync(hisView, DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(0, (await api.RefreshAsync(hisView)).Unread);
        Assert.Contains((_tom, hisView.Id), _notices.Cleared);

        // Only the sender edits, and only under the tag they saw: a second edit with the first tag is a 412.
        _caller.UserId = _anna;
        var edited = await api.EditAsync(sent, chat.Epoch, MessageFormat.Seal(key, new MessageContext(chat.Id, chat.Epoch, id, _anna), "Hi Tom"u8));
        Assert.NotNull(edited.EditedAt);
        var stale = await Assert.ThrowsAsync<MessengerApiException>(() => api.EditAsync(sent, chat.Epoch, [1, 2, 3]));
        Assert.Equal((412, "ETAG_MISMATCH"), (stale.StatusCode, stale.Code));
    }

    [Fact]
    public async Task Groups_files_and_refusals_travel_as_the_server_sends_them()
    {
        var (api, _) = await StartAsync();
        _caller.UserId = _anna;
        await api.DiscoverAsync();

        var group = await api.CreateGroupAsync(await api.ConversationsAsync(), "Team", [new ClientKey(_anna, [1]), new ClientKey(_tom, [2])]);
        Assert.True(group.CanManageMembers);
        var tomAsMember = group.Members.Single(m => m.UserId == _tom);
        Assert.True(tomAsMember.Offers("make-admin"));
        Assert.Equal("Tom", tomAsMember.DisplayName);
        await api.MakeAdminAsync(tomAsMember);
        Assert.True((await api.RefreshAsync(group)).Members.Single(m => m.UserId == _tom).IsAdmin);

        var sealedFile = RandomNumberGenerator.GetBytes(64);
        var file = await api.UploadFileAsync(group, sealedFile);
        var page = await api.MessagesAsync(group);
        var id = Guid.NewGuid();
        var withFile = await api.SendAsync(page, id, group.Epoch, [9], attachmentDocumentId: file.DocumentId);
        Assert.Equal(sealedFile, await api.DownloadFileAsync(withFile));   // through the redirect, from the store

        // After a removal the new epoch's key joins the old one, so older messages still open.
        var eve = Guid.NewGuid();
        _archive.Users.Add(new ModuleUser(eve, "Eve", "eve@x.io"));
        _archive.Certificates[eve] = ["pem"];
        await api.AddMemberAsync(group, new ClientKey(eve, [3]));
        group = await api.RefreshAsync(group);
        await api.RemoveMemberAsync(group.Members.Single(m => m.UserId == eve), [new ClientKey(_anna, [4]), new ClientKey(_tom, [5])]);
        group = await api.RefreshAsync(group);
        Assert.Equal([(1, (byte)1), (2, (byte)4)], group.MyKeys.Select(k => (k.Epoch, k.Key.Single())));

        // A refusal arrives with the server's own code.
        var refusal = await Assert.ThrowsAsync<MessengerApiException>(() => api.SendAsync(page, Guid.NewGuid(), group.Epoch + 1, [9]));
        Assert.Equal((409, "MESSENGER_STALE_EPOCH"), (refusal.StatusCode, refusal.Code));
    }

    /// <summary>Every request is signed in; who is asking is the fake caller's switchable user.</summary>
    private sealed class SignedIn(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "test")], "Test")), "Test")));
    }

    /// <summary>The object store: a presigned PUT keeps the bytes, a GET of the content address returns them.</summary>
    private sealed class FakeStore(Dictionary<string, byte[]> store) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Null(request.Headers.Authorization);   // the archive's token never reaches the store
            if (request.Method == HttpMethod.Put)
            {
                store["last"] = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(store["last"]) };
        }
    }
}
