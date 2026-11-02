using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UpgradeCaptain.Harness;

namespace UpgradeCaptain.Harness.Tests;

/// <summary>Modelo guionizado: cada llamada devuelve la siguiente respuesta de la cola (o la calcula).</summary>
internal sealed class ScriptedChatClient : IChatClient
{
    private readonly Func<IReadOnlyList<ChatMessage>, int, ChatMessage> _next;
    private int _calls;

    public ScriptedChatClient(Func<IReadOnlyList<ChatMessage>, int, ChatMessage> next, long tokensPerCall = 0)
    {
        this._next = next;
        this.TokensPerCall = tokensPerCall;
    }

    public long TokensPerCall { get; }

    public int Calls => this._calls;

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        int call = Interlocked.Increment(ref this._calls);
        var response = new ChatResponse(this._next(messages.ToList(), call));
        if (this.TokensPerCall > 0)
        {
            response.Usage = new UsageDetails { InputTokenCount = this.TokensPerCall - 10, OutputTokenCount = 10, TotalTokenCount = this.TokensPerCall };
        }

        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ChatResponse response = await this.GetResponseAsync(messages, options, cancellationToken);
        foreach (ChatResponseUpdate update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

/// <summary>Verificador falso: devuelve los resultados encolados, sin ejecutar dotnet.</summary>
internal sealed class FakeVerifier(params VerificationResult[] results) : RepoVerifier("/tmp/fake-repo", "/tmp/none.json")
{
    private readonly Queue<VerificationResult> _results = new(results);

    public int Calls { get; private set; }

    public override Task<VerificationResult> VerifyAsync(CancellationToken cancellationToken = default)
    {
        this.Calls++;
        return Task.FromResult(this._results.Count > 1 ? this._results.Dequeue() : this._results.Peek());
    }
}

/// <summary>Checkpoints falsos: registran las llamadas, sin tocar git.</summary>
internal sealed class FakeCheckpoints(string stateRoot) : GitCheckpoints("/tmp/fake-repo", stateRoot)
{
    public List<string> Commits { get; } = [];

    public List<string> Restores { get; } = [];

    public override Task EnsureBaselineAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public override Task<string> CommitGreenAsync(string message, CancellationToken cancellationToken = default)
    {
        this.Commits.Add(message);
        return Task.FromResult("abc1234def5678");
    }

    public override Task<string> RestoreLastGreenAsync(string reason = "restore", CancellationToken cancellationToken = default)
    {
        this.Restores.Add(reason);
        return Task.FromResult("0000000baseline");
    }
}

internal static class Results
{
    public static VerificationResult Green() => new()
    {
        BuildSucceeded = true,
        Errors = [],
        TestsPassed = true,
        TestsTotal = 24,
        TestsFailed = 0,
        FailedTests = [],
        VulnerablePackages = [],
        Duration = TimeSpan.FromSeconds(6),
    };

    public static VerificationResult FareTestsRed() => new()
    {
        BuildSucceeded = true,
        Errors = [],
        TestsPassed = false,
        TestsTotal = 24,
        TestsFailed = 1,
        FailedTests =
        [
            new FailedTest(
                "Canarias.Tours.Tests.FareTests.Quote_LosCristianosToTeide_Is33_66Eur",
                "Assert.Equal() Failure: Values differ | Expected: 33.66 | Actual:   30663.76",
                "tests/Canarias.Tours.Tests/FareTests.cs:21"),
        ],
        VulnerablePackages = [],
        Duration = TimeSpan.FromSeconds(7),
    };

    public static VerificationResult BuildRed() => new()
    {
        BuildSucceeded = false,
        Errors = ["src/Canarias.Tours.Domain/RouteLeg.cs:5 CS0246 The type or namespace name 'GeoPoint' could not be found"],
        TestsPassed = false,
        FailedTests = [],
        VulnerablePackages = [],
        Duration = TimeSpan.FromSeconds(3),
    };
}

/// <summary>Directorio temporal que se borra al terminar el test.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        this.Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "uc-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(this.Path);
    }

    public string Path { get; }

    public string Sub(string relative)
    {
        string full = System.IO.Path.Combine(this.Path, relative);
        Directory.CreateDirectory(full);
        return full;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(this.Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
