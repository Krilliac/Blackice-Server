using BlackIce.Photon;
using BlackIce.Server.Data;
using BlackIce.Server.LoadBalancing;
using Xunit;

namespace BlackIce.Server.Tests;

public class IdentityTests
{
    const byte PUserId = 225;
    const string Secret = "test-secret";

    [Fact]
    public void NameServer_creates_account_and_returns_token_for_steamid()
    {
        var accounts = TestAccounts.Create();
        var ns = new NameServerHandler("127.0.0.1:5055", Secret, accounts);

        var resp = ns.Authenticate(new OperationRequest(230, new() { { PUserId, "76561198000000009" } }));
        Assert.Equal(0, resp.ReturnCode);
        Assert.NotNull(accounts.Find("76561198000000009"));   // account auto-created
        Assert.Equal(PlayerLevel.Player, accounts.Find("76561198000000009")!.Level);
        Assert.True(resp.Parameters.ContainsKey(221));         // token minted
    }

    [Fact]
    public void NameServer_refuses_banned_account()
    {
        var accounts = TestAccounts.Create();
        accounts.ResolveOrCreate("banned-id", "x");
        accounts.SetBanned("banned-id", true);
        var ns = new NameServerHandler("127.0.0.1:5055", Secret, accounts);

        var resp = ns.Authenticate(new OperationRequest(230, new() { { PUserId, "banned-id" } }));
        Assert.NotEqual(0, resp.ReturnCode);
    }

    [Fact]
    public void Master_refuses_banned_account_via_token()
    {
        var accounts = TestAccounts.Create();
        accounts.ResolveOrCreate("banned-2", "x");
        accounts.SetBanned("banned-2", true);
        var master = new MasterServerHandler("127.0.0.1:5056", Secret, new RoomRegistry(), accounts: accounts);

        var token = AuthToken.Mint("banned-2", Secret);
        var resp = master.Authenticate(new OperationRequest(230, new() { { 221, token } }));
        Assert.Equal(-3, resp.ReturnCode);
    }
}
