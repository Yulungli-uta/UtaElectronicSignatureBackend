using System.Text.RegularExpressions;
namespace UtaElectronicSignature.SecurityTests;
public sealed class SecurityTests
{
 [Fact] public void Source_files_do_not_contain_private_key_markers()
 {
  var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..","..","src"));
  foreach(var file in Directory.EnumerateFiles(root,"*.cs",SearchOption.AllDirectories))
   Assert.DoesNotContain("BEGIN PRIVATE KEY",File.ReadAllText(file),StringComparison.Ordinal);
 }
}
