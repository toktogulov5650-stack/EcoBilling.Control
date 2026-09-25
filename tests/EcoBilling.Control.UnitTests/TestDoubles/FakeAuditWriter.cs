using EcoBilling.Control.Application.Abstractions;

namespace EcoBilling.Control.UnitTests.TestDoubles;

/// <summary>Records every entry written, without persisting anything, so a test can assert exactly what was audited.</summary>
internal sealed class FakeAuditWriter : IAuditWriter
{
    public List<AuditEntry> Entries { get; } = [];

    public void Write(AuditEntry entry) => Entries.Add(entry);
}
