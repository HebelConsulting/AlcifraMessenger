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

/// <summary>
/// The Messenger's REAL controllers in process, the in-memory archive behind them, the core's root and its problem documents
/// reproduced as the core writes them (links; errorCode), and an object store. Who is asking is <see cref="Caller"/>'s user.
/// </summary>
internal sealed class ServerRig : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly Dictionary<string, byte[]> _store = [];
    private WebApplication? _app;

    public FakeArchive Archive { get; } = new();

    public FakeCaller Caller { get; } = new(Guid.NewGuid());

    public FakeNotices Notices { get; } = new();

    public FakeLiveEvents Live { get; } = new();

    public MessengerApi Api { get; private set; } = null!;

    /// <summary>A user of the tenant with an enrolled key (<paramref name="certificatePem"/>, or a placeholder) and a My Messages folder.</summary>
    public Guid AddPerson(string name, string? certificatePem = null)
    {
        var id = Guid.NewGuid();
        var email = $"{name.ToLowerInvariant()}@x.io";
        Archive.Users.Add(new ModuleUser(id, name, email));
        Caller.People[id] = (name, email);
        Archive.Certificates[id] = [certificatePem ?? "pem"];
        Archive.MyMessages[id] = Guid.NewGuid();
        return id;
    }

    public async Task<MessengerApi> StartAsync()
    {
        _connection.Open();
        Archive.Settings[MessengerModule.RepositorySetting] = Guid.NewGuid().ToString();

        var module = new MessengerModule();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(MessengerModule).Assembly);
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, SignedIn>("Test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddDbContext<MessengerReadModelContext>(o => o.UseSqlite(_connection));
        builder.Services.AddSingleton<IModuleArchiveFacade>(Archive);
        builder.Services.AddSingleton<IModuleCallerContext>(Caller);
        builder.Services.AddSingleton<IModuleNotices>(Notices);
        builder.Services.AddSingleton<IModuleLiveEvents>(Live);
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

        Api = new MessengerApi(_app.GetTestClient(), new HttpClient(new FakeStore(_store, Archive)));
        return Api;
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        _connection.Dispose();
    }

    /// <summary>Every request is signed in; who is asking is the fake caller's switchable user.</summary>
    private sealed class SignedIn(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "test")], "Test")), "Test")));
    }

    /// <summary>The object store: a presigned PUT keeps the bytes by upload, a GET of a document's content address returns
    /// the bytes it was filed from.</summary>
    private sealed class FakeStore(Dictionary<string, byte[]> store, FakeArchive archive) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Null(request.Headers.Authorization);   // the archive's token never reaches the store
            if (request.Method == HttpMethod.Put)
            {
                store[request.RequestUri!.AbsolutePath.Trim('/')] = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            var document = Guid.Parse(request.RequestUri!.Segments[^1]);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(store[archive.FiledFrom[document]]) };
        }
    }
}
