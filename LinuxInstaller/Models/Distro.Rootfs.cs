namespace LinuxInstaller.Models;

public partial class Distro
{
    public string RootfsId { get; set; } = string.Empty;
    public string RootfsFileName { get; set; } = string.Empty;
    public string RootfsArchitecture { get; set; } = string.Empty;
    public string RootfsSha256 { get; set; } = string.Empty;
}
