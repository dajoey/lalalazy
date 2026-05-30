using System.Text;

namespace LazyFateAutomation.Helpers.Utils;

public enum CommandArgumentKind {
    Word,
    Int,
    Rest,
}

public readonly record struct CommandExecutionResult(bool Success, string? Error = null, string? Usage = null, string? Help = null) {
    public static CommandExecutionResult Ok(string? help = null) => new(true, null, null, help);
    public static CommandExecutionResult Fail(string error, string? usage = null) => new(false, error, usage);
}

public sealed class CommandValues {
    private readonly Dictionary<string, object> _values = [];

    internal void Set(string key, object value) => _values[key] = value;

    public bool Has(string key) => _values.ContainsKey(key);

    public T Get<T>(string key) => _values.TryGetValue(key, out var value) && value is T typed
        ? typed
        : throw new KeyNotFoundException($"Argument '{key}' is missing or not of type {typeof(T).Name}.");

    public bool TryGet<T>(string key, out T value) {
        if (_values.TryGetValue(key, out var item) && item is T typed) {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }
}

public sealed record CommandArgument(
    string Name,
    CommandArgumentKind Kind,
    bool Optional = false,
    int? MinInt = null,
    int? MaxInt = null
);

public sealed class CommandNode<TContext> {
    private readonly List<CommandNode<TContext>> _children = [];
    private readonly List<CommandArgument> _arguments = [];
    private readonly HashSet<string> _aliases;

    private CommandNode(string name, string description, IEnumerable<string>? aliases = null, bool isRoot = false) {
        Name = name;
        Description = description;
        IsRoot = isRoot;
        _aliases = (aliases ?? [])
            .Append(name)
            .Select(a => a.Trim())
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public string Name { get; }
    public string Description { get; private set; }
    public bool IsRoot { get; }
    public IReadOnlyList<CommandNode<TContext>> Children => _children;
    public IReadOnlyList<CommandArgument> Arguments => _arguments;
    internal Action<TContext, CommandValues>? Handler { get; private set; }
    internal Action<TContext>? DefaultHandler { get; private set; }

    public static CommandNode<TContext> Root(string description = "Root command")
        => new("root", description, isRoot: true);

    public CommandNode<TContext> Describe(string description) {
        Description = description;
        return this;
    }

    public CommandNode<TContext> Alias(params string[] aliases) {
        foreach (var alias in aliases.Where(a => !string.IsNullOrWhiteSpace(a)))
            _aliases.Add(alias.Trim());
        return this;
    }

    public CommandNode<TContext> Sub(string name, string description, Action<CommandNode<TContext>>? configure = null) {
        var child = new CommandNode<TContext>(name.Trim(), description);
        _children.Add(child);
        configure?.Invoke(child);
        return this;
    }

    public CommandNode<TContext> ArgWord(string name, bool optional = false) {
        _arguments.Add(new(name, CommandArgumentKind.Word, optional));
        return this;
    }

    public CommandNode<TContext> ArgInt(string name, int? min = null, int? max = null, bool optional = false) {
        _arguments.Add(new(name, CommandArgumentKind.Int, optional, min, max));
        return this;
    }

    public CommandNode<TContext> ArgRest(string name, bool optional = false) {
        if (_arguments.Any(a => a.Kind == CommandArgumentKind.Rest))
            throw new InvalidOperationException("Only one rest argument is allowed.");
        _arguments.Add(new(name, CommandArgumentKind.Rest, optional));
        return this;
    }

    public CommandNode<TContext> Handle(Action<TContext, CommandValues> handler) {
        Handler = handler;
        return this;
    }

    public CommandNode<TContext> Default(Action<TContext> handler) {
        DefaultHandler = handler;
        return this;
    }

    internal bool Matches(string token) => _aliases.Contains(token);
}

public sealed class CommandRouter<TContext>(CommandNode<TContext> root) {
    public CommandExecutionResult Execute(string arguments, TContext context, string rootLabel = "") {
        var tokenize = CommandTokenizer.Tokenize(arguments);
        if (!tokenize.Success)
            return CommandExecutionResult.Fail(tokenize.Error!, BuildUsage(root, rootLabel));

        var tokens = tokenize.Tokens!;
        if (tokens.Count > 0 && IsHelpToken(tokens[0]))
            return CommandExecutionResult.Ok(BuildHelpForPath(root, rootLabel, tokens.Skip(1)));

        var node = root;
        var path = new List<string>();
        var index = 0;

        while (index < tokens.Count) {
            if (IsHelpToken(tokens[index]))
                return CommandExecutionResult.Ok(BuildHelp(node, rootLabel, path));

            var child = node.Children.FirstOrDefault(c => c.Matches(tokens[index]));
            if (child is null)
                break;
            path.Add(child.Name);
            node = child;
            index++;
        }

        if (index < tokens.Count && IsHelpToken(tokens[index]))
            return CommandExecutionResult.Ok(BuildHelp(node, rootLabel, path));

        if (index == tokens.Count && node.Handler is null && node.DefaultHandler is not null) {
            node.DefaultHandler(context);
            return CommandExecutionResult.Ok();
        }

        var values = new CommandValues();
        foreach (var argument in node.Arguments) {
            if (argument.Kind == CommandArgumentKind.Rest) {
                if (index >= tokens.Count) {
                    if (!argument.Optional)
                        return CommandExecutionResult.Fail($"Missing argument '{argument.Name}'.", BuildUsage(node, rootLabel, path));
                    continue;
                }

                values.Set(argument.Name, string.Join(' ', tokens.Skip(index)));
                index = tokens.Count;
                continue;
            }

            if (index >= tokens.Count) {
                if (!argument.Optional)
                    return CommandExecutionResult.Fail($"Missing argument '{argument.Name}'.", BuildUsage(node, rootLabel, path));
                continue;
            }

            if (argument.Kind == CommandArgumentKind.Word) {
                values.Set(argument.Name, tokens[index]);
                index++;
                continue;
            }

            if (!int.TryParse(tokens[index], out var intValue))
                return CommandExecutionResult.Fail($"Argument '{argument.Name}' must be a number.", BuildUsage(node, rootLabel, path));
            if (argument.MinInt is { } min && intValue < min)
                return CommandExecutionResult.Fail($"Argument '{argument.Name}' must be >= {min}.", BuildUsage(node, rootLabel, path));
            if (argument.MaxInt is { } max && intValue > max)
                return CommandExecutionResult.Fail($"Argument '{argument.Name}' must be <= {max}.", BuildUsage(node, rootLabel, path));

            values.Set(argument.Name, intValue);
            index++;
        }

        if (index < tokens.Count) {
            if (node.Children.Count > 0) {
                var options = string.Join(", ", node.Children.Select(c => c.Name));
                return CommandExecutionResult.Fail($"Unknown subcommand '{tokens[index]}'. Available: {options}", BuildUsage(node, rootLabel, path));
            }
            return CommandExecutionResult.Fail("Too many arguments.", BuildUsage(node, rootLabel, path));
        }

        if (node.Handler is null) {
            if (node.DefaultHandler is not null) {
                node.DefaultHandler(context);
                return CommandExecutionResult.Ok();
            }
            return CommandExecutionResult.Fail("Command has no handler.", BuildUsage(node, rootLabel, path));
        }

        node.Handler(context, values);
        return CommandExecutionResult.Ok();
    }

