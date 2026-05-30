using BlackIce.Server.Data;
using Xunit;

namespace BlackIce.Server.Data.Tests;

public class ConsoleCommandProcessorTests
{
    [Fact]
    public void Promote_sets_level()
    {
        using var db = new TestDb();
        var svc = new AccountService(db.Context);
        svc.ResolveOrCreate("s1", "x");
        var proc = new ConsoleCommandProcessor(svc);

        var output = proc.Execute("promote s1 2");
        Assert.Contains("Admin", output);
        Assert.Equal(PlayerLevel.Admin, svc.Find("s1")!.Level);
    }

    [Fact]
    public void Ban_marks_account()
    {
        using var db = new TestDb();
        var svc = new AccountService(db.Context);
        svc.ResolveOrCreate("s2", "x");
        var proc = new ConsoleCommandProcessor(svc);
        proc.Execute("ban s2");
        Assert.True(svc.Find("s2")!.IsBanned);
    }

    [Fact]
    public void Unknown_command_returns_help_hint()
    {
        using var db = new TestDb();
        var proc = new ConsoleCommandProcessor(new AccountService(db.Context));
        Assert.Contains("help", proc.Execute("frobnicate").ToLowerInvariant());
    }
}
