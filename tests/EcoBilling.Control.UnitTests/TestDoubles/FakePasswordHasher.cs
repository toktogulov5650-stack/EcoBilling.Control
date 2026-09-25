using EcoBilling.Control.Application.Abstractions;

namespace EcoBilling.Control.UnitTests.TestDoubles;

/// <summary>
/// A deliberately weak, fast, fully predictable stand-in for real PBKDF2 hashing --
/// tests only need "the same input hashes the same way and a wrong password fails,"
/// not the real algorithm's cost.
/// </summary>
internal sealed class FakePasswordHasher : IPasswordHasher
{
    public FakePasswordHasher() => DummyHash = Hash("__dummy_password_for_timing_safety__");

    public string DummyHash { get; }

    public string Hash(string plainPassword) => $"hashed:{plainPassword}";

    public bool Verify(string hash, string plainPassword) => hash == Hash(plainPassword);
}
