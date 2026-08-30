using Windows.Management.Deployment;

namespace YFTimeTracker.Windows.Processes;

internal sealed record XboxPackageInfo(
    string PackageName,
    string PackageFamilyName,
    string? DisplayName,
    string? EffectiveLocationPath);

internal interface IXboxPackageCatalog
{
    IReadOnlyList<XboxPackageInfo> GetInstalledPackages();
}

internal sealed class WindowsXboxPackageCatalog : IXboxPackageCatalog
{
    public IReadOnlyList<XboxPackageInfo> GetInstalledPackages()
    {
        var packageManager = new PackageManager();
        var packages = new List<XboxPackageInfo>();

        foreach (var package in packageManager.FindPackagesForUser(string.Empty))
        {
            try
            {
                if (package.IsFramework || package.IsResourcePackage)
                {
                    continue;
                }

                packages.Add(new XboxPackageInfo(
                    package.Id.Name,
                    package.Id.FamilyName,
                    package.DisplayName,
                    package.EffectiveLocation?.Path));
            }
            catch (Exception)
            {
                // A single protected or partially removed package must not block other Xbox games.
            }
        }

        return packages;
    }
}
