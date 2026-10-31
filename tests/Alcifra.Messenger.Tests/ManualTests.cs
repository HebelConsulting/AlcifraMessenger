using System.Security.Cryptography;
using SimplArchive.ModuleAbi;

namespace Alcifra.Messenger.Tests;

/// <summary>The module ships its manual (ABI 1.5), and what it ships is the manual committed beside its code.</summary>
public sealed class ManualTests
{
    [Fact]
    public void The_module_declares_a_pdf_manual_the_core_can_open_more_than_once()
    {
        var manual = ((IIndustryModule)new MessengerModule()).Manual;

        Assert.NotNull(manual);
        Assert.Equal("application/pdf", manual.ContentType);

        // Opened twice: the core opens it once per filing check and disposes each stream.
        using var first = manual.Open();
        using var second = manual.Open();
        Assert.True(first.Length > 0);
        Assert.Equal(first.Length, second.Length);
    }

    [Fact]
    public void The_embedded_manual_is_exactly_the_committed_one()
    {
        // manual.yml proves the committed PDF is what manual.typ builds; this proves the module ships THAT file.
        var committed = File.ReadAllBytes(Path.Combine(RepositoryRoot(), "manual", "Alcifra-Messenger-Module-Manual.pdf"));

        using var embedded = new MessengerModule().Manual!.Open();
        Assert.Equal(SHA256.HashData(committed), SHA256.HashData(embedded));
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AlcifraMessenger.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root (AlcifraMessenger.slnx) is not above the test binaries.");
    }
}
