using CPCREDO.Application.Members;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Domain.Members;
using CPCREDO.Infrastructure.Audit;
using CPCREDO.Infrastructure.Members;
using CPCREDO.Infrastructure.Persistence;
using CPCREDO.Tests.Accounting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace CPCREDO.Tests.Members;

public sealed class KycDocumentTests
{
    private static readonly byte[] Jpeg = RgbJpeg(800, 800, 40, 90, 70);
    private static readonly byte[] Png = RgbPng(1000, 800, 200, 180, 140);
    private static readonly byte[] Pdf = "%PDF-1.4\n%%EOF"u8.ToArray();

    private static byte[] RgbJpeg(int width, int height, byte r, byte g, byte b)
    {
        using var img = new Image<Rgb24>(width, height);
        Fill(img, new Rgb24(r, g, b));
        using var ms = new MemoryStream();
        img.Save(ms, new JpegEncoder { Quality = 90 });
        return ms.ToArray();
    }

    private static byte[] RgbPng(int width, int height, byte r, byte g, byte b)
    {
        using var img = new Image<Rgb24>(width, height);
        Fill(img, new Rgb24(r, g, b));
        using var ms = new MemoryStream();
        img.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    private static void Fill(Image<Rgb24> img, Rgb24 color)
    {
        for (var y = 0; y < img.Height; y++)
        {
            for (var x = 0; x < img.Width; x++)
                img[x, y] = color;
        }
    }

    [Fact]
    public async Task Upload_stores_file_on_disk_not_in_postgres()
    {
        using var h = new KycHarness();
        var member = await h.CreateMemberAsync();

        await using var stream = new MemoryStream(Jpeg);
        var uploaded = await h.Kyc.UploadAsync(member.Id, KycDocumentType.Photo, stream, "face.jpg", "image/jpeg", stream.Length);

        Assert.True(uploaded.IsSuccess, uploaded.ErrorMessage);
        Assert.Equal("Photo", uploaded.Value!.Type);
        Assert.Equal("photo.jpg", uploaded.Value.FileName);
        Assert.Equal("Administrateur CPCREDO", uploaded.Value.UploadedByName);

        var row = Assert.Single(h.Db.KycDocuments);
        Assert.Null(typeof(KycDocument).GetProperty("Content"));
        Assert.Null(typeof(KycDocument).GetProperty("Bytes"));
        Assert.False(string.IsNullOrWhiteSpace(row.FilePath));
        var full = Path.Combine(h.Root, row.FilePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(full));
        var stored = await File.ReadAllBytesAsync(full);
        Assert.True(stored.Length <= KycImageProcessor.PhotoMaxBytes);
        using (var decoded = Image.Load(stored))
        {
            Assert.Equal(KycImageProcessor.PhotoEdge, decoded.Width);
            Assert.Equal(KycImageProcessor.PhotoEdge, decoded.Height);
        }

        var view = await h.Members.Get360Async(member.Id);
        Assert.True(view.IsSuccess);
        var doc = Assert.Single(view.Value!.KycDocuments);
        Assert.Equal("Photo", doc.Type);

        var list = await h.Members.SearchAsync(null, null, 0, 50);
        Assert.True(list.IsSuccess);
        Assert.DoesNotContain("cin", list.Value!.Items[0].GetType().GetProperties().Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Photo_rejects_pdf_and_oversize()
    {
        using var h = new KycHarness();
        var member = await h.CreateMemberAsync();

        await using var pdf = new MemoryStream(Pdf);
        var badType = await h.Kyc.UploadAsync(member.Id, KycDocumentType.Photo, pdf, "id.pdf", "application/pdf", pdf.Length);
        Assert.False(badType.IsSuccess);
        Assert.Equal("kyc.content_type", badType.ErrorCode);

        var huge = new byte[KycDocumentService.MaxBytes + 12];
        Jpeg.CopyTo(huge, 0);
        huge[0] = 0xFF;
        huge[1] = 0xD8;
        huge[2] = 0xFF;
        await using var big = new MemoryStream(huge);
        var tooBig = await h.Kyc.UploadAsync(member.Id, KycDocumentType.Photo, big, "face.jpg", "image/jpeg", huge.Length);
        Assert.False(tooBig.IsSuccess);
        Assert.Equal("kyc.too_large", tooBig.ErrorCode);
    }

    [Fact]
    public async Task Id_rejects_pdf_and_replace_keeps_cr80_ratio()
    {
        using var h = new KycHarness();
        var member = await h.CreateMemberAsync();

        await using var png = new MemoryStream(Png);
        var first = await h.Kyc.UploadAsync(member.Id, KycDocumentType.IdFront, png, "cin.png", "image/png", png.Length);
        Assert.True(first.IsSuccess, first.ErrorMessage);
        var pngPath = Path.Combine(h.Root, member.Id.ToString("D"), "id-front.png");
        Assert.True(File.Exists(pngPath));
        using (var decoded = Image.Load(pngPath))
            Assert.InRange(decoded.Width / (double)decoded.Height, KycImageProcessor.IdAspect - 0.02, KycImageProcessor.IdAspect + 0.02);

        await using var pdf = new MemoryStream(Pdf);
        var pdfRejected = await h.Kyc.UploadAsync(member.Id, KycDocumentType.IdFront, pdf, "cin.pdf", "application/pdf", pdf.Length);
        Assert.False(pdfRejected.IsSuccess);
        Assert.Equal("kyc.content_type", pdfRejected.ErrorCode);

        var jpeg = RgbJpeg(640, 480, 30, 30, 30);
        await using var next = new MemoryStream(jpeg);
        var replaced = await h.Kyc.UploadAsync(member.Id, KycDocumentType.IdFront, next, "cin.jpg", "image/jpeg", next.Length);
        Assert.True(replaced.IsSuccess, replaced.ErrorMessage);
        Assert.Equal("id-front.jpg", replaced.Value!.FileName);
        Assert.False(File.Exists(pngPath));
        Assert.True(File.Exists(Path.Combine(h.Root, member.Id.ToString("D"), "id-front.jpg")));
        Assert.Equal(1, h.Db.KycDocuments.Count());
        Assert.Contains(h.Db.AuditLogs, a => a.Action == "KycDocument.Replaced");
    }

    [Fact]
    public async Task Delete_removes_metadata_file_and_writes_audit()
    {
        using var h = new KycHarness();
        var member = await h.CreateMemberAsync();
        await using var stream = new MemoryStream(Jpeg);
        var uploaded = await h.Kyc.UploadAsync(member.Id, KycDocumentType.Signature, stream, "sig.jpg", "image/jpeg", stream.Length);
        Assert.True(uploaded.IsSuccess, uploaded.ErrorMessage);
        var full = Path.Combine(h.Root, member.Id.ToString("D"), "signature.jpg");
        Assert.True(File.Exists(full));

        var deleted = await h.Kyc.DeleteAsync(member.Id, KycDocumentType.Signature);
        Assert.True(deleted.IsSuccess, deleted.ErrorMessage);
        Assert.False(File.Exists(full));
        Assert.Empty(h.Db.KycDocuments);
        Assert.Contains(h.Db.AuditLogs, a => a.Action == "KycDocument.Deleted" && a.EntityType == nameof(KycDocument));
    }

    [Fact]
    public async Task Caissier_can_fill_empty_slot_but_cannot_replace()
    {
        using var h = new KycHarness();
        var member = await h.CreateMemberAsync();

        h.User.Roles = [RoleNames.Caissier];
        await using var one = new MemoryStream(Jpeg);
        var cashier = await h.Kyc.UploadAsync(member.Id, KycDocumentType.Photo, one, "face.jpg", "image/jpeg", one.Length);
        Assert.True(cashier.IsSuccess, cashier.ErrorMessage);

        await using var two = new MemoryStream(Jpeg);
        var replaced = await h.Kyc.UploadAsync(member.Id, KycDocumentType.Photo, two, "face2.jpg", "image/jpeg", two.Length);
        Assert.False(replaced.IsSuccess);
        Assert.Equal("kyc.locked", replaced.ErrorCode);
    }

    [Fact]
    public async Task Commissaire_cannot_upload()
    {
        using var h = new KycHarness();
        var member = await h.CreateMemberAsync();
        h.User.Roles = [RoleNames.Commissaire];
        await using var two = new MemoryStream(Jpeg);
        var commissaire = await h.Kyc.UploadAsync(member.Id, KycDocumentType.Photo, two, "face.jpg", "image/jpeg", two.Length);
        Assert.False(commissaire.IsSuccess);
        Assert.Equal("auth.forbidden", commissaire.ErrorCode);

        var view = await h.Kyc.ListAsync(member.Id);
        Assert.True(view.IsSuccess, view.ErrorMessage);
    }

    [Fact]
    public async Task Saving_member_does_not_require_kyc_files()
    {
        using var h = new KycHarness();
        var member = await h.CreateMemberAsync();
        var updated = await h.Members.UpdateAsync(member.Id, new MemberWriteRequest
        {
            FirstName = member.FirstName,
            LastName = member.LastName,
            Cin = member.Cin,
            Phone = member.Phone,
            AddressLine = member.AddressLine,
            City = member.City,
            Status = MemberStatus.Active,
            KycStatus = KycStatus.Verified,
            LegalStatus = LegalStatus.Usager
        });
        Assert.True(updated.IsSuccess, updated.ErrorMessage);
        Assert.Empty(updated.Value!.KycDocuments);
    }

    [Fact]
    public async Task Service_client_can_fill_empty_slot_but_cannot_replace()
    {
        using var h = new KycHarness();
        var member = await h.CreateMemberAsync();
        h.User.Roles = [RoleNames.ServiceClient];

        await using var first = new MemoryStream(Jpeg);
        var uploaded = await h.Kyc.UploadAsync(member.Id, KycDocumentType.Photo, first, "face.jpg", "image/jpeg", first.Length);
        Assert.True(uploaded.IsSuccess, uploaded.ErrorMessage);

        await using var second = new MemoryStream(Jpeg);
        var replaced = await h.Kyc.UploadAsync(member.Id, KycDocumentType.Photo, second, "face2.jpg", "image/jpeg", second.Length);
        Assert.False(replaced.IsSuccess);
        Assert.Equal("kyc.locked", replaced.ErrorCode);

        var deleted = await h.Kyc.DeleteAsync(member.Id, KycDocumentType.Photo);
        Assert.False(deleted.IsSuccess);
        Assert.Equal("kyc.locked", deleted.ErrorCode);
        Assert.Single(h.Db.KycDocuments);
    }

    [Fact]
    public async Task Wrong_override_password_is_401_and_does_not_change_file()
    {
        using var h = new KycHarness();
        var member = await h.CreateMemberAsync();
        await using var stream = new MemoryStream(Jpeg);
        var uploaded = await h.Kyc.UploadAsync(member.Id, KycDocumentType.Photo, stream, "face.jpg", "image/jpeg", stream.Length);
        Assert.True(uploaded.IsSuccess, uploaded.ErrorMessage);

        h.User.Roles = [RoleNames.OfficierCredit];
        var auth = await h.Kyc.AuthorizeOverrideAsync(uploaded.Value!.Id, new KycOverrideAuthRequest
        {
            Username = "gerant",
            Password = "wrong",
            Action = "replace"
        });
        Assert.False(auth.IsSuccess);
        Assert.Equal("auth.invalid_credentials", auth.ErrorCode);
        Assert.Single(h.Db.KycDocuments);
    }

    [Fact]
    public async Task Gerant_override_allows_one_replace_then_locks_again()
    {
        using var h = new KycHarness();
        var member = await h.CreateMemberAsync();
        await using var first = new MemoryStream(Jpeg);
        var uploaded = await h.Kyc.UploadAsync(member.Id, KycDocumentType.Photo, first, "face.jpg", "image/jpeg", first.Length);
        Assert.True(uploaded.IsSuccess, uploaded.ErrorMessage);
        var originalPath = Path.Combine(h.Root, member.Id.ToString("D"), "photo.jpg");

        h.User.Roles = [RoleNames.ServiceClient];
        var granted = await h.Kyc.AuthorizeOverrideAsync(uploaded.Value!.Id, new KycOverrideAuthRequest
        {
            Username = "gerant",
            Password = KycHarness.GerantPassword,
            Action = "replace"
        });
        Assert.True(granted.IsSuccess, granted.ErrorMessage);
        Assert.Contains(h.Db.AuditLogs, a => a.Action == "KycDocument.OverrideAuthorized" && a.UserId == h.User.UserId);

        await using var png = new MemoryStream(Png);
        var replaced = await h.Kyc.UploadAsync(
            member.Id,
            KycDocumentType.Photo,
            png,
            "face.png",
            "image/png",
            png.Length,
            granted.Value!.GrantId);
        Assert.True(replaced.IsSuccess, replaced.ErrorMessage);
        Assert.Equal("photo.jpg", replaced.Value!.FileName);
        Assert.True(File.Exists(originalPath));
        using (var decoded = Image.Load(originalPath))
        {
            Assert.Equal(KycImageProcessor.PhotoEdge, decoded.Width);
            Assert.Equal(KycImageProcessor.PhotoEdge, decoded.Height);
        }
        Assert.Contains(h.Db.AuditLogs, a =>
            a.Action == "KycDocument.Replaced"
            && a.DetailsJson != null
            && a.DetailsJson.Contains("gerant", StringComparison.OrdinalIgnoreCase));

        await using var again = new MemoryStream(Jpeg);
        var second = await h.Kyc.UploadAsync(
            member.Id,
            KycDocumentType.Photo,
            again,
            "face.jpg",
            "image/jpeg",
            again.Length,
            granted.Value.GrantId);
        Assert.False(second.IsSuccess);
        Assert.Equal("kyc.locked", second.ErrorCode);
    }

    [Fact]
    public async Task Gerant_can_replace_without_override()
    {
        using var h = new KycHarness();
        var member = await h.CreateMemberAsync();
        await using var first = new MemoryStream(Jpeg);
        var uploaded = await h.Kyc.UploadAsync(member.Id, KycDocumentType.Photo, first, "face.jpg", "image/jpeg", first.Length);
        Assert.True(uploaded.IsSuccess, uploaded.ErrorMessage);

        h.User.Roles = [RoleNames.Gerant];
        await using var png = new MemoryStream(Png);
        var replaced = await h.Kyc.UploadAsync(member.Id, KycDocumentType.Photo, png, "face.png", "image/png", png.Length);
        Assert.True(replaced.IsSuccess, replaced.ErrorMessage);
    }
}

internal sealed class KycHarness : IDisposable
{
    public CpcredoDbContext Db { get; }
    public MemberService Members { get; }
    public KycDocumentService Kyc { get; }
    public KycOverrideStore Overrides { get; }
    public TestCurrentUser User { get; }
    public string Root { get; }
    public const string GerantPassword = "Gerant@Cpcredo2026";

    public KycHarness()
    {
        var inner = new MembershipHarness();
        Db = inner.Db;
        Members = inner.Members;
        User = inner.User;
        Root = Path.Combine(Path.GetTempPath(), "cpcredo-kyc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        SeedGerant(Db);
        var audit = new AuditLogger(Db, inner.Clock, User);
        Overrides = new KycOverrideStore();
        Kyc = new KycDocumentService(
            Db,
            User,
            inner.Clock,
            audit,
            Options.Create(new KycStorageOptions { RootPath = Root }),
            Overrides);
        _inner = inner;
    }

    private readonly MembershipHarness _inner;

    public async Task<Member360Dto> CreateMemberAsync()
    {
        var created = await Members.CreateAsync(new MemberWriteRequest
        {
            FirstName = "Marie",
            LastName = "Joseph",
            Cin = "CIN-KYC-01",
            Phone = "+509 2812 0000",
            AddressLine = "Rue Test",
            City = "Pétion-Ville",
            Status = MemberStatus.Active,
            KycStatus = KycStatus.Verified,
            LegalStatus = LegalStatus.Societaire,
            QualificationShareCount = 1
        });
        Assert.True(created.IsSuccess, created.ErrorMessage);
        return created.Value!;
    }

    public void Dispose()
    {
        _inner.Dispose();
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // temp cleanup is best-effort
        }
    }

    private static void SeedGerant(CpcredoDbContext db)
    {
        var now = new DateTime(2026, 1, 2, 13, 0, 0, DateTimeKind.Utc);
        db.Roles.Add(new Role
        {
            Id = SeedGuids.RoleGerant,
            Name = RoleNames.Gerant,
            DisplayNameFr = "Gérant",
            DisplayNameHt = "Jeran",
            DisplayNameEn = "Manager",
            DescriptionFr = "Gérant",
            IsSystem = true,
            CreatedAtUtc = now
        });
        var hasher = new PasswordHasher<User>();
        var gerant = new User
        {
            Id = SeedGuids.GerantUserId,
            TenantId = SeedGuids.TenantId,
            BranchId = SeedGuids.BranchId,
            Username = "gerant",
            Email = "gerant@cpcredo.ht",
            FullName = "Gérant CPCREDO",
            PasswordHash = hasher.HashPassword(new User { Username = "gerant" }, GerantPassword),
            CreatedAtUtc = now
        };
        db.Users.Add(gerant);
        db.UserRoles.Add(new UserRole { UserId = gerant.Id, RoleId = SeedGuids.RoleGerant });
        db.SaveChanges();
    }
}
