namespace UpgradeCaptain.Harness.Tests;

public class AuditLogTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => this._temp.Dispose();

    [Fact]
    public void Entries_AreHashChained_AndVerify()
    {
        var audit = new AuditLog(this._temp.Path);
        AuditEntry first = audit.Append("requested", "job-001", "@dependabot", new { tool = "file_access_replace" });
        AuditEntry second = audit.Append("parked", "job-001", "harness", new { owners = new[] { "@maria-payments" } }, "APR-0001");
        audit.Append("decided", "job-001", "@maria-payments", new { approved = true, reason = "Revisado: tarifa en km" }, "APR-0001");

        Assert.Equal(AuditLog.Genesis, first.PrevHash);
        Assert.Equal(first.Hash, second.PrevHash);
        Assert.Equal(new AuditVerification(true, 3, null, null), audit.Verify());
    }

    [Fact]
    public void EditingAnEntry_BreaksTheChain()
    {
        var audit = new AuditLog(this._temp.Path);
        audit.Append("decided", "job-001", "@maria-payments", new { approved = false }, "APR-0001");
        audit.Append("resumed", "job-001", "harness");

        string[] lines = File.ReadAllLines(audit.FilePath);
        lines[0] = lines[0].Replace("\"approved\":false", "\"approved\":true", StringComparison.Ordinal);
        File.WriteAllLines(audit.FilePath, lines);

        AuditVerification check = audit.Verify();
        Assert.False(check.Ok);
        Assert.Equal(1, check.BrokenAtSeq);
    }

    [Fact]
    public void DeletingAnEntry_BreaksTheChain()
    {
        var audit = new AuditLog(this._temp.Path);
        audit.Append("requested", "job-001", "@dependabot");
        audit.Append("parked", "job-001", "harness");
        audit.Append("decided", "job-001", "@maria-payments");

        var lines = File.ReadAllLines(audit.FilePath).ToList();
        lines.RemoveAt(1);
        File.WriteAllLines(audit.FilePath, lines);

        AuditVerification check = audit.Verify();
        Assert.False(check.Ok);
        Assert.Equal(3, check.BrokenAtSeq);
    }
}
