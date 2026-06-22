using TZHJ.Gateway.Auth;

namespace TZHJ.Tests.Auth;

public class PasswordServiceTests
{
    private readonly PasswordService _svc = new();

    [Fact]
    public void Hash_is_not_plaintext_and_verifies()
    {
        var hash = _svc.Hash("Secret@123");
        Assert.NotEqual("Secret@123", hash);
        Assert.True(_svc.Verify(hash, "Secret@123"));
    }

    [Fact]
    public void Verify_rejects_wrong_password()
    {
        var hash = _svc.Hash("Secret@123");
        Assert.False(_svc.Verify(hash, "secret@123"));
        Assert.False(_svc.Verify(hash, "wrong"));
    }

    [Fact]
    public void Same_password_hashes_differ_due_to_salt()
    {
        Assert.NotEqual(_svc.Hash("Secret@123"), _svc.Hash("Secret@123"));
    }

    [Fact]
    public void Verify_empty_hash_is_false()
    {
        Assert.False(_svc.Verify("", "anything"));
    }
}
