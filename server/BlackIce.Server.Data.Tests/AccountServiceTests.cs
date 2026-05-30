using BlackIce.Server.Data;
using Xunit;

namespace BlackIce.Server.Data.Tests;

public class AccountServiceTests
{
    [Fact]
    public void Schema_supports_account_with_profile()
    {
        using var db = new TestDb();
        db.Context.Accounts.Add(new Account { SteamId = "76561198000000001", DisplayName = "Nate" });
        db.Context.SaveChanges();

        using var read = db.NewContext();
        var acct = read.Accounts.Find("76561198000000001");
        Assert.NotNull(acct);
        Assert.Equal(PlayerLevel.Player, acct!.Level);
        Assert.False(acct.IsBanned);
    }
}
