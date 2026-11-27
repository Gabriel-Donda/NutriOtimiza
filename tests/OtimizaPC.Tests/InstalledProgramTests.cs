using System;
using System.Collections.Generic;
using Microsoft.Win32;
using OtimizaPC.Tweaks.Uninstall;
using Xunit;

namespace OtimizaPC.Tests
{
    public class InstalledProgramTests
    {
        [Fact]
        public void ParsesFieldsAndRegistrySource()
        {
            var values = NewValues();
            values["DisplayVersion"] = "2.0";
            values["Publisher"] = "Editora";
            values["InstallDate"] = "20240229";
            values["EstimatedSize"] = 4096;
            values["InstallLocation"] = @"C:\Programa";
            values["DisplayIcon"] = @"C:\Programa\app.exe,0";
            values["QuietUninstallString"] = "uninstall.exe /quiet";

            var program = InstalledProgramService.Parse(values, RegistryHive.CurrentUser, RegistryView.Registry32, "program");

            Assert.Equal("Programa", program.DisplayName);
            Assert.Equal("2.0", program.DisplayVersion);
            Assert.Equal("Editora", program.Publisher);
            Assert.Equal<DateTime?>(new DateTime(2024, 2, 29), program.InstallDate);
            Assert.Equal<long?>(4096, program.EstimatedSizeKb);
            Assert.Equal(@"C:\Programa", program.InstallLocation);
            Assert.Equal(@"C:\Programa\app.exe,0", program.DisplayIcon);
            Assert.Equal("uninstall.exe", program.UninstallString);
            Assert.Equal("uninstall.exe /quiet", program.QuietUninstallString);
            Assert.Equal(RegistryHive.CurrentUser, program.Hive);
            Assert.Equal(RegistryView.Registry32, program.View);
            Assert.Equal("program", program.SubKeyName);
        }

        [Theory]
        [InlineData("20230229")]
        [InlineData("2024-02-29")]
        [InlineData(null)]
        public void LeavesInvalidDateAndMissingSizeUnset(string installDate)
        {
            var values = NewValues();
            values["InstallDate"] = installDate;

            var program = Parse(values);

            Assert.Null(program.InstallDate);
            Assert.Null(program.EstimatedSizeKb);
        }

        [Theory]
        [InlineData("DisplayName", " ")]
        [InlineData("UninstallString", null)]
        [InlineData("SystemComponent", 1)]
        [InlineData("ParentKeyName", "parent")]
        [InlineData("ReleaseType", "Security Update")]
        [InlineData("ReleaseType", "Cumulative UPDATE")]
        [InlineData("ReleaseType", "Hotfix")]
        public void FiltersNonProgramEntries(string field, object value)
        {
            var values = NewValues();
            values[field] = value;
            values["QuietUninstallString"] = "uninstall.exe /quiet";

            Assert.Null(Parse(values));
        }

        [Theory]
        [InlineData("program", 1, null, true)]
        [InlineData("{01234567-89AB-CDEF-0123-456789ABCDEF}", 0, null, true)]
        [InlineData("program", 0, "{01234567-89AB-CDEF-0123-456789ABCDEF}", true)]
        [InlineData("program", 0, "invalid", false)]
        public void DetectsMsi(string subKey, int windowsInstaller, string productCode, bool expected)
        {
            var values = NewValues();
            values["WindowsInstaller"] = windowsInstaller;
            values["ProductCode"] = productCode;

            Assert.Equal(expected, Parse(values, subKey).IsMsi);
        }

        [Fact]
        public void DeduplicatesByNameVersionAndPublisherIgnoringCase()
        {
            var first = Parse(NewValues("Programa", "1.0", "Editora"), "first");
            var duplicate = InstalledProgramService.Parse(NewValues("PROGRAMA", "1.0", "EDITORA"),
                RegistryHive.CurrentUser, RegistryView.Registry32, "duplicate");
            var otherVersion = Parse(NewValues("Programa", "2.0", "Editora"));
            var otherPublisher = Parse(NewValues("Programa", "1.0", "Outra editora"));
            var otherName = Parse(NewValues("Outro programa", "1.0", "Editora"));

            var programs = InstalledProgramService.Deduplicate(new[] { first, duplicate, otherVersion, otherPublisher, otherName });

            Assert.Collection(programs,
                program => Assert.Same(first, program),
                program => Assert.Same(otherVersion, program),
                program => Assert.Same(otherPublisher, program),
                program => Assert.Same(otherName, program));
        }

        private static Dictionary<string, object> NewValues(string name = "Programa", string version = null, string publisher = null) =>
            new Dictionary<string, object>
            {
                ["DisplayName"] = name,
                ["DisplayVersion"] = version,
                ["Publisher"] = publisher,
                ["UninstallString"] = "uninstall.exe"
            };

        private static InstalledProgram Parse(Dictionary<string, object> values, string subKey = "program") =>
            InstalledProgramService.Parse(values, RegistryHive.LocalMachine, RegistryView.Registry64, subKey);
    }
}
