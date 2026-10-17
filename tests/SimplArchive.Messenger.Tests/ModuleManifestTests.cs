using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SimplArchive.Messenger.ReadModel;
using SimplArchive.ModuleAbi;

namespace SimplArchive.Messenger.Tests;

// What the core loads, gates and asks of the Messenger. Each value is one another system depends on, so the test makes a
// change deliberate rather than proving it correct.
public class ModuleManifestTests
{
    private readonly IIndustryModule _module = new MessengerModule();

    [Fact]
    public void Files_are_sealed_content_in_their_own_segment_and_outside_everything_the_core_reads()
    {
        var objects = Assert.Single(_module.Masks, m => m.MaskId == MessengerMasks.MessageObject);
        Assert.Equal("messenger", objects.ContentSegment);
        Assert.Equal(ModuleMaskExemptions.All, objects.Exemptions);
        Assert.True(objects.ContentIsSealed);   // the core refuses anything but an envelope, and serves it on the strict tier
        Assert.False(objects.IsFolderMask);
    }

    [Fact]
    public void My_Messages_is_provisioned_in_every_personal_space_and_holds_only_one_to_one_chats()
    {
        var messages = Assert.Single(_module.Masks, m => m.MaskId == MessengerMasks.Messages);
        Assert.Equal("My Messages", messages.PersonalSpaceFolderName);
        Assert.True(messages.IsFolderMask);
        Assert.True(messages.AdmitsOnlyDeclaredChildren);
        Assert.Equal([MessengerMasks.MessageUser], messages.AdmittedChildren);
    }

    [Fact]
    public void It_needs_no_licence_because_it_is_free_and_open_source()
    {
        // Owner, 2026-10-17 (core ADR 0944): activated without a licence file, anywhere; nothing lapses.
        Assert.False(((IIndustryModule)_module).RequiresLicence);
    }

    [Fact]
    public void It_licenses_certificate_enrolment_because_it_encrypts_to_users()
    {
        Assert.True(_module.LicensesCertificateEnrolment);
        Assert.Null(_module.ReaderCertificates);   // the core answers from its registry
    }

    [Fact]
    public void The_repository_is_an_administrators_setting()
    {
        var setting = Assert.Single(_module.Settings);
        Assert.Equal(MessengerModule.RepositorySetting, setting.Key);
        Assert.False(setting.IsSecret);
    }

    [Fact]
    public void It_declares_the_ABI_it_was_compiled_against()
    {
        Assert.Equal(ModuleAbiVersion.Major, _module.AbiMajorVersion);
        Assert.Equal(ModuleAbiVersion.Minor, _module.AbiMinorVersion);
    }

    [Fact]
    public void The_six_tables_come_up_and_only_messages_are_concurrency_tracked()
    {
        using var connection = new SqliteConnection("Filename=:memory:");
        connection.Open();
        using var db = new MessengerReadModelContext(new DbContextOptionsBuilder<MessengerReadModelContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();

        var tables = db.Model.GetEntityTypes().Select(t => t.GetTableName()).Order().ToList();
        Assert.Equal(["msg_direct", "msg_epoch_keys", "msg_epochs", "msg_hidden", "msg_messages", "msg_receipts"], tables);

        var tracked = db.Model.GetEntityTypes().Where(t => t.GetProperties().Any(p => p.IsConcurrencyToken)).Select(t => t.GetTableName());
        Assert.Equal(["msg_messages"], tracked);
    }
}
