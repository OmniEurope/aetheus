// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;

namespace Aetheus.Front.Tests.Pages;

public class SecretVersionHistoryDialogTests : BunitContext
{
    public SecretVersionHistoryDialogTests()
    {
        BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void Renders_EmptyList()
    {
        var cut = Render<SecretVersionHistoryDialog>(p =>
            p.Add(x => x.Versions, new List<VaultSecretVersionDto>()));

        // The history grid renders its column headers even with no version rows.
        Assert.Contains("Key", cut.Markup);
        Assert.Contains("Date", cut.Markup);
        Assert.Empty(cut.FindAll(".rz-data-row"));
    }

    [Fact]
    public void Renders_Versions()
    {
        var versions = new List<VaultSecretVersionDto>
        {
            new() { Version = 1, Key = "DB_PASS", ChangeType = ChangeType.Created, ChangedAt = new DateTime(2026, 1, 1) },
            new() { Version = 2, Key = "DB_PASS", ChangeType = ChangeType.Updated, ChangedAt = new DateTime(2026, 2, 1) },
            new() { Version = 3, Key = "DB_PASS", ChangeType = ChangeType.Deleted, ChangedAt = new DateTime(2026, 3, 1) }
        };
        var cut = Render<SecretVersionHistoryDialog>(p => p.Add(x => x.Versions, versions));

        Assert.Contains("DB_PASS", cut.Markup);
    }

    [Fact]
    public void Renders_SingleVersion()
    {
        var versions = new List<VaultSecretVersionDto>
        {
            new() { Version = 1, Key = "API_KEY", ChangeType = ChangeType.Created, ChangedAt = DateTime.UtcNow }
        };
        var cut = Render<SecretVersionHistoryDialog>(p => p.Add(x => x.Versions, versions));

        Assert.Contains("API_KEY", cut.Markup);
    }
}
