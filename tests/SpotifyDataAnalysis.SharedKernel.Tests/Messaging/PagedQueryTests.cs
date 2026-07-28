using SpotifyDataAnalysis.SharedKernel.Messaging;

namespace SpotifyDataAnalysis.SharedKernel.Tests.Messaging;

public sealed class PagedQueryTests
{
    private sealed record SampleQuery : PagedQuery<PagedResult<string>>;

    [Fact]
    public void FirstAndLastResult_ComputeRowNumberBounds()
    {
        var query = new SampleQuery { Page = 3, PageSize = 20 };

        Assert.Equal(41, query.FirstResult); // ((3 - 1) * 20) + 1
        Assert.Equal(60, query.LastResult);  // 3 * 20
    }

    [Fact]
    public void Page_BelowOne_IsClampedToOne()
    {
        var query = new SampleQuery { Page = 0 };

        Assert.Equal(1, query.Page);
    }

    [Fact]
    public void PageSize_IsClampedToTheAllowedRange()
    {
        Assert.Equal(1, (new SampleQuery { PageSize = 0 }).PageSize);
        Assert.Equal(200, (new SampleQuery { PageSize = 5000 }).PageSize);
    }

    [Fact]
    public void PagedResult_TotalPages_RoundsUp()
    {
        var result = new PagedResult<string>(["a", "b"], totalCount: 21, page: 1, pageSize: 20);

        Assert.Equal(2, result.TotalPages);
    }
}
