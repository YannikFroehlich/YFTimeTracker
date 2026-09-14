using YFTimeTracker.App.Controls;

namespace YFTimeTracker.Windows.Tests.Controls;

[TestClass]
public sealed class IconDecodeSizeTests
{
    [TestMethod]
    [DataRow(1d, 48)]
    [DataRow(1.25d, 64)]
    [DataRow(1.5d, 80)]
    [DataRow(2d, 96)]
    [DataRow(3d, 144)]
    public void Decodes_enough_physical_pixels_at_each_display_scale(double scale, int expected)
    {
        Assert.AreEqual(expected, IconDecodeSize.ForLogicalSize(48, 48, scale));
    }

    [TestMethod]
    public void Large_portrait_cover_keeps_detail_at_high_dpi()
    {
        Assert.AreEqual(752, IconDecodeSize.ForLogicalSize(160, 250, 3));
    }

    [TestMethod]
    public void Adjacent_sizes_share_a_decode_step_without_losing_resolution()
    {
        Assert.AreEqual(64, IconDecodeSize.ForLogicalSize(47, 47, 1.25));
        Assert.AreEqual(64, IconDecodeSize.ForLogicalSize(48, 48, 1.25));
        Assert.AreEqual(64, IconDecodeSize.ForLogicalSize(49, 49, 1.25));
    }

    [TestMethod]
    public void Missing_layout_and_invalid_scale_use_a_bounded_fallback()
    {
        Assert.AreEqual(96, IconDecodeSize.ForLogicalSize(double.NaN, 0, double.NaN));
        Assert.AreEqual(192, IconDecodeSize.ForLogicalSize(0, 0, 2));
        Assert.AreEqual(32, IconDecodeSize.ForLogicalSize(1, 1, 1));
        Assert.AreEqual(1024, IconDecodeSize.ForLogicalSize(5000, 5000, 3));
    }
}
