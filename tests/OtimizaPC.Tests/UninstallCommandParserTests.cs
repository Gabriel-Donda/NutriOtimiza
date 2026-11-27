using OtimizaPC.Tweaks.Uninstall;
using Xunit;

namespace OtimizaPC.Tests
{
    public class UninstallCommandParserTests
    {
        [Theory]
        [InlineData("  \"C:\\Program Files\\X\\unins000.exe\"  /arg \"dois valores\"  ", @"C:\Program Files\X\unins000.exe", "/arg \"dois valores\"")]
        [InlineData(@"C:\Program Files\X\unins000.exe /arg", @"C:\Program Files\X\unins000.exe", "/arg")]
        [InlineData(@"C:\Program Files\X\unins000.EXE", @"C:\Program Files\X\unins000.EXE", "")]
        [InlineData("rundll32.exe setup.dll,Uninstall", "rundll32.exe", "setup.dll,Uninstall")]
        public void SeparatesExecutableAndArguments(string command, string expectedFile, string expectedArguments)
        {
            Assert.True(UninstallCommandParser.TryParse(command, out var file, out var arguments));

            Assert.Equal(expectedFile, file);
            Assert.Equal(expectedArguments, arguments);
        }

        [Theory]
        [InlineData("MsiExec.exe /I{01234567-89ab-cdef-0123-456789abcdef}")]
        [InlineData("msiexec.exe /X {01234567-89AB-CDEF-0123-456789ABCDEF} /qn")]
        [InlineData("\"C:\\Windows\\System32\\MsiExec.exe\" /i \"{01234567-89AB-CDEF-0123-456789ABCDEF}\"")]
        [InlineData("msiexec /x {01234567-89AB-CDEF-0123-456789ABCDEF}")]
        public void ConvertsMsiToInteractiveUninstall(string command)
        {
            Assert.True(UninstallCommandParser.TryParse(command, out var file, out var arguments));

            Assert.Equal("msiexec.exe", file);
            Assert.Equal("/x {01234567-89AB-CDEF-0123-456789ABCDEF}", arguments);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("\"C:\\Programs\\uninstall.exe /arg")]
        [InlineData("\"C:\\Programs\\uninstall.exe\"/arg")]
        [InlineData("msiexec.exe /i product.msi")]
        [InlineData("msiexec.exe /x {00000000-0000-0000-0000-00000000000z}")]
        [InlineData("msiexec.exe /x {01234567-89AB-CDEF-0123-456789ABCDEF}suffix")]
        public void RejectsInvalidCommands(string command)
        {
            Assert.False(UninstallCommandParser.TryParse(command, out _, out _));
        }
    }
}