    public string Usage(string rootLabel = "") => BuildUsage(root, rootLabel);

    private static bool IsHelpToken(string token)
        => token.Equals("help", StringComparison.OrdinalIgnoreCase)
        || token.Equals("?", StringComparison.OrdinalIgnoreCase)
        || token.Equals("-h", StringComparison.OrdinalIgnoreCase)
        || token.Equals("--help", StringComparison.OrdinalIgnoreCase);

    private static string BuildHelpForPath(CommandNode<TContext> startNode, string rootLabel, IEnumerable<string> pathTokens) {
        var node = startNode;
        var path = new List<string>();

        foreach (var token in pathTokens) {
            var child = node.Children.FirstOrDefault(c => c.Matches(token));
            if (child is null)
                break;
            node = child;
            path.Add(child.Name);
        }

        return BuildHelp(node, rootLabel, path);
    }

    private static string BuildHelp(CommandNode<TContext> node, string rootLabel, IReadOnlyList<string>? path = null) {
        var lines = new List<string>();
        CollectHelpLines(node, rootLabel, path?.ToList() ?? [], lines);
        return lines.Count == 0 ? BuildUsage(node, rootLabel, path) : string.Join('\n', lines);
    }

    private static void CollectHelpLines(CommandNode<TContext> node, string rootLabel, List<string> path, List<string> lines) {
        if (!node.IsRoot) {
            var usage = BuildUsage(node, rootLabel, path);
            lines.Add(string.IsNullOrWhiteSpace(node.Description) ? usage : $"{usage} - {node.Description}");
        }

        foreach (var child in node.Children.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)) {
            var childPath = new List<string>(path) { child.Name };
            CollectHelpLines(child, rootLabel, childPath, lines);
        }
    }

    private static string BuildUsage(CommandNode<TContext> node, string rootLabel, IEnumerable<string>? path = null) {
        var sb = new StringBuilder("Usage: ");
        if (!string.IsNullOrWhiteSpace(rootLabel))
            sb.Append(rootLabel.Trim()).Append(' ');

        if (path is not null) {
            foreach (var segment in path)
                sb.Append(segment).Append(' ');
        }

        foreach (var argument in node.Arguments) {
            var wrapperStart = argument.Optional ? "[" : "<";
            var wrapperEnd = argument.Optional ? "]" : ">";
            sb.Append(wrapperStart).Append(argument.Name).Append(wrapperEnd).Append(' ');
        }

        if (node.Children.Count > 0 && node.Handler is null)
            sb.Append('{').Append(string.Join("|", node.Children.Select(c => c.Name))).Append('}');

        return sb.ToString().TrimEnd();
    }
}

public static class CommandTokenizer {
    public static TokenizeResult Tokenize(string? input) {
        if (string.IsNullOrWhiteSpace(input))
            return TokenizeResult.Ok([]);

        var tokens = new List<string>();
        var sb = new StringBuilder();
        char? quote = null;
        var escaped = false;

        foreach (var ch in input!) {
            if (escaped) {
                sb.Append(ch);
                escaped = false;
                continue;
            }

            if (ch == '\\') {
                escaped = true;
                continue;
            }

            if (quote is not null) {
                if (ch == quote.Value) {
                    quote = null;
                    continue;
                }
                sb.Append(ch);
                continue;
            }

            if (ch is '"' or '\'') {
                quote = ch;
                continue;
            }

            if (char.IsWhiteSpace(ch)) {
                if (sb.Length > 0) {
                    tokens.Add(sb.ToString());
                    sb.Clear();
                }
                continue;
            }

            sb.Append(ch);
        }

        if (escaped)
            sb.Append('\\');

        if (quote is not null)
            return TokenizeResult.Fail("Unterminated quote in command arguments.");

        if (sb.Length > 0)
            tokens.Add(sb.ToString());

        return TokenizeResult.Ok(tokens);
    }
}

public readonly record struct TokenizeResult(bool Success, List<string>? Tokens = null, string? Error = null) {
    public static TokenizeResult Ok(List<string> tokens) => new(true, tokens, null);
    public static TokenizeResult Fail(string error) => new(false, null, error);
}
