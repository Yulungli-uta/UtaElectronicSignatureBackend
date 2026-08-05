namespace UtaElectronicSignature.ArchitectureTests;
public sealed class ArchitectureTests
{
 [Fact] public void Domain_project_has_no_package_references()
 {
  var file=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..","..","src","UtaElectronicSignature.Domain","UtaElectronicSignature.Domain.csproj"));
  Assert.DoesNotContain("PackageReference",File.ReadAllText(file),StringComparison.Ordinal);
 }
}
