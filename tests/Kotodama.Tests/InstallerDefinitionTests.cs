using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Kotodama.Tests;

public sealed class InstallerDefinitionTests
{
    private static readonly string PackagePath = Path.Combine(AppContext.BaseDirectory, "installer", "Package.wxs");

    [Fact]
    public void Package_UsesRequiredManufacturer()
    {
        var document = XDocument.Load(PackagePath);
        var wix = XNamespace.Get("http://wixtoolset.org/schemas/v4/wxs");

        document.Descendants(wix + "Package").Single()
            .Attribute("Manufacturer")?.Value.Should().Be("Akatsukisoft");
    }

    [Fact]
    public void Package_AllowsReinstallingSameVersionBuilds()
    {
        var document = XDocument.Load(PackagePath);
        var wix = XNamespace.Get("http://wixtoolset.org/schemas/v4/wxs");

        var majorUpgrade = document.Descendants(wix + "MajorUpgrade").Single();
        majorUpgrade.Attribute("AllowSameVersionUpgrades")?.Value.Should().Be("yes");
        majorUpgrade.Attribute("Schedule")?.Value.Should().Be("afterInstallInitialize");
    }

    [Fact]
    public void Package_InstallsForCurrentUserWithoutServiceOrMachinePath()
    {
        var document = XDocument.Load(PackagePath);
        var wix = XNamespace.Get("http://wixtoolset.org/schemas/v4/wxs");
        document.Descendants(wix + "Package").Single().Attribute("Scope")?.Value.Should().Be("perUser");
        document.Descendants(wix + "ServiceInstall").Should().BeEmpty();
        document.Descendants(wix + "ServiceControl").Should().BeEmpty();
        var startup = document.Descendants(wix + "RegistryValue").Single();
        startup.Attribute("Root")?.Value.Should().Be("HKCU");
        startup.Attribute("Key")?.Value.Should().EndWith(@"CurrentVersion\Run");
        startup.Attribute("Value")?.Value.Should().Contain("Kotodama.Tray.exe");
        document.Descendants(wix + "Environment").Single().Attribute("System")?.Value.Should().Be("no");
    }

    [Fact]
    public void Package_StopsTrayGracefullyBeforeFileValidation()
    {
        var document = XDocument.Load(PackagePath);
        var wix = XNamespace.Get("http://wixtoolset.org/schemas/v4/wxs");
        var action = document.Descendants(wix + "CustomAction")
            .Single(element => (string?)element.Attribute("Id") == "StopTray");
        action.Attribute("ExeCommand")?.Value.Should().Contain("Kotodama.Tray.exe").And.Contain("--stop");
        action.Attribute("Impersonate")?.Value.Should().Be("yes");
        document.Descendants(wix + "Custom")
            .Single(element => (string?)element.Attribute("Action") == "StopTray")
            .Attribute("Before")?.Value.Should().Be("InstallValidate");
    }

    [Fact]
    public void Package_InstallsOnePreservedCanonicalConfigurationFile()
    {
        var document = XDocument.Load(PackagePath);
        var wix = XNamespace.Get("http://wixtoolset.org/schemas/v4/wxs");
        var component = document.Descendants(wix + "Component")
            .Single(element => (string?)element.Attribute("Id") == "KotodamaConfiguration");

        component.Attribute("Permanent")?.Value.Should().Be("yes");
        component.Attribute("NeverOverwrite")?.Value.Should().Be("yes");
        component.Element(wix + "File")?.Attribute("Source")?.Value.Should().Be(@"installer\kotodama.json");
        component.Element(wix + "File")?.Attribute("Name")?.Value.Should().Be("kotodama.json");
        component.Element(wix + "File")?.Attribute("KeyPath")?.Value.Should().Be("yes");
        document.Descendants(wix + "ComponentRef")
            .Should().ContainSingle(element => (string?)element.Attribute("Id") == "KotodamaConfiguration");
    }
}
