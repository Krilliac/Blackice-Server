namespace BlackIce.Server.Data;

/// <summary>Parses and executes a single server-console admin line, returning text output.</summary>
public sealed class ConsoleCommandProcessor
{
    private readonly AccountService _accounts;
    public ConsoleCommandProcessor(AccountService accounts) => _accounts = accounts;

    public string Execute(string line)
    {
        var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "";
        switch (parts[0].ToLowerInvariant())
        {
            case "promote" or "demote" when parts.Length == 3 && int.TryParse(parts[2], out var lvl) && lvl is >= 0 and <= 3:
                return _accounts.SetLevel(parts[1], (PlayerLevel)lvl)
                    ? $"{parts[1]} -> {(PlayerLevel)lvl}" : $"no such account: {parts[1]}";
            case "ban" when parts.Length == 2:
                return _accounts.SetBanned(parts[1], true) ? $"banned {parts[1]}" : $"no such account: {parts[1]}";
            case "unban" when parts.Length == 2:
                return _accounts.SetBanned(parts[1], false) ? $"unbanned {parts[1]}" : $"no such account: {parts[1]}";
            case "list":
                return string.Join('\n', _accounts.All().Select(a =>
                    $"{a.SteamId} {a.DisplayName} {a.Level}{(a.IsBanned ? " [BANNED]" : "")}"));
            case "code":
                return $"bootstrap code: {_accounts.EnsureBootstrapCode()}";
            case "help":
                return Help;
            default:
                return $"unknown command '{parts[0]}'. type 'help'.";
        }
    }

    public const string Help =
        "commands: promote <steamId> <0-3> | demote <steamId> <0-3> | ban <steamId> | unban <steamId> | list | code | help";
}
