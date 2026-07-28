using SpotifyDataAnalysis.SharedKernel.Domain;

namespace SpotifyDataAnalysis.SharedKernel.Tests.Domain;

public sealed class EntityTests
{
    private sealed class SampleEntity : Entity<Guid>
    {
        public SampleEntity(Guid id) : base(id) { }
    }

    private sealed class OtherEntity : Entity<Guid>
    {
        public OtherEntity(Guid id) : base(id) { }
    }

    [Fact]
    public void Entities_WithSameId_AreEqual()
    {
        Guid id = Guid.NewGuid();
        var a = new SampleEntity(id);
        var b = new SampleEntity(id);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Entities_WithDifferentId_AreNotEqual()
    {
        var a = new SampleEntity(Guid.NewGuid());
        var b = new SampleEntity(Guid.NewGuid());

        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact]
    public void Entities_OfDifferentType_WithSameId_AreNotEqual()
    {
        Guid id = Guid.NewGuid();
        var a = new SampleEntity(id);
        var b = new OtherEntity(id);

        Assert.False(a.Equals(b));
    }
}
